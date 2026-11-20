using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ElCarbonazo.Desktop.Converters;

public sealed class EstadoInventarioColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(Color.Parse((value as string) switch
        {
            "Agotado" => "#FF5B66",
            "StockBajo" => "#FFBF36",
            "Normal" => "#27E7A0",
            _ => "#9CA3AF"
        }));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
