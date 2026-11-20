using System.Globalization;
using Avalonia.Media;
using ElCarbonazo.Desktop.Converters;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class IndicadoresVisualesTests
{
    [Theory]
    [InlineData("Libre", "#FF27E7A0")]
    [InlineData("Ocupada", "#FFFF5B66")]
    [InlineData("Reservada", "#FFFFBF36")]
    [InlineData("Agrupada", "#FF32D9F4")]
    public void MesaUsaVarianteLuminosa(string estado, string color)
    {
        var brush = Assert.IsType<SolidColorBrush>(new EstadoMesaColorConverter().Convert(estado, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse(color), brush.Color);
    }

    [Theory]
    [InlineData("Conectado", "#FF27E7A0")]
    [InlineData("Sin conexión o respuesta inválida. Mostrando últimos datos.", "#FFFF5B66")]
    [InlineData("Cargando mesas…", "#FFFFBF36")]
    [InlineData("Sesión en pausa. Interactúe para continuar.", "#FFFFBF36")]
    [InlineData("La autenticación no es válida para consultar mesas.", "#FFFFBF36")]
    public void ConexionNoIndicaExitoEnEstadosNoConfirmados(string estado, string color)
    {
        var brush = Assert.IsType<SolidColorBrush>(new ConexionColorConverter().Convert(estado, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse(color), brush.Color);
    }
}
