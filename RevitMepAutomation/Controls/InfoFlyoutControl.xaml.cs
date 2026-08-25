using System;
using System.Windows;
using System.Windows.Controls;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Controls
{
    public partial class InfoFlyoutControl : UserControl
    {
        public static readonly DependencyProperty CloseRequestedProperty =
            DependencyProperty.Register(nameof(CloseRequested), typeof(Action), typeof(InfoFlyoutControl));

        public Action? CloseRequested
        {
            get => (Action?)GetValue(CloseRequestedProperty);
            set => SetValue(CloseRequestedProperty, value);
        }

        public InfoFlyoutControl()
        {
            InitializeComponent();
        }

        public void UpdateDetails(ClashItemModel? clash)
        {
            if (clash == null)
            {
                TxtHostElement.Text = "No clash selected";
                TxtObstacle.Text = "-";
                TxtCoordinates.Text = "-";
                TxtSafety.Text = "-";
                return;
            }

            TxtHostElement.Text = $"{clash.ElementCategory} (ID: {clash.ElementId})";
            TxtObstacle.Text = $"{clash.ObstacleCategory} (ID: {clash.ObstacleId})";
            TxtCoordinates.Text = $"X:{clash.ClashX:0.#}, Y:{clash.ClashY:0.#}, Z:{clash.ClashZ:0.#}";
            TxtSafety.Text = (clash.TopClearanceMm >= 30) ? $"✓ Safe (+{clash.TopClearanceMm:0}mm)" : "⚠ Low Clearance";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }
    }
}
