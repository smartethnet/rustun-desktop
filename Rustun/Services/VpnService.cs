using Microsoft.UI.Dispatching;
using Rustun.Lib;
using System.Collections.Generic;
using Serilog;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Rustun.Services
{
    /// <summary>
    /// 应用层 VPN 入口：唯一对 UI/ViewModel 暴露连接状态。
    /// <see cref="RustunClient"/> 的 OnConnected/OnDisconnected 可能来自 DotNetty IO 线程，此处统一投递到 UI 线程后再通知。
    /// </summary>
    internal sealed class VpnService : INotifyPropertyChanged
    {
        private static readonly VpnService _instance = new();
        public static VpnService Instance => _instance;

        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _clientSync = new();
        private RustunClient? Client { get; set; }

        private DispatcherQueue? _uiDispatcher;
        private bool _isConnected;
        private DispatcherQueueTimer? _trafficTimer;
        private readonly TrafficStatistics _traffic = new();

        /// <summary>与底层 <see cref="RustunClient"/> 的隧道就绪状态一致；在 UI 线程上触发变更通知。</summary>
        public bool IsConnected => _isConnected;

        /// <summary>当前隧道累计上传字节（payload）。</summary>
        public long BytesUploaded => _traffic.BytesUploaded;

        /// <summary>当前隧道累计下载字节（payload）。</summary>
        public long BytesDownloaded => _traffic.BytesDownloaded;

        /// <summary>当前上传速率（B/s），由相邻采样点估算。</summary>
        public double UploadBytesPerSecond => _traffic.UploadBytesPerSecond;

        /// <summary>当前下载速率（B/s），由相邻采样点估算。</summary>
        public double DownloadBytesPerSecond => _traffic.DownloadBytesPerSecond;

        /// <summary>最近 30 分钟上传速率（B/s）样本，按时间先后排列（每秒一个点）。</summary>
        public IReadOnlyList<double> UploadSpeedSeries => _traffic.UploadSpeedSeries;

        /// <summary>最近 30 分钟下载速率（B/s）样本，按时间先后排列（每秒一个点）。</summary>
        public IReadOnlyList<double> DownloadSpeedSeries => _traffic.DownloadSpeedSeries;

        /// <summary>读取当前客户端隧道累计字节；未连接时为 0。可在任意线程调用。</summary>
        public void GetTrafficCounters(out long bytesUploaded, out long bytesDownloaded)
        {
            RustunClient? client;
            lock (_clientSync)
            {
                client = Client;
            }

            if (client is null)
            {
                bytesUploaded = 0;
                bytesDownloaded = 0;
                return;
            }

            bytesUploaded = client.BytesUploaded;
            bytesDownloaded = client.BytesDownloaded;
        }

        /// <summary>流量统计刷新（每秒一次，已投递到 UI 线程）。</summary>
        public event EventHandler? TrafficUpdated;

        /// <summary>
        /// 连接状态变化（true=已连接并可转发流量）。订阅此事件即可，无需再区分 OnConnected/OnDisconnected。
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        private VpnService()
        {
            _traffic.Updated += (_, _) => TrafficUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>在 UI 线程创建主窗口后调用一次，保证状态回调在 UI 线程派发。</summary>
        public void AttachUiDispatcher(DispatcherQueue dispatcher)
        {
            _uiDispatcher = dispatcher;
            EnsureTrafficSamplerStarted();
        }

        private void PostToUi(Action action)
        {
            if (_uiDispatcher is null || _uiDispatcher.HasThreadAccess)
            {
                action();
                return;
            }

            _ = _uiDispatcher.TryEnqueue(() => action());
        }

        private void SetIsConnected(bool value)
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            PostToUi(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
                ConnectionStateChanged?.Invoke(this, value);
            });
        }

        private void EnsureTrafficSamplerStarted()
        {
            if (_uiDispatcher is null)
            {
                return;
            }

            if (_trafficTimer is not null)
            {
                return;
            }

            _trafficTimer = _uiDispatcher.CreateTimer();
            _trafficTimer.Interval = TimeSpan.FromSeconds(1);
            _trafficTimer.IsRepeating = true;
            _trafficTimer.Tick += (_, _) => SampleTrafficOnUi();
            _trafficTimer.Start();

            // 初始化一次，避免首次进入页面看到旧值/空值
            SampleTrafficOnUi();
        }

        private void SampleTrafficOnUi()
        {
            // 该方法运行在 UI 线程（由 DispatcherQueueTimer 驱动）
            GetTrafficCounters(out var up, out var down);
            _traffic.Sample(up, down, DateTimeOffset.UtcNow);
        }

        private void UnsubscribeClient(RustunClient client)
        {
            client.OnConnected -= Client_OnConnected;
            client.OnDisconnected -= Client_OnDisconnected;
        }

        public async Task ConnectAsync(string ip, int port, string identity, string crypto, string secret)
        {
            await _operationLock.WaitAsync();
            try
            {
                RustunClient? existing;
                lock (_clientSync)
                {
                    existing = Client;
                }

                if (existing != null && IsConnected)
                {
                    return;
                }

                if (existing != null)
                {
                    RustunClient? old;
                    lock (_clientSync)
                    {
                        old = Client;
                        Client = null;
                        if (old != null)
                        {
                            UnsubscribeClient(old);
                        }
                    }

                    if (old != null)
                    {
                        try
                        {
                            await old.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "VpnService: StopAsync failed while clearing client before connect.");
                        }
                    }

                    SetIsConnected(false);
                }

                var client = new RustunClient();
                client.OnConnected += Client_OnConnected;
                client.OnDisconnected += Client_OnDisconnected;

                lock (_clientSync)
                {
                    Client = client;
                }

                try
                {
                    await client.StartAsync(ip, port, identity, crypto, secret);
                }
                catch
                {
                    await TryRemoveFailedClientAsync(client);
                    throw;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private async Task TryRemoveFailedClientAsync(RustunClient client)
        {
            lock (_clientSync)
            {
                if (!ReferenceEquals(Client, client))
                {
                    return;
                }

                Client = null;
                UnsubscribeClient(client);
            }

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "VpnService: StopAsync failed while cleaning up after failed connect.");
            }

            SetIsConnected(false);
        }

        private void Client_OnDisconnected(object? sender, EventArgs e)
        {
            if (sender is not RustunClient disconnected)
            {
                return;
            }

            bool wasCurrent;
            lock (_clientSync)
            {
                wasCurrent = ReferenceEquals(disconnected, Client);
                if (wasCurrent)
                {
                    UnsubscribeClient(disconnected);
                    Client = null;
                }
            }

            if (wasCurrent)
            {
                SetIsConnected(false);
                _traffic.ResetSpeedBaseline();
            }
        }

        private void Client_OnConnected(object? sender, EventArgs e)
        {
            SetIsConnected(true);
            _traffic.ResetSpeedBaseline();
        }

        public async Task DisconnectAsync()
        {
            await _operationLock.WaitAsync();
            try
            {
                RustunClient? client;
                lock (_clientSync)
                {
                    client = Client;
                    Client = null;
                    if (client != null)
                    {
                        UnsubscribeClient(client);
                    }
                }

                if (client != null)
                {
                    try
                    {
                        await client.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "VpnService: StopAsync failed during disconnect.");
                    }
                }

                SetIsConnected(false);
            }
            finally
            {
                _operationLock.Release();
            }
        }
    }
}
