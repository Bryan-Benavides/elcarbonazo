using ElCarbonazo.Api.Domain;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaOperacionCanonicaTests
{
    [Fact]
    public void Equivalentes_normalizan_decimales_y_unicode_con_hash_estable()
    {
        var first = CajaOperacionCanonica.Cobrar(1, 2, 10m, new string('a', 64), CajaStates.Tarjeta, null, "  Cafe\u0301  ", true, false);
        var second = CajaOperacionCanonica.Cobrar(1, 2, 10.00m, new string('a', 64), CajaStates.Tarjeta, null, "Café", true, false);
        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.NotEqual(first.HashArgumentos, CajaOperacionCanonica.Cobrar(1, 2, 10m, new string('a', 64), CajaStates.Tarjeta, null, "Café", true, true)!.HashArgumentos);
    }

    [Fact]
    public void Rechaza_importes_metodos_y_textos_invalidos_sin_rechazar_efectivo_insuficiente()
    {
        Assert.Null(CajaOperacionCanonica.Abrir(0, false));
        Assert.NotNull(CajaOperacionCanonica.Abrir(0, true));
        Assert.Null(CajaOperacionCanonica.Abrir(0.001m, true));
        Assert.Null(CajaOperacionCanonica.Abrir(10000000000m, true));
        var hash = new string('a', 64);
        Assert.NotNull(CajaOperacionCanonica.Cobrar(1, 2, 10, hash, CajaStates.Efectivo, 9, null, false, false));
        Assert.Null(CajaOperacionCanonica.Cobrar(1, 2, 10, hash, CajaStates.Tarjeta, null, "\n", true, false));
        Assert.Null(CajaOperacionCanonica.Cobrar(1, 2, 10, hash, CajaStates.Tarjeta, null, "\nref", true, false));
        Assert.Null(CajaOperacionCanonica.Cobrar(1, 2, 10, hash, CajaStates.Tarjeta, null, "ref", false, false));
        Assert.Null(CajaOperacionCanonica.Cobrar(1, 2, 10, hash.ToUpperInvariant(), CajaStates.Efectivo, 10, null, false, false));
        Assert.Null(CajaOperacionCanonica.Cerrar(1, 0, "a\nb", true));
    }
}
