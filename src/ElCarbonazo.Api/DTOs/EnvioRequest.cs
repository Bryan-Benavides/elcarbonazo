namespace ElCarbonazo.Api.DTOs;

public sealed record EnvioRequest(Guid EnvioId, int? MesaId, int? GrupoId, uint? VersionMesa, List<EnvioLineaRequest?>? Lineas);

public sealed record EnvioLineaRequest(int IdProducto, int VersionProducto, int Cantidad, string? Notas);

public sealed record EnvioConfirmadoDto(CuentaDto Pedido, int IdComanda, uint? VersionMesa);
