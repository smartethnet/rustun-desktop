using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;

namespace Rustun.Services;

/// <summary>
/// 全局单例：流量统计服务（累计字节、实时速率、30 分钟历史曲线）。
/// 通过 UI 线程的 <see cref="DispatcherQueueTimer"/> 每秒采样一次 <see cref="VpnService"/> 的计数器。
/// </summary>
internal sealed class TrafficStatisticsService
{
    private static readonly TrafficStatisticsService _instance = new();
    public static TrafficStatisticsService Instance => _instance;

    private DispatcherQueue? _uiDispatcher;
    private DispatcherQueueTimer? _timer;
    private readonly TrafficStatistics _traffic = new();

    public long BytesUploaded => _traffic.BytesUploaded;
    public long BytesDownloaded => _traffic.BytesDownloaded;
    public double UploadBytesPerSecond => _traffic.UploadBytesPerSecond;
    public double DownloadBytesPerSecond => _traffic.DownloadBytesPerSecond;
    public IReadOnlyList<double> UploadSpeedSeries => _traffic.UploadSpeedSeries;
    public IReadOnlyList<double> DownloadSpeedSeries => _traffic.DownloadSpeedSeries;

    /// <summary>流量统计刷新（每秒一次，已投递到 UI 线程）。</summary>
    public event EventHandler? TrafficUpdated;

    private TrafficStatisticsService()
    {
        _traffic.Updated += (_, _) => TrafficUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void AttachUiDispatcher(DispatcherQueue dispatcher)
    {
        _uiDispatcher = dispatcher;
        EnsureStarted();
    }

    public void ResetSpeedBaseline()
    {
        _traffic.ResetSpeedBaseline();
    }

    private void EnsureStarted()
    {
        if (_uiDispatcher is null)
        {
            return;
        }

        if (_timer is not null)
        {
            return;
        }

        _timer = _uiDispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => SampleOnUi();
        _timer.Start();

        // 初始化一次，避免首次进入页面看到旧值/空值
        SampleOnUi();
    }

    private void SampleOnUi()
    {
        // 该方法运行在 UI 线程（由 DispatcherQueueTimer 驱动）
        VpnService.Instance.GetTrafficCounters(out var up, out var down);
        _traffic.Sample(up, down, DateTimeOffset.UtcNow);
    }
}

