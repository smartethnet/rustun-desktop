using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rustun.Services
{
    public class ProcessEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public bool HasExited { get; set; }
        public int ExitCode { get; set; }
    }
}
