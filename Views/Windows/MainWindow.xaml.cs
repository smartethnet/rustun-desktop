using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Rustun.Helpers;
using Rustun.Views.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private bool _isInitialNavigation = true;
        private OverlappedPresenter? WindowPresenter { get; }
        private OverlappedPresenterState CurrentWindowState { get; set; }
        public NavigationView NavigationView
        {
            get { return NavigationViewControl; }
        }
        public Action? NavigationViewLoaded { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            // Hide the default system title bar.
            ExtendsContentIntoTitleBar = true;
            // Replace system title bar with the WinUI TitleBar.
            SetTitleBar(AppTitleBar);

            // Workaround for WinUI issue #9934:
            // https://github.com/microsoft/microsoft-ui-xaml/issues/9934.
            // See `AdjustNavigationViewMargin()` for implementation details.
            if (AppWindow.Presenter is OverlappedPresenter windowPresenter)
            {
                WindowPresenter = windowPresenter;
                CurrentWindowState = WindowPresenter.State;
                AdjustNavigationViewMargin(force: true);
                AppWindow.Changed += (_, _) => AdjustNavigationViewMargin();
            }
        }

        // Wraps a call to rootFrame.Navigate to give the Page a way to know which NavigationRootPage is navigating.
        // Please call this function rather than rootFrame.Navigate to navigate the rootFrame.
        public void Navigate(Type pageType, object? targetPageArguments = null, NavigationTransitionInfo? navigationTransitionInfo = null)
        {
            RootFrame.Navigate(pageType, targetPageArguments, navigationTransitionInfo);
        }

        private void AdjustNavigationViewMargin(bool? force = null)
        {
            if (WindowPresenter is null ||
                (WindowPresenter.State == CurrentWindowState && force is not true))
            {
                return;
            }

            NavigationViewControl.Margin = WindowPresenter.State == OverlappedPresenterState.Maximized
                ? new Thickness(0, -1, 0, 0)
                : new Thickness(0, -2, 0, 0);
            CurrentWindowState = WindowPresenter.State;
        }

        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                RootFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItemContainer != null)
            {
                var selectedItemTag = args.SelectedItemContainer.Tag.ToString();
                switch (selectedItemTag)
                {
                    case "home":
                        RootFrame.Navigate(typeof(HomePage));
                        break;
                    case "log":
                        RootFrame.Navigate(typeof(LogPage));
                        break;
                    default:
                        break;
                }
            }
        }

        private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavigationViewControl.IsPaneOpen = !NavigationViewControl.IsPaneOpen;
        }

        private void TitleBar_BackRequested(TitleBar sender, object args)
        {
            if (this.RootFrame.CanGoBack)
            {
                this.RootFrame.GoBack();
            }
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            foreach (var menuItem in NavigationViewControl.MenuItems)
            {
                if (menuItem is NavigationViewItem navItem)
                {
                    if (navItem.Tag.ToString() == e.SourcePageType.Name.Replace("Page", "").ToLower())
                    {
                        NavigationViewControl.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        private void OnNavigationViewControlLoaded(object sender, RoutedEventArgs e)
        {
            // Delay necessary to ensure NavigationView visual state can match navigation
            Task.Delay(500).ContinueWith(_ => this.NavigationViewLoaded?.Invoke(), TaskScheduler.FromCurrentSynchronizationContext());

            var navigationView = sender as NavigationView;
            navigationView?.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnIsPaneOpenChanged);
        }

        private void OnIsPaneOpenChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (sender is not NavigationView navigationView)
            {
                return;
            }

            var announcementText = navigationView.IsPaneOpen ? "Navigation Pane Opened" : "Navigation Pane Closed";

            UIHelper.AnnounceActionForAccessibility(navigationView, announcementText, "NavigationViewPaneIsOpenChangeNotificationId");
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // We need to set the minimum size here because the XamlRoot is not available in the constructor.
            WindowHelper.SetWindowMinSize(this, 800, 400);

            if (sender is FrameworkElement rootGrid && rootGrid.XamlRoot is not null)
            {
                rootGrid.XamlRoot.Changed += RootGridXamlRoot_Changed;
            }

            NavigationOrientationHelper.UpdateNavigationViewForElement(NavigationOrientationHelper.IsLeftMode());
            TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
        }

        private void RootGridXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            WindowHelper.SetWindowMinSize(this, 800, 400);
        }
    }
}
