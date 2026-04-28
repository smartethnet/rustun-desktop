using Rustun.Lib;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Rustun.Services
{
    /// <summary>
    /// VPN service
    /// </summary>
    internal class VpnService
    {
        private static VpnService _instance = new VpnService();
        public static VpnService Instance => _instance;

        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _clientSync = new();
        private RustunClient? Client { get; set; }

        public event EventHandler? OnConnected;
        public event EventHandler? OnDisconnected;
        public bool IsConnected { get; set; }

        private VpnService()
        {

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
        }

        private void Client_OnDisconnected(object? sender, EventArgs e)
        {
            IsConnected = false;

            lock (_clientSync)
            {
                if (sender is RustunClient disconnected && ReferenceEquals(disconnected, Client))
                {
                    disconnected.OnConnected -= Client_OnConnected;
                    disconnected.OnDisconnected -= Client_OnDisconnected;
                    Client = null;
                }
            }

            OnDisconnected?.Invoke(this, e);
        }

        private void Client_OnConnected(object? sender, EventArgs e)
        {
            IsConnected = true;
            OnConnected?.Invoke(this, e);
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

                IsConnected = false;
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _operationLock.Release();
            }
        }

    }
}
