using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Quest_Companion.Converters;

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null && int.TryParse(parameter.ToString(), out int targetVal))
        {
            return intValue == targetVal ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
