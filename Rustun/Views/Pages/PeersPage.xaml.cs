using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Rustun.ViewModels.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Rustun.Views.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PeersPage : Page
    {
        private PeersPageViewModel viewModel = new PeersPageViewModel();

        public PeersPage()
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            viewModel.Dispose();
        }
    }
}
