using Rustun.Lib;
using System.ComponentModel;
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

        private RustunClient? Client { get; set; }

        private VpnService()
        {

        }

        public bool IsConnected => Client != null;

        public async Task ConnectAsync(string ip, int port, string identity, string crypto, string secret)
        {
            Client = new RustunClient(ip, port, identity, crypto, secret);
            await Client.StartAsync();
        }

        public async Task DisconnectAsync()
        {
            if (Client != null)
            {
                await Client.StopAsync();
                Client = null;
            }
        }
    }
}
