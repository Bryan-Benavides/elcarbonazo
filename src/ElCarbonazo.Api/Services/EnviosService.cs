using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record EnvioResult(int Status, string? Code = null, string? Json = null, string? Location = null, object? Data = null);

public sealed class EnviosService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    private const decimal MaxTotal = 9999999999.99m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EnvioResult> EnviarAsync(EnvioRequest request, int userId, CancellationToken cancellationToken)
    {
        var guid = request.EnvioId.ToString("D");
        if (request.EnvioId == Guid.Empty || guid[14] != '4' || "89ab".IndexOf(guid[19], StringComparison.Ordinal) < 0 ||
            (request.MesaId is null) == (request.GrupoId is null) ||
            request.MesaId is <= 0 || request.GrupoId is <= 0 ||
            (request.MesaId is not null && request.VersionMesa is null) ||
            (request.GrupoId is not null && request.VersionMesa is not null) ||
            request.Lineas is null || request.Lineas.Count is < 1 or > 50 ||
            request.Lineas.Any(line => line is null || line.IdProducto <= 0 || line.VersionProducto <= 0 || line.Cantidad is < 1 or > 99))
        {
            return new EnvioResult(400, "VALIDATION_ERROR");
        }

        var lines = new List<EnvioLineaRequest>();
        foreach (var maybeLine in request.Lineas)
        {
            var line = maybeLine!;
            string note;
            try
            {
                note = (line.Notas ?? string.Empty).Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return new EnvioResult(400, "VALIDATION_ERROR");
            }

            if (note.EnumerateRunes().Count() > 150 || note.EnumerateRunes().Any(rune => Rune.IsControl(rune)))
            {
                return new EnvioResult(400, "VALIDATION_ERROR");
            }
            lines.Add(line with { Notas = note });
        }

        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.MesaId,
            request.GrupoId,
            request.VersionMesa,
            Lineas = lines
        }, JsonOptions);
        var hash = Convert.ToHexStringLower(SHA256.HashData(canonical));
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(request.EnvioId.ToByteArray()));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        var existing = await context.EnviosPedido.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EnvioId == request.EnvioId, cancellationToken);
        if (existing is not null)
        {
            return existing.IdUsuario == userId && existing.HashSolicitud == hash
                ? new EnvioResult(existing.StatusCode, Json: existing.ResponseJson, Location: existing.Location)
                : new EnvioResult(409, "ENVIO_ID_REUTILIZADO");
        }

        Mesa? mesa = null;
        if (request.MesaId is int mesaId)
        {
            mesa = await context.Mesas.FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_mesa = {mesaId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (mesa is null) return new EnvioResult(404, "HTTP_NO_ENCONTRADO");
            if (mesa.Version != request.VersionMesa) return new EnvioResult(409, "MESA_CONFLICTO_CONCURRENCIA");
            if (mesa.IdGrupo is not null || mesa.Estado == MesaStates.Agrupada)
                return new EnvioResult(409, "DESTINO_STATE_CONFLICT");
        }
        else
        {
            var groupId = request.GrupoId!.Value;
            var group = await context.GruposMesa.FromSqlInterpolated($"""
                SELECT * FROM grupo_mesa WHERE id_grupo = {groupId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (group is null || !group.Activo) return new EnvioResult(404, "HTTP_NO_ENCONTRADO");
            var members = await context.Mesas.FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_grupo = {groupId} ORDER BY id_mesa FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (members.Count < 2 || members.Any(item => item.Estado != MesaStates.Agrupada))
                return new EnvioResult(409, "DESTINO_STATE_CONFLICT");
            if (await context.Pedidos.AsNoTracking().AnyAsync(item => item.IdGrupo == groupId && item.Estado == PedidoStates.Pagado, cancellationToken))
                return new EnvioResult(409, "GRUPO_PAGADO_REQUIERE_CIERRE");
        }

        var pedido = await context.Pedidos.SingleOrDefaultAsync(item =>
            (request.MesaId != null ? item.IdMesa == request.MesaId : item.IdGrupo == request.GrupoId) &&
            PedidoStates.Active.Contains(item.Estado), cancellationToken);
        if (pedido is not null)
        {
            pedido = await context.Pedidos.FromSqlInterpolated($"""
                SELECT * FROM pedido WHERE id_pedido = {pedido.IdPedido} FOR UPDATE
                """).SingleAsync(cancellationToken);
            if (!await context.Comandas.AnyAsync(item => item.IdPedido == pedido.IdPedido, cancellationToken))
                return new EnvioResult(409, "CUENTA_LEGADA_SIN_COMANDAS");
            if (await CajaPendientes.HayCobroParaCuentaAsync(context, pedido.IdPedido, Guid.Empty, cancellationToken))
                return new EnvioResult(409, "OPERACION_NO_TERMINAL");
        }
        if (mesa is not null && ((pedido is null && mesa.Estado is not (MesaStates.Libre or MesaStates.Reservada)) ||
                                 (pedido is not null && mesa.Estado != MesaStates.Ocupada)))
            return new EnvioResult(409, "DESTINO_STATE_CONFLICT");

        var ids = lines.Select(item => item.IdProducto).Distinct().Order().ToArray();
        var products = await context.Productos.FromSqlInterpolated($"""
            SELECT * FROM producto WHERE id_producto = ANY({ids}) ORDER BY id_producto FOR SHARE
            """).ToListAsync(cancellationToken);
        var byId = products.ToDictionary(item => item.IdProducto);
        if (lines.Any(line => !byId.TryGetValue(line.IdProducto, out var product) ||
                              !product.Disponible || product.Version != line.VersionProducto))
            return new EnvioResult(409, "CATALOGO_CAMBIADO", Data: ids.Select(id => new
            {
                IdProducto = id,
                Existe = byId.ContainsKey(id),
                Nombre = byId.GetValueOrDefault(id)?.Nombre,
                Precio = byId.GetValueOrDefault(id)?.Precio,
                Disponible = byId.GetValueOrDefault(id)?.Disponible,
                Version = byId.GetValueOrDefault(id)?.Version
            }).ToArray());

        var amount = lines.Sum(line => byId[line.IdProducto].Precio * line.Cantidad);
        if (amount + (pedido?.Subtotal ?? 0m) > MaxTotal) return new EnvioResult(400, "VALIDATION_ERROR");
        var now = time.GetUtcNow();
        if (pedido is null)
        {
            pedido = new Pedido { IdMesa = request.MesaId, IdGrupo = request.GrupoId, IdUsuario = userId, FechaHora = now };
            context.Pedidos.Add(pedido);
            if (mesa is not null) mesa.Estado = MesaStates.Ocupada;
        }
        pedido.Subtotal += amount;
        pedido.Impuestos = 0;
        pedido.Total = pedido.Subtotal;
        pedido.Estado = PedidoStates.Abierto;
        var command = new Comanda { Pedido = pedido, IdUsuario = userId, FechaHora = now, Estado = ComandaStates.Abierta };
        foreach (var line in lines)
        {
            var product = byId[line.IdProducto];
            command.Detalles.Add(new DetallePedido
            {
                IdProducto = product.IdProducto,
                NombreProducto = product.Nombre,
                Cantidad = (short)line.Cantidad,
                PrecioUnitario = product.Precio,
                Subtotal = product.Precio * line.Cantidad,
                Notas = line.Notas!
            });
        }
        context.Comandas.Add(command);
        await context.SaveChangesAsync(cancellationToken);
        audit.Record("ComandaEnviada", "Exitoso", userId, entityType: "comanda", entityId: command.IdComanda.ToString(),
            details: new Dictionary<string, object?>
            {
                ["idPedido"] = pedido.IdPedido,
                ["idComanda"] = command.IdComanda,
                ["idMesa"] = request.MesaId,
                ["idGrupo"] = request.GrupoId,
                ["envioId"] = request.EnvioId,
                ["cantidadLineas"] = lines.Count
            });
        var allCommands = await context.Comandas.AsNoTracking().Include(item => item.Detalles)
            .Where(item => item.IdPedido == pedido.IdPedido)
            .ToListAsync(cancellationToken);
        var commandDtos = allCommands.OrderBy(item => item.FechaHora).ThenBy(item => item.IdComanda)
            .Select(item => new CuentaComandaDto(item.IdComanda, item.IdUsuario, item.FechaHora, item.Estado, item.Version,
                item.Detalles.OrderBy(detail => detail.IdDetalle).Select(detail => new CuentaDetalleDto(
                    detail.IdDetalle, detail.IdProducto, detail.NombreProducto, detail.Cantidad,
                    detail.PrecioUnitario, detail.Subtotal, detail.Notas)).ToArray())).ToArray();
        var accountDto = new CuentaDto(pedido.IdPedido, pedido.IdMesa, pedido.IdGrupo, pedido.IdUsuario, pedido.FechaHora,
            pedido.Subtotal, pedido.Impuestos, pedido.Total, pedido.Estado, false, null, commandDtos);
        var response = JsonSerializer.Serialize(new EnvioConfirmadoDto(accountDto, command.IdComanda, mesa?.Version), JsonOptions);
        var location = $"/api/pedidos/activo?{(request.MesaId is not null ? $"mesaId={request.MesaId}" : $"grupoId={request.GrupoId}")}";
        context.EnviosPedido.Add(new EnvioPedido
        {
            EnvioId = request.EnvioId,
            IdUsuario = userId,
            HashSolicitud = hash,
            IdComanda = command.IdComanda,
            StatusCode = 201,
            Location = location,
            ResponseJson = response,
            FechaHora = now
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EnvioResult(201, Json: response, Location: location);
    }
}
