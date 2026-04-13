using NetWintun;
using Rustun.Lib;
using Serilog;
using System;
using System.Management;
using System.Threading.Tasks;

namespace Rustun.Services
{
    /// <summary>
    /// VPN service
    /// </summary>
    internal class VpnService
    {
        public static readonly string AdapterName = "Rustun";
        private static VpnService _instance = new VpnService();
        public static VpnService Instance => _instance;
        public Adapter? Adapter { get; private set; }
        public Guid? AdapterId { get; private set; }
        private Session? Session { get; set; }
        private RustunClient? Client { get; set; }

        private VpnService()
        {

        }

        private void SetIpAddressByWmic(string uuid, string ip, string mask)
        {
            // 获取所有网络适配器配置
            ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection adapters = mc.GetInstances();
            foreach (ManagementObject item in adapters)
            {
                var objectSettingId = item["SettingID"];
                if (objectSettingId is string settingId)
                {
                    string adapterId = "{" + uuid.ToUpper() + "}";
                    if (settingId == adapterId)
                    {
                        var newIP = item.GetMethodParameters("EnableStatic");
                        if (newIP == null)
                        {
                            throw new InvalidOperationException("EnableStatic method not found on adapter configuration.");
                        }

                        newIP["IPAddress"] = new string[] { ip ?? throw new ArgumentNullException(nameof(ip)) };
                        newIP["SubnetMask"] = new string[] { mask ?? throw new ArgumentNullException(nameof(mask)) };

                        var result = item.InvokeMethod("EnableStatic", newIP, null);
                        if (result == null)
                        {
                            throw new InvalidOperationException("EnableStatic invocation returned null.");
                        }

                        var returnObj = result["returnvalue"];
                        int returnValue = Convert.ToInt32(returnObj ?? 0);
                        if (returnValue != 0 && returnValue != 1)
                        {
                            throw new Exception("Set IP address and subnet mask error: " + returnValue);
                        }
                    }
                }
            }
        }

        public void CreateAdapter()
        {
            // 创建网卡
            AdapterId = Guid.NewGuid();
            Adapter = Adapter.Create(AdapterName, "Wintun", AdapterId);
            Log.Information($"Created adapter with name: {AdapterName}, id: {AdapterId}");
        }

        public async Task ConnectAsync(string ip, int port, string identity, string crypto, string secret)
        {
            Client = new RustunClient(ip, port, identity, crypto, secret);
            await Client.StartAsync();
        }

        public void Start(string ip, string mask)
        {
            // 创建网卡
            CreateAdapter();

            // 创建一个会话
            Session = Adapter!.StartSession(Wintun.Constants.MinRingCapacity);

            // 获取所有网络适配器配置
            SetIpAddressByWmic(AdapterId.ToString()!, ip, mask);
        }
    }
}
