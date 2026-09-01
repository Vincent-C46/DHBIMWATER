using System.Globalization;
using System.Windows.Data;

namespace DHBIMWATER.UI.Converters;

public sealed class FamilyTypeNameToTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? string.Empty;
        var separatorIndex = name.LastIndexOf(" : ", StringComparison.Ordinal);
        return separatorIndex < 0 ? name : name[(separatorIndex + 3)..];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
