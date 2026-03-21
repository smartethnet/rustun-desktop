using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.BadgeNotifications;
using NetWintun;
using Rustun.Helpers;
using Rustun.Services;
using Rustun.Views.Pages;
using Rustun.Views.Windows;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        internal static MainWindow MainWindow { get; private set; } = null!;
        internal static VpnService VpnService { get; private set; } = VpnService.Instance;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            SetupLogging();
            VpnService.start("192.168.100.1", "255.255.255.0");
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();

            WindowHelper.TrackWindow(MainWindow);
            EnsureWindow();

            MainWindow.Closed += (s, e) =>
            {
                if (NativeMethods.IsAppPackaged)
                {
                    BadgeNotificationManager.Current.ClearBadge();
                }

                // Close all remaining active windows to prevent resource disposal conflicts
                var activeWindows = new List<Window>(WindowHelper.ActiveWindows);
                foreach (var window in activeWindows)
                {
                    if (!window.Equals(s)) // Don't try to close the window that's already closing
                    {
                        try
                        {
                            window.Close();
                        }
                        catch
                        {
                            // Ignore any exceptions during cleanup
                        }
                    }
                }

                // Flush and close the logger to ensure all logs are written before the application exits
                Log.CloseAndFlush();
            };
        }

        private async void EnsureWindow()
        {
            ThemeHelper.Initialize();

            var targetPageType = typeof(HomePage);
            var targetPageArguments = string.Empty;
            MainWindow.Navigate(targetPageType, targetPageArguments);

            if (targetPageType == typeof(HomePage))
            {
                var navItem = (NavigationViewItem)MainWindow.NavigationView.MenuItems[0];
                navItem.IsSelected = true;
            }

            // Activate the startup window.
            MainWindow.Activate();
        }

        private void SetupLogging()
        {
            var logFile = Path.Combine(WindowHelper.GetAppLocalFolder().Path, "logs", "rustun-.log");
            Log.Logger = new LoggerConfiguration()
               .MinimumLevel.Verbose()
               .WriteTo.File(logFile, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31)
               .CreateLogger();
        }
    }
}
