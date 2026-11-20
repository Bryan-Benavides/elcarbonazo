using System.Security.Claims;
using System.Text.Json;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja)]
[Route("api/caja/operaciones")]
public sealed class CajaOperacionesController(CajaOperacionesService operations, CajaCancelacionService cancellations) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Preparar(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null || Session() is null) return Error(401, "AUTH_NO_AUTENTICADO");
        var (json, error) = await ReadJsonAsync(32768, ct);
        if (error != 0) return Error(error, error == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        using (json)
        {
            var intent = ParseIntent(json!.RootElement);
            if (intent is null) return Error(400, "VALIDATION_ERROR");
            return Reply(await operations.PrepararAsync(id, actor.Value, intent, ct, Session()));
        }
    }

    [HttpPost("{id:guid}/ejecucion")]
    public async Task<IActionResult> Ejecutar(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null || Session() is null) return Error(401, "AUTH_NO_AUTENTICADO");
        var (json, error) = await ReadJsonAsync(16384, ct);
        if (error != 0) return Error(error, error == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        using (json)
        {
            if (!Fields(json!.RootElement)) return Error(400, "VALIDATION_ERROR");
            return Reply(await operations.EjecutarAsync(id, actor.Value, ct, Session()));
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Consultar(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        return actor is null || Session() is null ? Error(401, "AUTH_NO_AUTENTICADO") : Reply(await operations.ConsultarAsync(id, actor.Value, ct, Session()));
    }

    [HttpPost("{id:guid}/cancelacion")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null || Session() is null) return Error(401, "AUTH_NO_AUTENTICADO");
        var (json, error) = await ReadJsonAsync(16384, ct);
        if (error != 0) return Error(error, error == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        using (json)
        {
            var root = json!.RootElement;
            if (!Fields(root, "confirmaIntervencion") || !Bool(root, "confirmaIntervencion", out var confirmed))
                return Error(400, "VALIDATION_ERROR");
            return Reply(await cancellations.CancelarAsync(id, actor.Value, confirmed, ct, Session()));
        }
    }

    [HttpGet("{id:guid}/evidencia-conciliacion")]
    public async Task<IActionResult> Evidencia(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null || Session() is null) return Error(401, "AUTH_NO_AUTENTICADO");
        var (error, evidencia) = await operations.EvidenciaAsync(id, actor.Value, ct, Session());
        return evidencia is null ? Error(error.Status, error.Code) : Ok(evidencia);
    }

    private static CajaIntencionCanonica? ParseIntent(JsonElement root)
    {
        if (!Fields(root, "tipo", "version", "argumentos") || !Text(root, "tipo", out var type) ||
            !Int(root, "version", out var version) || version != 1 || !root.TryGetProperty("argumentos", out var a)) return null;
        if (type == "AbrirTurno" && Fields(a, "fondoInicial", "confirmado") &&
            Decimal(a, "fondoInicial", out var fund) && Bool(a, "confirmado", out var confirmed))
            return CajaOperacionCanonica.Abrir(fund, confirmed);
        if (type == "CerrarTurno" && Fields(a, "turnoId", "efectivoContado", "motivo", "confirmado") &&
            Int(a, "turnoId", out var closeShift) && Decimal(a, "efectivoContado", out var counted) &&
            OptionalText(a, "motivo", out var reason) && Bool(a, "confirmado", out var closeConfirmed))
            return CajaOperacionCanonica.Cerrar(closeShift, counted, reason, closeConfirmed);
        if (type == "CobrarCuenta" && Fields(a, "pedidoId", "turnoId", "totalVisto", "huellaLineas", "metodo", "efectivoRecibido", "referencia", "verificacionExterna", "referenciaRepetida") &&
            Int(a, "pedidoId", out var order) && Int(a, "turnoId", out var shift) && Decimal(a, "totalVisto", out var total) &&
            Text(a, "huellaLineas", out var hash) && Text(a, "metodo", out var method) && OptionalDecimal(a, "efectivoRecibido", out var cash) &&
            OptionalText(a, "referencia", out var reference) && Bool(a, "verificacionExterna", out var verified) && Bool(a, "referenciaRepetida", out var repeated))
            return CajaOperacionCanonica.Cobrar(order, shift, total, hash, method, cash, reference, verified, repeated);
        return null;
    }

    private static bool Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
        return seen.Count == names.Length;
    }
    private static bool Text(JsonElement a, string name, out string? result)
    {
        result = null;
        if (!a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        result = v.GetString();
        return result is not null;
    }
    private static bool OptionalText(JsonElement a, string name, out string? result)
    {
        result = null;
        return a.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.Null || Text(a, name, out result));
    }
    private static bool Int(JsonElement a, string name, out int result)
    {
        result = 0;
        return a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out result);
    }
    private static bool Decimal(JsonElement a, string name, out decimal result)
    {
        result = 0;
        return a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out result);
    }
    private static bool OptionalDecimal(JsonElement a, string name, out decimal? result)
    {
        result = null;
        if (!a.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Null) return true;
        if (!Decimal(a, name, out var number)) return false;
        result = number;
        return true;
    }
    private static bool Bool(JsonElement a, string name, out bool result)
    {
        result = false;
        if (!a.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        result = v.GetBoolean();
        return true;
    }
    private async Task<(JsonDocument? Document, int Error)> ReadJsonAsync(int limit, CancellationToken ct)
    {
        if (Request.ContentLength > limit) return (null, 413);
        var buffer = new byte[limit + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > limit) return (null, 413);
        try { return (JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 }), 0); }
        catch (JsonException) { return (null, 400); }
    }
    private int? Actor() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private Guid? Session() => Guid.TryParse(User.FindFirstValue("sid"), out var id) ? id : null;
    private IActionResult Reply(CajaCancelacionResult result)
    {
        if (result.ResultadoJson is not null)
            return new ContentResult { Content = result.ResultadoJson, ContentType = "application/json", StatusCode = result.Status };
        if (result.Status >= 400) return Error(result.Status, result.Code);
        return StatusCode(result.Status, new { estado = result.Estado, codigo = result.Code });
    }
    private ObjectResult Error(int status, string code) => new(ApiProblemDetails.Create(HttpContext, status, code,
        "No se pudo procesar la operación", "Revise la operación y su estado antes de continuar."))
    { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
