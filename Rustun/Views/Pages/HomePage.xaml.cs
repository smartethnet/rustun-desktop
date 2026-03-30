using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rustun.Helpers;
using Rustun.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HomePage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly LogService _logService;
    private readonly ProcessService _processService;

    public HomePage()
    {
        InitializeComponent();

        _logService = LogService.Instance;
        _processService = ProcessService.Instance;
        _processService.OutputReceived += OnOutputReceived;
        _processService.ProcessExited += OnProcessExited;
        if (_processService.IsProcessRunning())
        {
            IsProcessRunning = true;
        }
        else
        {
            statusText.Text = "状态: 已停止";
        }
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
        get => _processService.IsProcessRunning();
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
            if (!_processService.IsProcessRunning() && toggleSwitch.IsOn)
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
            if (_processService.IsProcessRunning())
            {
                ShowNotification("客户端已在运行");
                return;
            }

            // 获取应用程序所在目录
            var appFolder = Directory.GetCurrentDirectory();
            var exePath = Path.Combine(appFolder, "client.exe");

            if (!File.Exists(exePath))
            {
                ShowNotification("找不到 client.exe 文件");
                return;
            }

            var identity = SettingsHelper.Current.Identity;
            var serverUrl = $"{SettingsHelper.Current.ServerIp}:{SettingsHelper.Current.ServerPort}";
            var crypto = "plain";
            switch (SettingsHelper.Current.EncryptionMode)
            {
                case "Chacha20":
                    crypto = "chacha20:" + SettingsHelper.Current.EncryptionSecret;
                    break;
                case "AES":
                    crypto = "aes256:" + SettingsHelper.Current.EncryptionSecret;
                    break;
                case "XOR":
                    crypto = "xor:" + SettingsHelper.Current.EncryptionSecret;
                    break;
                default:
                    crypto = "plain";
                    break;
            }
            var success = await _processService.StartClientProcess(exePath, $"--server {serverUrl} --identity {identity} --crypto {crypto}");
            if (success)
            {
                IsProcessRunning = true;
                ShowNotification("启动成功", false);
            }
            else
            {
                ShowNotification("启动失败");
                IsProcessRunning = false;
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"启动失败: {ex.Message}");
            IsProcessRunning = false;
        }
        finally
        {
            Loading = false;
        }
    }

    private async void disconnect()
    {
        if (_processService.IsProcessRunning())
        {
            _processService.StopClientProcess();
            ShowNotification("已停止", false);
        }
    }

    private void OnOutputReceived(object? sender, ProcessEventArgs e)
    {
        _logService.AddLog(e.Message);
    }

    private void OnProcessExited(object? sender, ProcessEventArgs e)
    {
        IsProcessRunning = false;

        // 显示意外停止通知
        if (e.ExitCode != 0)
        {
            ShowNotification($"意外停止 (退出代码: {e.ExitCode})");
        }
    }

    private void ShowNotification(string message, bool isError = true)
    {
        _logService.AddLog(message, isError ? "ERROR" : "INFO");
    }

    private void Page_Unloaded(object? sender, RoutedEventArgs e)
    {
        // 清理事件订阅
        _processService.ProcessExited -= OnProcessExited;
        _processService.OutputReceived -= OnOutputReceived;
    }
}
