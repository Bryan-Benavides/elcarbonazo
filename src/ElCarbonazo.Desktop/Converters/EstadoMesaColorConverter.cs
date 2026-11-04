using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ElCarbonazo.Desktop.Converters;

public sealed class EstadoMesaColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(Color.Parse((value as string) switch
        {
            "Libre" => "#10B981",
            "Ocupada" => "#EF4444",
            "Reservada" => "#F59E0B",
            "Agrupada" => "#06B6D4",
            _ => "#9CA3AF"
        }));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
