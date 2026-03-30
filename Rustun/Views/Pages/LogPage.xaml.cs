using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rustun.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LogPage : Page
{
    private readonly ProcessService _processService;
    private readonly LogService _logService;


    public LogPage()
    {
        InitializeComponent();

        _processService = ProcessService.Instance;
        _logService = LogService.Instance;

        // 订阅进程输出事件
        _processService.OutputReceived += OnOutputReceived;

        // 订阅日志集合变化事件，实现自动滚动
        _logService.PropertyChanged += OnLogsChanged;
    }

    private void OnOutputReceived(object? sender, ProcessEventArgs e)
    {
        _logService.AddLog(e.Message);
    }

    private void OnLogsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_logService.LogEntries))
        {
            // 延迟滚动以确保UI已更新
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_logService.LogEntries.Count > 0)
                {
                    LogScrollViewer.ChangeView(0, double.MaxValue, 1);
                }
            });
        }
    }

    private void Page_Unloaded(object? sender, RoutedEventArgs e)
    {
        // 清理事件订阅
        _processService.OutputReceived -= OnOutputReceived;
        _logService.PropertyChanged -= OnLogsChanged;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _logService.ClearLogs();
    }
}
