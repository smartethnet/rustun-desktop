using NetWintun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Rustun.Services
{
    internal class VpnService
    {
        public void start()
        {
            using var adapter = Adapter.Create("OfficeNet", "Wintun");
            using var session = adapter.StartSession(Wintun.Constants.MinRingCapacity);

            //你的IP和掩码
            IPAddress address = IPAddress.Parse("10.18.18.2");
            int prefixLength = 24;
        }
    }
}
