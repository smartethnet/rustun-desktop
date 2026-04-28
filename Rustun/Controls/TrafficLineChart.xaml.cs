using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Rustun.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace Rustun.Controls;

public sealed partial class TrafficLineChart : UserControl
{
    // 让绘制更“像图表”：留出内边距，避免线贴边
    private const double PlotPadding = 10;

    public static readonly DependencyProperty UploadSeriesProperty =
        DependencyProperty.Register(
            nameof(UploadSeries),
            typeof(IReadOnlyList<double>),
            typeof(TrafficLineChart),
            new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty DownloadSeriesProperty =
        DependencyProperty.Register(
            nameof(DownloadSeries),
            typeof(IReadOnlyList<double>),
            typeof(TrafficLineChart),
            new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty TimeSeriesProperty =
        DependencyProperty.Register(
            nameof(TimeSeries),
            typeof(IReadOnlyList<DateTimeOffset>),
            typeof(TrafficLineChart),
            new PropertyMetadata(null, OnSeriesChanged));

    public IReadOnlyList<double>? UploadSeries
    {
        get => (IReadOnlyList<double>?)GetValue(UploadSeriesProperty);
        set => SetValue(UploadSeriesProperty, value);
    }

    public IReadOnlyList<double>? DownloadSeries
    {
        get => (IReadOnlyList<double>?)GetValue(DownloadSeriesProperty);
        set => SetValue(DownloadSeriesProperty, value);
    }

    /// <summary>X 轴时间点（通常每分钟一个点）。若为空，将按索引等分绘制。</summary>
    public IReadOnlyList<DateTimeOffset>? TimeSeries
    {
        get => (IReadOnlyList<DateTimeOffset>?)GetValue(TimeSeriesProperty);
        set => SetValue(TimeSeriesProperty, value);
    }

    public TrafficLineChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrafficLineChart chart)
        {
            chart.Redraw();
        }
    }

    private void Redraw()
    {
        var upload = UploadSeries;
        var download = DownloadSeries;
        var times = TimeSeries;

        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var count = Math.Max(Math.Max(upload?.Count ?? 0, download?.Count ?? 0), times?.Count ?? 0);
        if (count <= 1)
        {
            UploadPolyline.Points = new PointCollection();
            DownloadPolyline.Points = new PointCollection();
            UploadFill.Points = new PointCollection();
            DownloadFill.Points = new PointCollection();
            GridCanvas.Children.Clear();
            AxisCanvas.Children.Clear();
            ScaleText.Text = $"{ByteFormatHelper.FormatBytesPerSecond(0)} – {ByteFormatHelper.FormatBytesPerSecond(0)}";
            return;
        }

        var max = 1d;
        if (upload is { Count: > 0 })
        {
            max = Math.Max(max, upload.Max());
        }
        if (download is { Count: > 0 })
        {
            max = Math.Max(max, download.Max());
        }

        // 给顶部留一点空隙，避免贴边
        var yMax = max * 1.05;
        ScaleText.Text = $"0 – {ByteFormatHelper.FormatBytesPerSecond(yMax)}";

        // 可绘制区域（留边距）
        var plotWidth = Math.Max(1, width - PlotPadding * 2);
        var plotHeight = Math.Max(1, height - PlotPadding * 2);

        DrawGrid(width, height);
        DrawXAxis(width, height, count, times);

        var uploadPoints = BuildPoints(upload, count, plotWidth, plotHeight, yMax, PlotPadding, PlotPadding);
        var downloadPoints = BuildPoints(download, count, plotWidth, plotHeight, yMax, PlotPadding, PlotPadding);
        UploadPolyline.Points = uploadPoints;
        DownloadPolyline.Points = downloadPoints;

        UploadFill.Points = BuildFillPolygon(uploadPoints, width, height);
        DownloadFill.Points = BuildFillPolygon(downloadPoints, width, height);
    }

    private void DrawGrid(double width, double height)
    {
        GridCanvas.Children.Clear();

        // 4 条水平网格线（25%/50%/75% + 顶部）
        // 尽量使用主题文字色作为网格线基础色；若资源读取失败则退回浅灰
        var gridColor = Microsoft.UI.Colors.Gray;
        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var b) && b is SolidColorBrush sb)
        {
            gridColor = sb.Color;
        }

        var gridBrush = new SolidColorBrush(gridColor) { Opacity = 0.18 };

        for (var i = 1; i <= 4; i++)
        {
            var y = PlotPadding + (height - PlotPadding * 2) * (i / 4.0);
            var line = new Line
            {
                X1 = PlotPadding,
                X2 = width - PlotPadding,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            };
            GridCanvas.Children.Add(line);
        }

        // 2 条竖向辅助线（1/3、2/3）
        for (var i = 1; i <= 2; i++)
        {
            var x = PlotPadding + (width - PlotPadding * 2) * (i / 3.0);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = PlotPadding,
                Y2 = height - PlotPadding,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
            };
            GridCanvas.Children.Add(line);
        }
    }

    private void DrawXAxis(double width, double height, int count, IReadOnlyList<DateTimeOffset>? times)
    {
        AxisCanvas.Children.Clear();

        // X 轴时间标签：每分钟显示一个时间点（小字号 + 45° 旋转避免重叠）
        var labelBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var tickColor = Microsoft.UI.Colors.Gray;
        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var b) && b is SolidColorBrush sb)
        {
            tickColor = sb.Color;
        }
        var tickBrush = new SolidColorBrush(tickColor) { Opacity = 0.25 };

        var plotWidth = Math.Max(1, width - PlotPadding * 2);
        var xStep = count <= 1 ? 0 : plotWidth / (count - 1);
        var yBase = height - PlotPadding;

        // 如果外部没提供 TimeSeries，则假设序列为“每秒一个点”，以当前时间为尾点反推每个点的时间
        var useDerivedTime = times is null || times.Count < count;
        var endLocal = DateTimeOffset.Now;

        // 只在“整分钟点”绘制 tick + label（默认每 60 个点一个刻度）
        for (var i = 0; i < count; i++)
        {
            var secondsFromEnd = (count - 1) - i;
            if (secondsFromEnd % 60 != 0)
            {
                continue;
            }

            var x = PlotPadding + i * xStep;

            // tick
            var tick = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = yBase,
                Y2 = yBase - 4,
                Stroke = tickBrush,
                StrokeThickness = 1,
            };
            AxisCanvas.Children.Add(tick);

            var tLocal = useDerivedTime
                ? endLocal.AddSeconds(-secondsFromEnd)
                : times![i].ToLocalTime();

            var label = new TextBlock
            {
                Text = tLocal.ToString("HH:mm"),
                FontSize = 10,
                Foreground = labelBrush,
                Opacity = 0.9,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = new RotateTransform { Angle = -45 },
            };

            Canvas.SetLeft(label, x - 2);
            Canvas.SetTop(label, yBase + 2);
            AxisCanvas.Children.Add(label);
        }
    }

    private static PointCollection BuildPoints(
        IReadOnlyList<double>? series,
        int count,
        double width,
        double height,
        double yMax,
        double xOffset,
        double yOffset)
    {
        var points = new PointCollection();
        if (series is null || series.Count == 0)
        {
            return points;
        }

        // 横轴均匀铺满：count 个点映射到 [0, width]
        var xStep = count <= 1 ? 0 : width / (count - 1);

        for (var i = 0; i < series.Count; i++)
        {
            var v = Math.Max(0, series[i]);
            var x = xOffset + i * xStep;
            var y = yOffset + (height - (v / yMax) * height);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static PointCollection BuildFillPolygon(PointCollection linePoints, double width, double height)
    {
        var poly = new PointCollection();
        if (linePoints.Count <= 1)
        {
            return poly;
        }

        // 从折线起点到底部、沿折线到终点、再回到底部闭合
        var first = linePoints[0];
        var last = linePoints[^1];

        poly.Add(new Point(first.X, height - PlotPadding));
        foreach (var p in linePoints)
        {
            poly.Add(p);
        }
        poly.Add(new Point(last.X, height - PlotPadding));
        return poly;
    }
}

