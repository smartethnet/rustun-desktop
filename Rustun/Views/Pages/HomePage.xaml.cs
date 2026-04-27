using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rustun.Helpers;
using Rustun.Services;
using Rustun.ViewModels.Pages;
using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.
namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HomePage : Page, INotifyPropertyChanged
{
    private HomePageViewModel viewModel = new HomePageViewModel();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    public HomePage()
    {
        InitializeComponent();
        statusText.Text = "状态: 已停止";
    }

    private string serverUrl
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
    private string identity => SettingsHelper.Current.Identity;
    private bool isServerInfoSet
    {
        get
        {
            string serverIp = SettingsHelper.Current.ServerIp;
            string serverPort = SettingsHelper.Current.ServerPort;
            return !string.IsNullOrEmpty(serverIp) && !string.IsNullOrEmpty(serverPort);
        }
    }
    private bool _loading = false;
    private new bool Loading
    {
        get => _loading;
        set
        {
            _loading = value;
            OnPropertyChanged();
        }
    }

    public bool IsProcessRunning
    {
        get => false;
        set
        {
            statusText.Text = value ? "状态: 运行中" : "状态: 已停止";
            OnPropertyChanged();
        }
    }

    private void connectToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            if (!IsProcessRunning && toggleSwitch.IsOn)
            {
                Loading = true;
                connect();
            }
            else if (!toggleSwitch.IsOn)
            {
                Loading = false;
                disconnect();
            }
        }
    }

    private async void connect()
    {
        try
        {
            if (IsProcessRunning)
            {
                return;
            }

            string serverIp = SettingsHelper.Current.ServerIp;
            string serverPort = SettingsHelper.Current.ServerPort;
            await VpnService.Instance.ConnectAsync(serverIp, Convert.ToInt32(serverPort), identity, SettingsHelper.Current.EncryptionMode, SettingsHelper.Current.EncryptionSecret);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"启动失败");
            IsProcessRunning = false;
            await ShowNotification($"启动失败: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    private async void disconnect()
    {
        if (IsProcessRunning)
        {
            await VpnService.Instance.DisconnectAsync();
            await ShowNotification("已停止", false);
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
        // 清理事件订阅
    }
}
