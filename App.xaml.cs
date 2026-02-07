using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.BadgeNotifications;
using Rustun.Helpers;
using Rustun.Views.Pages;
using Rustun.Views.Windows;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Activation;

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

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
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
    }
}
