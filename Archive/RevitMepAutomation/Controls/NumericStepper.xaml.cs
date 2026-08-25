using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RevitMepAutomation.Controls
{
    public partial class NumericStepper : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(double),
                typeof(NumericStepper),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum),
                typeof(double),
                typeof(NumericStepper),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(NumericStepper),
                new PropertyMetadata(10000.0));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(
                nameof(Step),
                typeof(double),
                typeof(NumericStepper),
                new PropertyMetadata(10.0));

        public static readonly DependencyProperty UnitSuffixProperty =
            DependencyProperty.Register(
                nameof(UnitSuffix),
                typeof(string),
                typeof(NumericStepper),
                new PropertyMetadata("mm", OnValueChanged));

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(
                nameof(DecimalPlaces),
                typeof(int),
                typeof(NumericStepper),
                new PropertyMetadata(0, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, Math.Max(Minimum, Math.Min(Maximum, value)));
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public string UnitSuffix
        {
            get => (string)GetValue(UnitSuffixProperty);
            set => SetValue(UnitSuffixProperty, value);
        }

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        public string FormattedValue
        {
            get
            {
                string format = DecimalPlaces > 0 ? $"F{DecimalPlaces}" : "F0";
                return string.IsNullOrWhiteSpace(UnitSuffix)
                    ? Value.ToString(format, CultureInfo.InvariantCulture)
                    : $"{Value.ToString(format, CultureInfo.InvariantCulture)} {UnitSuffix}";
            }
        }

        public NumericStepper()
        {
            InitializeComponent();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericStepper stepper)
            {
                stepper.UpdateText();
            }
        }

        private void UpdateText()
        {
            TxtValue.Text = FormattedValue;
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            Value = Math.Min(Maximum, Value + Step);
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            Value = Math.Max(Minimum, Value - Step);
        }

        private void TxtValue_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseInput();
        }

        private void TxtValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ParseInput();
                TxtValue.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            else if (e.Key == Key.Up)
            {
                BtnUp_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Down)
            {
                BtnDown_Click(this, new RoutedEventArgs());
            }
        }

        private void ParseInput()
        {
            string raw = TxtValue.Text ?? "";
            if (!string.IsNullOrEmpty(UnitSuffix))
            {
                raw = raw.Replace(UnitSuffix, "");
            }
            raw = raw.Trim();

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) ||
                double.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
            {
                Value = Math.Max(Minimum, Math.Min(Maximum, parsed));
            }
            UpdateText();
        }
    }
}
