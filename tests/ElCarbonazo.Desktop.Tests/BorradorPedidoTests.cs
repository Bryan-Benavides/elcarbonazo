using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class BorradorPedidoTests
{
    [Fact]
    public void Shared_unicode_cases_match_desktop_normalization()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pedido-notas.json")));
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var text = string.Concat(Enumerable.Repeat(item.GetProperty("texto").GetString()!, item.GetProperty("repeticiones").GetInt32()));
            var valid = NotaPedido.TryNormalizar(text, out var normalized);
            Assert.Equal(item.GetProperty("valida").GetBoolean(), valid);
            if (valid && item.TryGetProperty("normalizada", out var expected))
                Assert.Equal(expected.GetString(), normalized);
        }
        Assert.False(NotaPedido.TryNormalizar("\ud800", out _));
    }

    [Fact]
    public void Draft_keeps_repeated_lines_separate_and_rejects_invalid_changes()
    {
        var draft = new BorradorPedido();
        draft.Agregar(1, "Producto DEMO", "Comida", 10.10m, 1);
        draft.Agregar(1, "Producto DEMO", "Comida", 10.10m, 1);
        draft.Actualizar(0, 2, "Cafe\u0301");
        draft.Actualizar(1, 1, "Otra nota");
        Assert.Equal(30.30m, draft.Subtotal);
        Assert.Equal("Café", draft.Lineas[0].Notas);
        Assert.Equal("Otra nota", draft.Lineas[1].Notas);
        Assert.Throws<ArgumentException>(() => draft.Actualizar(0, 100, ""));
        Assert.Throws<ArgumentException>(() => draft.Actualizar(0, 1, "inválida\n"));
        Assert.Equal(2, draft.Lineas[0].Cantidad);
        draft.Retirar(1);
        Assert.Single(draft.Lineas);
        Assert.Equal(20.20m, draft.Subtotal);
    }

    [Fact]
    public void Draft_enforces_line_count_and_price_boundaries()
    {
        var draft = new BorradorPedido();
        Assert.Throws<ArgumentException>(() => draft.Agregar(1, "Prueba", "Comida", 0m, 1));
        Assert.Throws<ArgumentException>(() => draft.Agregar(1, "Prueba", "Comida", 1.001m, 1));
        Assert.Throws<ArgumentException>(() => draft.Agregar(1, "Prueba", "Comida", 1000000m, 1));
        for (var i = 0; i < 50; i++) draft.Agregar(1, "Prueba", "Comida", 0.01m, 1);
        Assert.Throws<ArgumentException>(() => draft.Agregar(1, "Prueba", "Comida", 0.01m, 1));
        Assert.Equal(50, draft.Lineas.Count);
    }
}
