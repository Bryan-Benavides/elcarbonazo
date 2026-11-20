using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

public abstract class InventarioControllerBase : ControllerBase
{
    protected int? Actor => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    protected ObjectResult Error(int status, string code)
    {
        var problem = ApiProblemDetails.Create(HttpContext, status, code, "No se pudo completar la solicitud", "Revise la solicitud y el estado actual.");
        if (status == 400) problem.Extensions["errors"] = new Dictionary<string, string[]> { ["solicitud"] = ["Solicitud inválida."] };
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected IActionResult Result(InventarioResult result) => result.Code is null
        ? new ObjectResult(new { id = result.Id }) { StatusCode = result.Status }
        : Error(result.Status, result.Code);

    protected async Task<(T? Value, int Status)> ReadBody<T>(int max, bool requireVersion, CancellationToken ct) where T : class
    {
        if (Request.ContentLength > max) return (null, 413);
        var bytes = new byte[max + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await Request.Body.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > max) return (null, 413);
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                (requireVersion && !document.RootElement.TryGetProperty("versionEsperada", out _))) return (null, 400);
            var value = JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            return value is null ? (null, 400) : (value, 200);
        }
        catch (JsonException) { return (null, 400); }
    }
}

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.PersonalCocina)]
[Route("api/inventario")]
public sealed class InventarioController(InventarioService writes, InventarioConsultasService reads) : InventarioControllerBase
{
    [HttpGet("ingredientes")]
    public async Task<IActionResult> Ingredientes(string? cursor, int limite = 25, string? estadoAlerta = null, string? busqueda = null, CancellationToken ct = default)
    {
        var result = await reads.IngredientesAsync(cursor, limite, estadoAlerta, busqueda, ct);
        return result is null ? Error(400, "VALIDATION_ERROR") : Ok(result);
    }

    [HttpGet("movimientos")]
    public async Task<IActionResult> Movimientos(int? idIngrediente, string? cursor, int limite = 25, CancellationToken ct = default)
    {
        var result = await reads.MovimientosAsync(idIngrediente, cursor, limite, ct);
        return result is null ? Error(400, "VALIDATION_ERROR") : Ok(result);
    }

    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas(string? cursor, int limite = 25, CancellationToken ct = default)
    {
        var result = await reads.AlertasAsync(cursor, limite, ct);
        return result is null ? Error(400, "VALIDATION_ERROR") : Ok(result);
    }

    [HttpPost("ingredientes")]
    [Authorize(Roles = UserRoles.Jefa)]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        if (Actor is not int actor) return Error(401, "AUTH_NO_AUTENTICADO");
        var (body, status) = await ReadBody<IngredienteInput>(16 * 1024, false, ct);
        if (body is null) return Error(status, status == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        return Result(await writes.CrearIngredienteAsync(body, actor, ct));
    }

    [HttpPut("ingredientes/{id:int}")]
    [Authorize(Roles = UserRoles.Jefa)]
    public async Task<IActionResult> Actualizar(int id, CancellationToken ct)
    {
        if (Actor is not int actor) return Error(401, "AUTH_NO_AUTENTICADO");
        var (body, status) = await ReadBody<IngredienteInput>(16 * 1024, false, ct);
        if (body is null) return Error(status, status == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        return Result(await writes.ActualizarIngredienteAsync(id, body, actor, ct));
    }

    [HttpPost("movimientos")]
    [Authorize(Roles = UserRoles.Jefa)]
    public async Task<IActionResult> Registrar(CancellationToken ct)
    {
        if (Actor is not int actor) return Error(401, "AUTH_NO_AUTENTICADO");
        var (body, status) = await ReadBody<MovimientoInput>(16 * 1024, false, ct);
        if (body is null) return Error(status, status == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        return Result(await writes.RegistrarMovimientoAsync(body, actor, ct));
    }
}

[ApiController]
[Authorize(Roles = UserRoles.Jefa)]
[Route("api/recetas")]
public sealed class RecetasController(RecetasService writes, InventarioConsultasService reads) : InventarioControllerBase
{
    [HttpGet("productos")]
    public async Task<IActionResult> Productos(string? cursor, int limite = 25, string? busqueda = null, CancellationToken ct = default)
    {
        var result = await reads.ProductosAsync(cursor, limite, busqueda, ct);
        return result is null ? Error(400, "VALIDATION_ERROR") : Ok(result);
    }

    [HttpGet("producto/{id:int}")]
    public async Task<IActionResult> Receta(int id, CancellationToken ct)
    {
        var (exists, recipe) = await reads.RecetaAsync(id, ct);
        return exists ? Ok(new { receta = recipe }) : Error(404, "HTTP_NO_ENCONTRADO");
    }

    [HttpPut("producto/{id:int}")]
    public async Task<IActionResult> Guardar(int id, CancellationToken ct)
    {
        if (Actor is not int actor) return Error(401, "AUTH_NO_AUTENTICADO");
        var (body, status) = await ReadBody<RecetaInput>(32 * 1024, true, ct);
        if (body is null) return Error(status, status == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_ERROR");
        return Result(await writes.GuardarAsync(id, body, actor, ct));
    }
}
