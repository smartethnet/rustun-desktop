using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rustun.Helpers;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.DataTransfer;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public string Version
    {
        get
        {
            return ProcessInfoHelper.GetVersion() is Version version
                ? string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision)
                : string.Empty;
        }
    }

    public string WinAppSdkRuntimeDetails => VersionHelper.WinAppSdkRuntimeDetails;
    private int lastNavigationSelectionMode = 0;

    public string serverIp
    {
        get => SettingsHelper.Current.ServerIp;
        set
        {
            SettingsHelper.Current.ServerIp = value ?? string.Empty;
            OnPropertyChanged();
        }
    }
    public string serverPort
    {
        get => SettingsHelper.Current.ServerPort;
        set
        {
            SettingsHelper.Current.ServerPort = value ?? string.Empty;
            OnPropertyChanged();
        }
    }
    public string identity
    {
        get => SettingsHelper.Current.Identity;
        set
        {
            SettingsHelper.Current.Identity = value ?? string.Empty;
            OnPropertyChanged();
        }
    }
    public string encryptionMode
    {
        get => SettingsHelper.Current.EncryptionMode;
        set
        {
            SettingsHelper.Current.EncryptionMode = value ?? string.Empty;
            OnPropertyChanged();
        }
    }
    public string encryptionSecret
    {
        get => SettingsHelper.Current.EncryptionSecret;
        set
        {
            SettingsHelper.Current.EncryptionSecret = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += OnSettingsPageLoaded;
    }

    private void OnSettingsPageLoaded(object sender, RoutedEventArgs e)
    {
        var currentTheme = ThemeHelper.RootTheme;
        switch (currentTheme)
        {
            case ElementTheme.Light:
                themeMode.SelectedIndex = 0;
                break;
            case ElementTheme.Dark:
                themeMode.SelectedIndex = 1;
                break;
            case ElementTheme.Default:
                themeMode.SelectedIndex = 2;
                break;
        }

        if (App.MainWindow.NavigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.Auto)
        {
            navigationLocation.SelectedIndex = 0;
        }
        else
        {
            navigationLocation.SelectedIndex = 1;
        }

        lastNavigationSelectionMode = navigationLocation.SelectedIndex;

        switch(encryptionMode)
        {
            case "Chacha20":
                encryptMode.SelectedIndex = 0;
                break;
            case "AES":
                encryptMode.SelectedIndex = 1;
                break;
            case "XOR":
                encryptMode.SelectedIndex = 2;
                break;
            default:
                encryptMode.SelectedIndex = -1;
                break;
        }
    }

    private void themeMode_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement senderUiLement ||
            (themeMode.SelectedItem as ComboBoxItem)?.Tag.ToString() is not string selectedTheme ||
            WindowHelper.GetWindowForElement(this) is not Window window)
        {
            return;
        }

        ThemeHelper.RootTheme = EnumHelper.GetEnum<ElementTheme>(selectedTheme);
        var elementThemeResolved = ThemeHelper.RootTheme == ElementTheme.Default ? ThemeHelper.ActualTheme : ThemeHelper.RootTheme;
        TitleBarHelper.ApplySystemThemeToCaptionButtons(window, elementThemeResolved);

        // announce visual change to automation
        UIHelper.AnnounceActionForAccessibility(senderUiLement, $"Theme changed to {elementThemeResolved}", "ThemeChangedNotificationActivityId");
    }

    private void navigationLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Since setting the left mode does not look at the old setting we 
        // need to check if this is an actual update
        if (navigationLocation.SelectedIndex != lastNavigationSelectionMode)
        {
            NavigationOrientationHelper.IsLeftModeForElement(navigationLocation.SelectedIndex == 0);
            lastNavigationSelectionMode = navigationLocation.SelectedIndex;
        }
    }

    private void toCloneRepoCard_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new DataPackage();
        package.SetText(gitCloneTextBlock.Text);
        Clipboard.SetContent(package);
    }

    private void encryptMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        encryptionMode = (encryptMode.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? string.Empty;
    }
}
