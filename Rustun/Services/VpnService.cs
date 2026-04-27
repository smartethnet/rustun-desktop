using CommunityToolkit.WinUI.Animations;
using Rustun.Lib;
using System;
using System.Threading.Tasks;

namespace Rustun.Services
{
    /// <summary>
    /// VPN service
    /// </summary>
    internal class VpnService : IDisposable
    {
        private static VpnService _instance = new VpnService();
        public static VpnService Instance => _instance;
        private RustunClient? Client { get; set; }

        public event EventHandler? OnConnected;
        public event EventHandler? OnDisconnected;
        public bool IsConnected { get; set; }

        private VpnService()
        {

        }

        public async Task ConnectAsync(string ip, int port, string identity, string crypto, string secret)
        {
            Client = new RustunClient(ip, port, identity, crypto, secret);
            Client.OnConnected += Client_OnConnected;
            Client.OnDisconnected += Client_OnDisconnected;

            await Client.StartAsync();
        }

        private void Client_OnDisconnected(object? sender, EventArgs e)
        {
            IsConnected = false;
            if (Client != null)
            {
                Client.OnConnected -= Client_OnConnected;
                Client.OnDisconnected -= Client_OnDisconnected;
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
            if (Client != null)
            {
                await Client.StopAsync();

                Client.OnConnected -= Client_OnConnected;
                Client.OnDisconnected -= Client_OnDisconnected;
                Client = null;
            }
        }

        public async void Dispose()
        {
            if (Client != null)
            {
                await Client.StopAsync();

                Client.OnConnected -= Client_OnConnected;
                Client.OnDisconnected -= Client_OnDisconnected;
                Client = null;
            }
        }
    }
}
