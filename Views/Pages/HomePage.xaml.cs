using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Rustun.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
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
}
