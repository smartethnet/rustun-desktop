using Microsoft.UI.Dispatching;
using Rustun.Lib;
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

        /// <summary>与底层 <see cref="RustunClient"/> 的隧道就绪状态一致；在 UI 线程上触发变更通知。</summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 连接状态变化（true=已连接并可转发流量）。订阅此事件即可，无需再区分 OnConnected/OnDisconnected。
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        private VpnService()
        {
        }

        /// <summary>在 UI 线程创建主窗口后调用一次，保证状态回调在 UI 线程派发。</summary>
        public void AttachUiDispatcher(DispatcherQueue dispatcher)
        {
            _uiDispatcher = dispatcher;
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
                            old.OnConnected -= Client_OnConnected;
                            old.OnDisconnected -= Client_OnDisconnected;
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

                var client = new RustunClient(ip, port, identity, crypto, secret);
                client.OnConnected += Client_OnConnected;
                client.OnDisconnected += Client_OnDisconnected;

                lock (_clientSync)
                {
                    Client = client;
                }

                try
                {
                    await client.StartAsync();
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
                client.OnConnected -= Client_OnConnected;
                client.OnDisconnected -= Client_OnDisconnected;
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
            lock (_clientSync)
            {
                if (sender is RustunClient disconnected && ReferenceEquals(disconnected, Client))
                {
                    disconnected.OnConnected -= Client_OnConnected;
                    disconnected.OnDisconnected -= Client_OnDisconnected;
                    Client = null;
                }
            }

            SetIsConnected(false);
        }

        private void Client_OnConnected(object? sender, EventArgs e)
        {
            SetIsConnected(true);
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
                        client.OnConnected -= Client_OnConnected;
                        client.OnDisconnected -= Client_OnDisconnected;
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
