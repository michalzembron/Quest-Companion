using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Quest_Companion.Converters;

public static class ThemeHelper
{
    private static Color? _cachedAccentColor;

    public static Color GetWindowsSystemAccentColor()
    {
        if (_cachedAccentColor.HasValue) return _cachedAccentColor.Value;

        try
        {
            using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwmKey != null)
            {
                var accentVal = dwmKey.GetValue("AccentColor");
                if (accentVal is int val && val != 0)
                {
                    byte[] bytes = BitConverter.GetBytes(val);
                    _cachedAccentColor = Color.FromArgb(255, bytes[0], bytes[1], bytes[2]);
                    return _cachedAccentColor.Value;
                }

                var colorVal = dwmKey.GetValue("ColorizationColor");
                if (colorVal is int cVal && cVal != 0)
                {
                    byte[] bytes = BitConverter.GetBytes(cVal);
                    // ARGB
                    _cachedAccentColor = Color.FromArgb(255, bytes[2], bytes[1], bytes[0]);
                    return _cachedAccentColor.Value;
                }
            }

            using var explorerKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (explorerKey != null)
            {
                var palette = explorerKey.GetValue("AccentPalette") as byte[];
                if (palette != null && palette.Length >= 16)
                {
                    _cachedAccentColor = Color.FromArgb(255, palette[12], palette[13], palette[14]);
                    return _cachedAccentColor.Value;
                }
            }
        }
        catch { }

        _cachedAccentColor = Color.FromArgb(255, 0, 178, 148); // Fallback Mint / Teal
        return _cachedAccentColor.Value;
    }

    public static void ApplySystemAccentToApplication()
    {
        var accent = GetWindowsSystemAccentColor();
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent, Wpf.Ui.Appearance.ApplicationTheme.Dark);

        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();

        if (Application.Current != null)
        {
            SetAccentResources(Application.Current.Resources, accent, accentBrush);

            foreach (var md in Application.Current.Resources.MergedDictionaries)
            {
                SetAccentResources(md, accent, accentBrush);
            }
        }
    }

    public static void SetAccentResources(ResourceDictionary dict, Color accent, SolidColorBrush accentBrush)
    {
        dict["SystemAccentColor"] = accent;
        dict["SystemAccentBrush"] = accentBrush;
        dict["SystemAccentColorBrush"] = accentBrush;
        dict["SystemAccentColorPrimary"] = accent;
        dict["SystemAccentColorPrimaryBrush"] = accentBrush;
        dict["AccentFillColorDefaultBrush"] = accentBrush;
        dict["AccentFillColorSecondaryBrush"] = accentBrush;
        dict["AccentTextFillColorPrimaryBrush"] = accentBrush;
        dict["ControlStrongStrokeColorDefaultBrush"] = accentBrush;
        dict["AppAccentBrush"] = accentBrush;
    }

    public static Brush GetSystemAccentBrush()
    {
        return new SolidColorBrush(GetWindowsSystemAccentColor());
    }

    public static Brush GetSubtleHighlightBrush()
    {
        return new SolidColorBrush(Color.FromArgb(255, 47, 50, 58)); // MQDH #2F323A active pill
    }

    public static Brush GetPrimaryTextBrush()
    {
        return Brushes.White;
    }

    public static Brush GetSecondaryTextBrush()
    {
        return new SolidColorBrush(Color.FromArgb(255, 156, 163, 175)); // MQDH #9CA3AF muted text
    }
}

public class TabActiveBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(255, 47, 50, 58)); // #2F323A

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? ActiveBrush : Brushes.Transparent;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class TabActiveIndicatorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? ThemeHelper.GetSystemAccentBrush() : Brushes.Transparent;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class TabActiveForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = Brushes.White;
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 156, 163, 175)); // #9CA3AF

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? ActiveBrush : InactiveBrush;
        }
        return InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class RefreshRateMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class IntMatchToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBg = new(Color.FromArgb(255, 56, 56, 62)); // #38383E
    private static readonly SolidColorBrush InactiveBg = new(Color.FromArgb(255, 42, 45, 52)); // #2A2D34

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? ActiveBg : InactiveBg;
        }
        return InactiveBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class IntMatchToBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBorder = new(Color.FromArgb(255, 34, 197, 94)); // Meta Green #22C55E
    private static readonly SolidColorBrush InactiveBorder = new(Color.FromArgb(255, 59, 62, 71)); // #3B3E47

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? ActiveBorder : InactiveBorder;
        }
        return InactiveBorder;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public class IntMatchToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return current == target ? FontWeights.Bold : FontWeights.Normal;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
