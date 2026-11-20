using System.Globalization;
using Avalonia.Data.Converters;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Converters;

public sealed class MesaSeleccionadaConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count >= 2 && values[0] is MesaDto actual &&
        ((values[1] is MesaDto seleccionada && actual.IdMesa == seleccionada.IdMesa) ||
         (values.Count >= 3 && values[2] is int[] ids && ids.Contains(actual.IdMesa)));
}
