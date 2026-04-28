using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Rustun.Helpers;
using Rustun.Services;
using Rustun.Views.Pages;
using System;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Windows
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
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

            // 将 VpnService 的连接状态回调投递到 UI 线程
            VpnService.Instance.AttachUiDispatcher(DispatcherQueue.GetForCurrentThread());
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
            if (!RootFrame.CanGoBack)
            {
                return;
            }

            RootFrame.GoBack();
            // GoBack 后 Navigated 也会触发；此处再同步一次，避免仅依赖 SourcePageType 导致选中项不更新。
            UpdateNavigationViewSelectionForPage(GetCurrentFramePageType());
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            var pageType = (e.Content as Page)?.GetType() ?? e.SourcePageType;
            UpdateNavigationViewSelectionForPage(pageType);
        }

        /// <summary>
        /// 根据当前 Frame 中的页面类型，同步 <see cref="NavigationView"/> 的选中项（含设置项）。
        /// </summary>
        private void UpdateNavigationViewSelectionForPage(Type? pageType)
        {
            if (pageType == typeof(SettingsPage))
            {
                NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
                return;
            }

            var key = pageType?.Name.Replace("Page", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            foreach (var menuItem in NavigationViewControl.MenuItems)
            {
                if (menuItem is NavigationViewItem navItem &&
                    navItem.Tag is string tag &&
                    tag.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    NavigationViewControl.SelectedItem = navItem;
                    return;
                }
            }
        }

        private Type? GetCurrentFramePageType()
        {
            if (RootFrame.Content is Page page)
            {
                return page.GetType();
            }

            return RootFrame.CurrentSourcePageType;
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
