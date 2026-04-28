using Rustun.Helpers;
using Rustun.Services;
using Serilog;
using System;
using System.Collections.Generic;
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

        /// <summary>隧道 payload 累计上传（由 <see cref="Rustun.Lib.RustunClient"/> 统计）。</summary>
        public string TrafficUploadedDisplay => ByteFormatHelper.FormatBinary(VpnService.Instance.BytesUploaded);

        /// <summary>隧道 payload 累计下载。</summary>
        public string TrafficDownloadedDisplay => ByteFormatHelper.FormatBinary(VpnService.Instance.BytesDownloaded);

        /// <summary>由相邻两次流量采样估算的上传速率（字节/秒）。</summary>
        public string TrafficUploadSpeedDisplay => ByteFormatHelper.FormatBytesPerSecond(VpnService.Instance.UploadBytesPerSecond);

        /// <summary>由相邻两次流量采样估算的下载速率（字节/秒）。</summary>
        public string TrafficDownloadSpeedDisplay => ByteFormatHelper.FormatBytesPerSecond(VpnService.Instance.DownloadBytesPerSecond);

        /// <summary>最近 30 分钟上传速率（B/s）样本，按时间先后排列。</summary>
        public IReadOnlyList<double> UploadSpeedSeries => VpnService.Instance.UploadSpeedSeries;

        /// <summary>最近 30 分钟下载速率（B/s）样本，按时间先后排列。</summary>
        public IReadOnlyList<double> DownloadSpeedSeries => VpnService.Instance.DownloadSpeedSeries;

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
            VpnService.Instance.ConnectionStateChanged += handleVpnConnectionStateChanged;
            VpnService.Instance.TrafficUpdated += handleTrafficUpdated;

            IsConnected = VpnService.Instance.IsConnected;
            // 初始化一次显示（实际采样由 VpnService 常驻进行）
            RaiseTrafficPropertiesChanged();
        }

        private void handleVpnConnectionStateChanged(object? sender, bool connected)
        {
            IsConnected = connected;
            RaiseTrafficPropertiesChanged();
        }

        private void handleTrafficUpdated(object? sender, EventArgs e)
        {
            RaiseTrafficPropertiesChanged();
        }

        private void RaiseTrafficPropertiesChanged()
        {
            OnPropertyChanged(nameof(TrafficUploadedDisplay));
            OnPropertyChanged(nameof(TrafficDownloadedDisplay));
            OnPropertyChanged(nameof(TrafficUploadSpeedDisplay));
            OnPropertyChanged(nameof(TrafficDownloadSpeedDisplay));
            OnPropertyChanged(nameof(UploadSpeedSeries));
            OnPropertyChanged(nameof(DownloadSpeedSeries));
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
            VpnService.Instance.ConnectionStateChanged -= handleVpnConnectionStateChanged;
            VpnService.Instance.TrafficUpdated -= handleTrafficUpdated;
        }
    }
}
