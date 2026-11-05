using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ElCarbonazo.Desktop.Converters;

public sealed class EstadoMesaColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(Color.Parse((value as string) switch
        {
            "Libre" => "#27E7A0",
            "Ocupada" => "#FF5B66",
            "Reservada" => "#FFBF36",
            "Agrupada" => "#32D9F4",
            _ => "#9CA3AF"
        }));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
