using Microsoft.UI.Dispatching;
using Rustun.Lib;
using Rustun.Lib.Message;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        /// <summary>全局单例实例。</summary>
        public static VpnService Instance => _instance;

        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _clientSync = new();
        private RustunClient? Client { get; set; }

        private DispatcherQueue? _uiDispatcher;
        private bool _isConnected;

        /// <summary>与底层 <see cref="RustunClient"/> 的隧道就绪状态一致；在 UI 线程上触发变更通知。</summary>
        public bool IsConnected => _isConnected;

        public List<PeerDetail> PeerDetails { get; private set; } = new List<PeerDetail>();

        /// <summary>
        /// 读取当前客户端隧道累计字节（payload）；未连接时为 0。可在任意线程调用。
        /// </summary>
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

        /// <summary>
        /// 连接状态变化（true=已连接并可转发流量）。订阅此事件即可，无需再区分 OnConnected/OnDisconnected。
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        private VpnService()
        {
        }

        /// <summary>
        /// 在 UI 线程创建主窗口后调用一次，保证状态回调在 UI 线程派发。
        /// </summary>
        public void AttachUiDispatcher(DispatcherQueue dispatcher)
        {
            _uiDispatcher = dispatcher;
        }

        /// <summary>
        /// 将回调投递到 UI 线程执行；若当前已在 UI 线程则直接执行。
        /// </summary>
        private void PostToUi(Action action)
        {
            if (_uiDispatcher is null || _uiDispatcher.HasThreadAccess)
            {
                action();
                return;
            }

            _ = _uiDispatcher.TryEnqueue(() => action());
        }

        /// <summary>
        /// 更新连接状态并通知 UI（属性变更 + <see cref="ConnectionStateChanged"/>）。
        /// </summary>
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

        private void UpdatePeerDetails(List<PeerDetail> details)
        {
            PeerDetails = details;
            PostToUi(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PeerDetails)));
            });
        }

        /// <summary>取消订阅客户端事件，避免重复回调与泄漏。</summary>
        private void UnsubscribeClient(RustunClient client)
        {
            client.OnConnected -= Client_OnConnected;
            client.OnDisconnected -= Client_OnDisconnected;
            client.OnPeerDetail -= Client_OnPeerDetailsUpdated;
        }

        /// <summary>
        /// 建立 VPN 连接：会创建新的 <see cref="RustunClient"/> 并执行握手/网卡配置。
        /// 若已有旧客户端会先停止并清理。该方法串行化执行（<see cref="_operationLock"/>）。
        /// </summary>
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
                client.OnPeerDetail += Client_OnPeerDetailsUpdated;

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

        /// <summary>
        /// 连接失败后的清理：仅当失败客户端仍为当前客户端时才移除并停止。
        /// </summary>
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

        /// <summary>
        /// 底层客户端断开回调：若断开的是当前客户端，则释放引用并更新连接状态。
        /// 同时重置速率基线以避免下一次连接的速率突跳。
        /// </summary>
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
                TrafficStatisticsService.Instance.ResetSpeedBaseline();
            }
        }

        /// <summary>
        /// 底层客户端 PeerDetails 更新回调：直接更新属性并通知 UI。该回调可能来自非 UI 线程，因此不直接触发 PropertyChanged。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="details"></param>
        private void Client_OnPeerDetailsUpdated(object? sender, Collection<PeerDetail> details)
        {
            UpdatePeerDetails(new List<PeerDetail>(details));
        }

        /// <summary>
        /// 底层客户端连接就绪回调：更新连接状态，并重置速率基线（不清空历史曲线）。
        /// </summary>
        private void Client_OnConnected(object? sender, EventArgs e)
        {
            SetIsConnected(true);
            TrafficStatisticsService.Instance.ResetSpeedBaseline();
        }

        /// <summary>
        /// 断开 VPN：停止并释放当前客户端资源，然后更新连接状态。该方法串行化执行（<see cref="_operationLock"/>）。
        /// </summary>
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
