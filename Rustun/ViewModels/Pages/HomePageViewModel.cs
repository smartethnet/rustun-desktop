using Rustun.Helpers;
using Rustun.Services;
using Serilog;
using System;
using System.Threading.Tasks;

namespace Rustun.ViewModels.Pages
{
    public class HomePageViewModel : ViewModelBase, IDisposable
    {
        public string ServerUrl
        {
            get
            {
                string serverIp = SettingsHelper.Current.ServerIp;
                string serverPort = SettingsHelper.Current.ServerPort;
                if (string.IsNullOrEmpty(serverIp) || string.IsNullOrEmpty(serverPort))
                {
                    return "unset";
                }
                return $"{serverIp}:{serverPort}";
            }
        }

        public string Identity => SettingsHelper.Current.Identity;

        public bool IsServerInfoSet
        {
            get
            {
                string serverIp = SettingsHelper.Current.ServerIp;
                string serverPort = SettingsHelper.Current.ServerPort;
                return !string.IsNullOrEmpty(serverIp) && !string.IsNullOrEmpty(serverPort);
            }
        }

        private bool _loading;
        public bool Loading
        {
            get => _loading;
            set
            {
                _loading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanToggleVpn));
            }
        }

        /// <summary>连接/断开进行中时禁用开关，避免重复点击与状态错乱。</summary>
        public bool CanToggleVpn => !_loading;

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public HomePageViewModel()
        {
            SettingsHelper.Current.PropertyChanged += handleSettingsPropertyChanged;
            VpnService.Instance.OnConnected += handleVpnConnected;
            VpnService.Instance.OnDisconnected += handleVpnDisconnected;

            IsConnected = VpnService.Instance.IsConnected;
        }

        private void handleVpnDisconnected(object? sender, EventArgs e)
        {
            IsConnected = false;
        }

        private void handleVpnConnected(object? sender, EventArgs e)
        {
            IsConnected = true;
        }

        private void handleSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsHelper.ServerIp) || e.PropertyName == nameof(SettingsHelper.ServerPort) || e.PropertyName == nameof(SettingsHelper.Identity))
            {
                OnPropertyChanged(nameof(ServerUrl));
                OnPropertyChanged(nameof(Identity));
                OnPropertyChanged(nameof(IsServerInfoSet));
            }
        }

        public async Task Start()
        {
            if (_isConnected || _loading)
            {
                Log.Information("Already connected or loading, skipping start.");
                return;
            }

            Loading = true;
            try
            {
                string serverIp = SettingsHelper.Current.ServerIp;
                string serverPort = SettingsHelper.Current.ServerPort;
                string identity = SettingsHelper.Current.Identity;
                await VpnService.Instance.ConnectAsync(serverIp, Convert.ToInt32(serverPort), identity, SettingsHelper.Current.EncryptionMode, SettingsHelper.Current.EncryptionSecret);
            }
            catch
            {
                IsConnected = false;
                throw;
            }
            finally
            {
                Loading = false;
            }
        }

        public async Task Stop()
        {
            if (!_isConnected)
            {
                Log.Information("Not connected, skipping stop.");
                return;
            }

            Loading = true;
            try
            {
                await VpnService.Instance.DisconnectAsync();
            }
            finally
            {
                Loading = false;
            }
        }

        public void Dispose()
        {
            SettingsHelper.Current.PropertyChanged -= handleSettingsPropertyChanged;
            VpnService.Instance.OnConnected -= handleVpnConnected;
            VpnService.Instance.OnDisconnected -= handleVpnDisconnected;
        }
    }
}
