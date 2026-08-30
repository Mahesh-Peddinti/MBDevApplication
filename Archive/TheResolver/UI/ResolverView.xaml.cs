using System.Windows.Controls;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    /// <summary>
    /// Interaction logic for ResolverView.xaml
    /// </summary>
    public partial class ResolverView : UserControl
    {
        public ClashViewModel ViewModel { get; }
        public ResolverView()
        {
            InitializeComponent();
            ClashViewModel vm = new ClashViewModel();
            DataContext = vm;
        }
    }
}
