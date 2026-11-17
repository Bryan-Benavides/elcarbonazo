using System.Security.Claims;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.Mesero + "," + UserRoles.EncargadoCaja)]
[Route("api/seguridad")]
public sealed class AnulacionesSeguridadController(AnulacionAutorizacionService service) : ControllerBase
{
    [HttpPost("autorizar-anulacion")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Autorizar(AutorizarAnulacionRequest? request, CancellationToken ct)
    {
        if (request is null) return Error(400, "VALIDATION_ERROR");
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor) ||
            !Guid.TryParse(User.FindFirstValue("sid"), out var session))
            return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await service.AutorizarAsync(request, actor, session, ct);
        return result.Status == 200 ? Ok(result.Data) : Error(result.Status, result.Code);
    }

    private ObjectResult Error(int status, string code) =>
        new(ApiProblemDetails.Create(HttpContext, status, code,
            "No se pudo autorizar la operación", "Revise la sesión y la intención antes de continuar."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
}

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.Mesero + "," + UserRoles.EncargadoCaja)]
[Route("api/pedidos/{idPedido:int}/anulaciones")]
public sealed class AnulacionesController(AnulacionEjecucionService execution) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Ejecutar(int idPedido, EjecutarAnulacionRequest? request, CancellationToken ct)
    {
        if (request is null) return Error(400, "VALIDATION_ERROR");
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor) ||
            !Guid.TryParse(User.FindFirstValue("sid"), out var session))
            return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await execution.EjecutarAsync(idPedido, request, actor, session, ct);
        if (result.Json is not null)
            return new ContentResult { Content = result.Json, ContentType = "application/json", StatusCode = result.Status };
        return result.Status < 400 ? StatusCode(result.Status, new { codigo = result.Code }) : Error(result.Status, result.Code);
    }

    private ObjectResult Error(int status, string code) =>
        new(ApiProblemDetails.Create(HttpContext, status, code,
            "No se pudo ejecutar la anulación", "Revise la sesión, autorización y estado de la cuenta."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
}

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.Mesero + "," + UserRoles.EncargadoCaja)]
[Route("api/anulaciones/operaciones")]
public sealed class AnulacionesOperacionesController(AnulacionRecuperacionService recovery) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Consultar(Guid id, CancellationToken ct) => Resolver(id, false, false, ct);

    [HttpPost("{id:guid}/cancelacion")]
    public Task<IActionResult> Barrera(Guid id, [FromBody] ConfirmarIntervencionRequest? request, CancellationToken ct) =>
        request?.ConfirmaIntervencion == true
            ? Resolver(id, true, request.IntervencionAjena, ct)
            : Task.FromResult<IActionResult>(Error(400, "VALIDATION_ERROR"));

    private async Task<IActionResult> Resolver(Guid id, bool barrier, bool intervention, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor) ||
            !Guid.TryParse(User.FindFirstValue("sid"), out var session))
            return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await recovery.ResolverAsync(id, actor, session, barrier, intervention, ct);
        if (result.Status >= 400) return Error(result.Status, result.Code);
        return Ok(new { estado = result.Estado, codigo = result.Code, resultado = result.ResultadoJson });
    }

    private ObjectResult Error(int status, string code) =>
        new(ApiProblemDetails.Create(HttpContext, status, code,
            "No se pudo recuperar la operación", "Revise la sesión y el resultado de la operación."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
}

public sealed record ConfirmarIntervencionRequest(bool ConfirmaIntervencion, bool IntervencionAjena);

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor)]
[Route("api/auditoria")]
public sealed class AnulacionesAuditoriaController(AnulacionesConsultasService queries) : ControllerBase
{
    [HttpGet("anulaciones")]
    public async Task<IActionResult> Anulaciones([FromQuery] DateTimeOffset? desde, [FromQuery] DateTimeOffset? hasta,
        [FromQuery] string? cursor, [FromQuery] int limite = 25, CancellationToken ct = default)
    {
        if (!TryActorSession(out var actor, out var session)) return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await queries.AnulacionesAsync(desde, hasta, cursor, limite, ct, actor, session);
        return result.Status == 200 ? Ok(result.Page) : Error(result.Status, result.Code);
    }

    [HttpGet("eventos")]
    public async Task<IActionResult> Eventos([FromQuery] DateTimeOffset? desde, [FromQuery] DateTimeOffset? hasta,
        [FromQuery] string? tipo, [FromQuery] string? cursor, [FromQuery] int limite = 25, CancellationToken ct = default)
    {
        if (!TryActorSession(out var actor, out var session)) return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await queries.EventosAsync(desde, hasta, tipo, cursor, limite, ct, actor, session);
        return result.Status == 200 ? Ok(result.Page) : Error(result.Status, result.Code);
    }

    private ObjectResult Error(int status, string code) =>
        new(ApiProblemDetails.Create(HttpContext, status, code,
            "No se pudo consultar la auditoría", "Revise los filtros y permisos de la sesión."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };

    private bool TryActorSession(out int actor, out Guid session)
    {
        actor = 0;
        session = Guid.Empty;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out actor) &&
            Guid.TryParse(User.FindFirstValue("sid"), out session);
    }
}
