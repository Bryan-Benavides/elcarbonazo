using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja)]
[Route("api/caja/cuentas")]
public sealed class CajaCuentasController(CajaCuentasService cuentas) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? cursor, [FromQuery] int limite = 25, CancellationToken cancellationToken = default)
    {
        var page = await cuentas.ListarAsync(cursor, limite, cancellationToken);
        if (page is not null) return Ok(page);
        return new ObjectResult(ApiProblemDetails.Create(HttpContext, 400, "VALIDATION_ERROR", "Solicitud inválida",
            "El cursor o límite de paginación no es válido."))
        { StatusCode = 400, ContentTypes = { "application/problem+json" } };
    }
}
