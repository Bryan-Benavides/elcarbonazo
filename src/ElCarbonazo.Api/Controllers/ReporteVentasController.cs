using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor)]
[Route("api/reportes/ventas")]
public sealed class ReporteVentasController(ReporteVentasService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener([FromQuery] DateTimeOffset? desdeUtc, [FromQuery] DateTimeOffset? hastaUtc,
        CancellationToken ct)
    {
        if (desdeUtc is null || hastaUtc is null) return Error(400, "VALIDATION_ERROR");
        var result = await service.ObtenerAsync(desdeUtc.Value, hastaUtc.Value, ct);
        return result.Status == 200 ? Ok(result.Reporte) : Error(result.Status, result.Code);
    }

    private ObjectResult Error(int status, string code) =>
        new(ApiProblemDetails.Create(HttpContext, status, code,
            "No se pudo consultar el reporte", "Revise el rango de fechas y los permisos de la sesión."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
