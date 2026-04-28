using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rustun.ViewModels.Pages;
using Serilog;
using System;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HomePage : Page
{
    private HomePageViewModel viewModel = new HomePageViewModel();

    public HomePage()
    {
        InitializeComponent();
        statusText.Text = "状态: 已停止";
        DataContext = viewModel;
    }

    private void ConnectToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            if (toggleSwitch.IsOn)
            {
                ConnectAsync();
            }
            else
            {
                DisconnectAsync();
            }
        }
    }

    private async void ConnectAsync()
    {
        try
        {
            await viewModel.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动失败");
            await ShowNotification($"启动失败: {ex.Message}");
        }
    }

    private async void DisconnectAsync()
    {
        try
        {
            await viewModel.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "停止失败");
            await ShowNotification($"停止失败: {ex.Message}");
        }
    }

    private async Task ShowNotification(string message, bool isError = true)
    {
        ContentDialog errorDialog = new ContentDialog
        {
            Title = isError ? "错误" : "提示",
            Content = $"{message}",
            CloseButtonText = "关闭",
            XamlRoot = this.XamlRoot
        };
        await errorDialog.ShowAsync();
    }

    private void Page_Unloaded(object? sender, RoutedEventArgs e)
    {
        viewModel.Dispose();
    }
}
