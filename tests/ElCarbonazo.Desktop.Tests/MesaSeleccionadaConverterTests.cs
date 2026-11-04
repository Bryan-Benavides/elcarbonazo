using System.Globalization;
using ElCarbonazo.Desktop.Converters;
using ElCarbonazo.Desktop.Models;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesaSeleccionadaConverterTests
{
    [Fact]
    public void ComparesIdsInsteadOfObjectInstances()
    {
        var converter = new MesaSeleccionadaConverter();
        var actual = new MesaDto(1, null, 1, 4, "Libre", 2);
        var selected = new MesaDto(1, null, 1, 4, "Ocupada", 1);
        var other = new MesaDto(2, null, 2, 4, "Libre", 2);

        Assert.Equal(true, converter.Convert([actual, selected], typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert([other, selected], typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert([actual, null], typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
