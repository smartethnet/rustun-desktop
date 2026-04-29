using System;
using System.Collections.Generic;

namespace Rustun.Services;

/// <summary>
/// 负责流量统计：累计字节、速率估算、30 分钟历史曲线（每秒一个点）。
/// 不依赖 UI 框架，可由任意调度器/计时器周期性调用 <see cref="Sample"/>.
/// </summary>
internal sealed class TrafficStatistics
{
    private long _lastBytesUp;
    private long _lastBytesDown;
    private DateTimeOffset _lastSampleTimeUtc;
    private bool _baselineReady;

    private long _bytesUploaded;
    private long _bytesDownloaded;
    private double _uploadBps;
    private double _downloadBps;

    private const int HistorySeconds = 30 * 60;
    private readonly double[] _uploadRing = new double[HistorySeconds];
    private readonly double[] _downloadRing = new double[HistorySeconds];
    private int _ringHead;
    private int _ringCount;
    private readonly RingBufferSeriesView _uploadSeriesView = new(HistorySeconds);
    private readonly RingBufferSeriesView _downloadSeriesView = new(HistorySeconds);
    private long _seriesRevision;

    public long BytesUploaded => _bytesUploaded;
    public long BytesDownloaded => _bytesDownloaded;
    public double UploadBytesPerSecond => _uploadBps;
    public double DownloadBytesPerSecond => _downloadBps;
    public IReadOnlyList<double> UploadSpeedSeries => _uploadSeriesView;
    public IReadOnlyList<double> DownloadSpeedSeries => _downloadSeriesView;

    /// <summary>
    /// 曲线数据内容版本号：在底层数组就地更新时递增，供 UI 侧用轻量属性变更触发重绘。
    /// </summary>
    public long SeriesRevision => _seriesRevision;

    /// <summary>当采样完成并更新了内部状态时触发。</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// 重置速率计算基线：下次 <see cref="Sample"/> 会把当前累计字节作为基线并将速率置 0，
    /// 以避免断线/重连导致的速率突跳（不会清空历史曲线）。
    /// </summary>
    public void ResetSpeedBaseline()
    {
        _baselineReady = false;
        _ringHead = 0;
        _ringCount = 0;
        _uploadSeriesView.Reset();
        _downloadSeriesView.Reset();
        unchecked
        {
            _seriesRevision++;
        }
    }

    /// <summary>
    /// 采样一次：根据“当前累计字节”和“上一采样点”估算速率，并追加到 30 分钟历史曲线。
    /// 调用方应传入当前 UTC 时间；未连接时累计字节应为 0。
    /// </summary>
    public void Sample(long bytesUploaded, long bytesDownloaded, DateTimeOffset nowUtc)
    {
        _bytesUploaded = bytesUploaded;
        _bytesDownloaded = bytesDownloaded;

        if (!_baselineReady)
        {
            _lastBytesUp = bytesUploaded;
            _lastBytesDown = bytesDownloaded;
            _lastSampleTimeUtc = nowUtc;
            _baselineReady = true;
            _uploadBps = 0;
            _downloadBps = 0;
            AppendHistory(0, 0);
            Updated?.Invoke(this, EventArgs.Empty);
            return;
        }

        var elapsedSeconds = (nowUtc - _lastSampleTimeUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        var deltaUp = Math.Max(0L, bytesUploaded - _lastBytesUp);
        var deltaDown = Math.Max(0L, bytesDownloaded - _lastBytesDown);
        _uploadBps = deltaUp / elapsedSeconds;
        _downloadBps = deltaDown / elapsedSeconds;

        AppendHistory(_uploadBps, _downloadBps);

        _lastBytesUp = bytesUploaded;
        _lastBytesDown = bytesDownloaded;
        _lastSampleTimeUtc = nowUtc;

        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 追加一条速率样本到历史环形缓冲，并更新用于 UI 绑定的连续视图（按时间先后排列，就地写入）。
    /// </summary>
    private void AppendHistory(double uploadBps, double downloadBps)
    {
        _uploadRing[_ringHead] = Math.Max(0, uploadBps);
        _downloadRing[_ringHead] = Math.Max(0, downloadBps);

        _ringHead = (_ringHead + 1) % HistorySeconds;
        _ringCount = Math.Min(HistorySeconds, _ringCount + 1);

        _uploadSeriesView.CopyFromRing(_uploadRing, _ringHead, _ringCount);
        _downloadSeriesView.CopyFromRing(_downloadRing, _ringHead, _ringCount);
        unchecked
        {
            _seriesRevision++;
        }
    }
}

