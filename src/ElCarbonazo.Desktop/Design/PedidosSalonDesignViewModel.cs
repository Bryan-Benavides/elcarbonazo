using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;

namespace ElCarbonazo.Desktop.Design;

/// <summary>Datos locales para el previsualizador; no se conecta al transporte.</summary>
public sealed class PedidosSalonDesignViewModel : PedidosSalonViewModel
{
    public PedidosSalonDesignViewModel()
        : base(1, null, "Mesa 03", new CatalogoDiseno(), new CuentaDiseno(), () => { })
    {
        if (Productos.Count > 0) AgregarCommand.Execute(Productos[0]);
    }

    private sealed class CatalogoDiseno : IProductosPedidoApiService
    {
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaginaProductosDto(
            [
                new ProductoPedidoDto(1, "Gallo pinto DEMO", "Comida", 85m, true, 1),
                new ProductoPedidoDto(2, "Quesillo DEMO", "Comida", 95m, true, 1)
            ], null));
    }

    private sealed class CuentaDiseno : ICuentaPedidoApiService
    {
        public Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken = default)
        {
            var line = new DetallePedidoDto(1, 1, "Gallo pinto DEMO", 1, 85m, 85m, "Sin picante");
            var command = new ComandaPedidoDto(1, 1, DateTimeOffset.UnixEpoch, "Lista", 1, [line]);
            return Task.FromResult(new CuentaActivaPedidoDto(new CuentaPedidoDto(
                1, mesaId, grupoId, 1, DateTimeOffset.UnixEpoch, 85m, 0m, 85m, "Listo", false, null, [command])));
        }
    }
}
