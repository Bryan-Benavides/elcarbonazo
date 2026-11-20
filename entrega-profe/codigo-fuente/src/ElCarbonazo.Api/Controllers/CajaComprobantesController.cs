using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja)]
[Route("api/caja/comprobantes")]
public sealed class CajaComprobantesController(CajaComprobantesService comprobantes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? cursor, [FromQuery] int limite = 25, CancellationToken cancellationToken = default)
    {
        var result = await comprobantes.ListarAsync(cursor, limite, cancellationToken);
        return result is not null ? Ok(result) : Error(400, "VALIDATION_ERROR");
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id, CancellationToken cancellationToken)
    {
        var result = await comprobantes.ObtenerAsync(id, cancellationToken);
        return result is not null ? Ok(result) : Error(404, "RECURSO_NO_ENCONTRADO");
    }

    private ObjectResult Error(int status, string code) => new(ApiProblemDetails.Create(HttpContext, status, code,
        status == 400 ? "Solicitud inválida" : "Recurso no encontrado", "Revise la solicitud."))
    { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
