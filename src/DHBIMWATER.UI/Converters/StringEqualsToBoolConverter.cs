using System.Globalization;
using System.Windows.Data;

namespace DHBIMWATER.UI.Converters;

/// <summary>바인딩 값이 ConverterParameter 문자열과 같으면 true.
/// ConvertBack은 true일 때만 파라미터를 되돌려 주고 false는 Binding.DoNothing이라,
/// 같은 소스 프로퍼티에 묶인 RadioButton 그룹이 서로의 값을 지우지 않는다.</summary>
public sealed class StringEqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter as string : Binding.DoNothing;
}
