using System.Windows.Controls;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    /// <summary>
    /// Interaction logic for ResolverView.xaml
    /// </summary>
    public partial class ResolverView : UserControl
    {
        public ResolverView()
        {
            InitializeComponent();

            // The property used to stay null, so callers that wanted to drive
            // the pane from a command had nothing to talk to.
            ViewModel = new ClashViewModel();

            DataContext = ViewModel;
        }

        public ClashViewModel ViewModel { get; }
    }
}
