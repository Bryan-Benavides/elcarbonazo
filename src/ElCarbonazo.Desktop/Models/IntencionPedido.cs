namespace ElCarbonazo.Desktop.Models;

public sealed record LineaIntencionPedido(int IdProducto, int VersionProducto, int Cantidad, string Notas,
    string Nombre, decimal PrecioUnitario);

public sealed class IntencionPedido
{
    public Guid EnvioId { get; }
    public int? MesaId { get; }
    public int? GrupoId { get; }
    public uint? VersionMesa { get; }
    public IReadOnlyList<LineaIntencionPedido> Lineas { get; }

    public IntencionPedido(int? mesaId, int? grupoId, uint? versionMesa, IEnumerable<LineaIntencionPedido> lineas)
    {
        EnvioId = Guid.NewGuid();
        MesaId = mesaId;
        GrupoId = grupoId;
        VersionMesa = versionMesa;
        Lineas = Array.AsReadOnly(lineas.ToArray());
    }
}

public sealed record EnvioConfirmadoPedidoDto(CuentaPedidoDto Pedido, int IdComanda, uint? VersionMesa);
