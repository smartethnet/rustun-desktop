using Microsoft.UI.Dispatching;
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

        private string _trafficUploadedDisplay = ByteFormatHelper.FormatBinary(0);
        private string _trafficDownloadedDisplay = ByteFormatHelper.FormatBinary(0);

        /// <summary>隧道 payload 累计上传（由 <see cref="Rustun.Lib.RustunClient"/> 统计）。</summary>
        public string TrafficUploadedDisplay
        {
            get => _trafficUploadedDisplay;
            private set
            {
                if (_trafficUploadedDisplay == value)
                {
                    return;
                }

                _trafficUploadedDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>隧道 payload 累计下载。</summary>
        public string TrafficDownloadedDisplay
        {
            get => _trafficDownloadedDisplay;
            private set
            {
                if (_trafficDownloadedDisplay == value)
                {
                    return;
                }

                _trafficDownloadedDisplay = value;
                OnPropertyChanged();
            }
        }

        private string _trafficUploadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);
        private string _trafficDownloadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);

        /// <summary>由相邻两次流量采样估算的上传速率（字节/秒）。</summary>
        public string TrafficUploadSpeedDisplay
        {
            get => _trafficUploadSpeedDisplay;
            private set
            {
                if (_trafficUploadSpeedDisplay == value)
                {
                    return;
                }

                _trafficUploadSpeedDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>由相邻两次流量采样估算的下载速率（字节/秒）。</summary>
        public string TrafficDownloadSpeedDisplay
        {
            get => _trafficDownloadSpeedDisplay;
            private set
            {
                if (_trafficDownloadSpeedDisplay == value)
                {
                    return;
                }

                _trafficDownloadSpeedDisplay = value;
                OnPropertyChanged();
            }
        }

        private DispatcherQueueTimer? _trafficRefreshTimer;

        private long _speedLastBytesUp;
        private long _speedLastBytesDown;
        private DateTimeOffset _speedLastSampleTime;
        private bool _speedSamplingReady;

        private const int SpeedHistorySeconds = 30 * 60;
        private readonly double[] _uploadSpeedRing = new double[SpeedHistorySeconds];
        private readonly double[] _downloadSpeedRing = new double[SpeedHistorySeconds];
        private int _speedRingHead;
        private int _speedRingCount;
        private double[] _uploadSpeedSnapshot = [];
        private double[] _downloadSpeedSnapshot = [];

        /// <summary>最近 30 分钟上传速率（B/s）样本，按时间先后排列。</summary>
        public IReadOnlyList<double> UploadSpeedSeries => _uploadSpeedSnapshot;

        /// <summary>最近 30 分钟下载速率（B/s）样本，按时间先后排列。</summary>
        public IReadOnlyList<double> DownloadSpeedSeries => _downloadSpeedSnapshot;

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

            IsConnected = VpnService.Instance.IsConnected;
            // 应用启动（页面创建）即开始采样并记录 30 分钟历史曲线；
            // 未连接时速率记录为 0，连接/断开仅重置采样基线避免跳变。
            resetSpeedBaseline();
            startTrafficRefreshTimer();
            refreshTrafficDisplaysFromVpn();
        }

        private void handleVpnConnectionStateChanged(object? sender, bool connected)
        {
            IsConnected = connected;
            if (connected)
            {
                resetSpeedBaseline();
                refreshTrafficDisplaysFromVpn();
            }
            else
            {
                resetSpeedBaseline();
                TrafficUploadedDisplay = ByteFormatHelper.FormatBinary(0);
                TrafficDownloadedDisplay = ByteFormatHelper.FormatBinary(0);
                TrafficUploadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);
                TrafficDownloadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);
            }
        }

        private void startTrafficRefreshTimer()
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null)
            {
                return;
            }

            _trafficRefreshTimer ??= dq.CreateTimer();
            _trafficRefreshTimer.Interval = TimeSpan.FromSeconds(1);
            _trafficRefreshTimer.IsRepeating = true;
            _trafficRefreshTimer.Tick -= trafficRefreshTimer_Tick;
            _trafficRefreshTimer.Tick += trafficRefreshTimer_Tick;
            _trafficRefreshTimer.Start();
        }

        private void stopTrafficRefreshTimer()
        {
            if (_trafficRefreshTimer is null)
            {
                return;
            }

            _trafficRefreshTimer.Stop();
            _trafficRefreshTimer.Tick -= trafficRefreshTimer_Tick;
        }

        private void trafficRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            refreshTrafficDisplaysFromVpn();
        }

        /// <summary>
        /// 仅重置速率采样的“上一采样点”基线，不清空 30 分钟历史序列。
        /// 用于断线/重连场景，避免 delta 使用到上一次连接的累计字节导致跳变。
        /// </summary>
        private void resetSpeedBaseline()
        {
            _speedSamplingReady = false;
        }

        private void refreshTrafficDisplaysFromVpn()
        {
            VpnService.Instance.GetTrafficCounters(out var up, out var down);
            TrafficUploadedDisplay = ByteFormatHelper.FormatBinary(up);
            TrafficDownloadedDisplay = ByteFormatHelper.FormatBinary(down);

            var now = DateTimeOffset.UtcNow;
            if (!_speedSamplingReady)
            {
                _speedLastBytesUp = up;
                _speedLastBytesDown = down;
                _speedLastSampleTime = now;
                _speedSamplingReady = true;
                TrafficUploadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);
                TrafficDownloadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(0);
                appendSpeedSample(0, 0);
                return;
            }

            var elapsedSeconds = (now - _speedLastSampleTime).TotalSeconds;
            if (elapsedSeconds <= 0)
            {
                return;
            }

            var deltaUp = Math.Max(0L, up - _speedLastBytesUp);
            var deltaDown = Math.Max(0L, down - _speedLastBytesDown);
            var upBps = deltaUp / elapsedSeconds;
            var downBps = deltaDown / elapsedSeconds;
            TrafficUploadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(upBps);
            TrafficDownloadSpeedDisplay = ByteFormatHelper.FormatBytesPerSecond(downBps);
            appendSpeedSample(upBps, downBps);

            _speedLastBytesUp = up;
            _speedLastBytesDown = down;
            _speedLastSampleTime = now;
        }

        private void appendSpeedSample(double uploadBps, double downloadBps)
        {
            // 固定容量环形缓冲：保存最近 30 分钟（按 1s 定时器采样）
            _uploadSpeedRing[_speedRingHead] = Math.Max(0, uploadBps);
            _downloadSpeedRing[_speedRingHead] = Math.Max(0, downloadBps);

            _speedRingHead = (_speedRingHead + 1) % SpeedHistorySeconds;
            _speedRingCount = Math.Min(SpeedHistorySeconds, _speedRingCount + 1);

            _uploadSpeedSnapshot = snapshotRing(_uploadSpeedRing, _speedRingHead, _speedRingCount);
            _downloadSpeedSnapshot = snapshotRing(_downloadSpeedRing, _speedRingHead, _speedRingCount);
            OnPropertyChanged(nameof(UploadSpeedSeries));
            OnPropertyChanged(nameof(DownloadSpeedSeries));
        }

        private static double[] snapshotRing(double[] ring, int head, int count)
        {
            if (count <= 0)
            {
                return [];
            }

            var result = new double[count];
            var start = (head - count + SpeedHistorySeconds) % SpeedHistorySeconds;
            var firstPart = Math.Min(count, SpeedHistorySeconds - start);
            Array.Copy(ring, start, result, 0, firstPart);
            if (firstPart < count)
            {
                Array.Copy(ring, 0, result, firstPart, count - firstPart);
            }
            return result;
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
            stopTrafficRefreshTimer();
            SettingsHelper.Current.PropertyChanged -= handleSettingsPropertyChanged;
            VpnService.Instance.ConnectionStateChanged -= handleVpnConnectionStateChanged;
        }
    }
}
