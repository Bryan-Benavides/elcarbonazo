using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ElCarbonazo.Desktop.Converters;

public sealed class ConexionColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(Color.Parse(value switch
        {
            "Conectado" => "#27E7A0",
            "Sin conexión o respuesta inválida. Mostrando últimos datos." => "#FF5B66",
            _ => "#FFBF36"
        }));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
