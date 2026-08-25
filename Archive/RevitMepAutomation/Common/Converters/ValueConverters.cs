using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Common.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        public SolidColorBrush ResolvedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // #28a745
        public SolidColorBrush NotResolvedBrush { get; set; } = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // #dc3545
        public SolidColorBrush IgnoredBrush { get; set; } = new SolidColorBrush(Color.FromRgb(108, 117, 125)); // #6c757d
        public SolidColorBrush PendingBrush { get; set; } = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // #ffc107

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ClashStatus status)
            {
                switch (status)
                {
                    case ClashStatus.Resolved:
                        return ResolvedBrush;
                    case ClashStatus.NotResolved:
                        return NotResolvedBrush;
                    case ClashStatus.Ignored:
                        return IgnoredBrush;
                    case ClashStatus.Pending:
                        return PendingBrush;
                }
            }
            return NotResolvedBrush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ClashStatus status)
            {
                switch (status)
                {
                    case ClashStatus.Resolved:
                        return "Resolved";
                    case ClashStatus.NotResolved:
                        return "Not Resolved";
                    case ClashStatus.Ignored:
                        return "Ignored";
                    case ClashStatus.Pending:
                        return "Pending";
                }
            }
            return "Unknown";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DirectionToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is RoutingDirection direction && parameter is string targetDirStr)
            {
                if (Enum.TryParse<RoutingDirection>(targetDirStr, true, out var targetDir))
                {
                    return direction == targetDir;
                }
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter is string targetDirStr)
            {
                if (Enum.TryParse<RoutingDirection>(targetDirStr, true, out var targetDir))
                {
                    return targetDir;
                }
            }
            return Binding.DoNothing;
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Inverted { get; set; } = false;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool val = false;
            if (value is bool b) val = b;

            if (Inverted) val = !val;

            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Visibility vis)
            {
                bool val = (vis == Visibility.Visible);
                return Inverted ? !val : val;
            }
            return false;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }

    public class AngleToStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return $"{d:0.#}°";
            }
            return $"{value}°";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                string cleaned = s.Replace("°", "").Trim();
                if (double.TryParse(cleaned, NumberStyles.Any, culture, out double res))
                    return res;
            }
            return 0.0;
        }
    }
}
