using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(ApiProblemDetails.Create(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "AUTH_CREDENCIALES_INVALIDAS",
                "Credenciales inválidas",
                "No fue posible autenticar las credenciales proporcionadas."))
            : Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request, cancellationToken);
        return result.Outcome switch
        {
            AuthOperationOutcome.Success => Ok(result.Tokens),
            AuthOperationOutcome.SessionExpired => ProblemResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_SESION_EXPIRADA",
                "Sesión expirada",
                "La sesión ya no está activa."),
            _ => ProblemResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_REFRESH_INVALIDO",
                "Refresh token inválido",
                "No fue posible renovar la sesión.")
        };
    }

    [Authorize]
    [HttpPost("activity")]
    [ProducesResponseType<ActivityResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activity(CancellationToken cancellationToken)
    {
        if (!TryGetSessionId(out var sessionId))
        {
            return SessionExpiredProblem();
        }

        var result = await authService.RecordActivityAsync(sessionId, cancellationToken);
        return result.Outcome == AuthOperationOutcome.Success
            ? Ok(result.Activity)
            : SessionExpiredProblem();
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!TryGetSessionId(out var sessionId))
        {
            return SessionExpiredProblem();
        }

        await authService.LogoutAsync(sessionId, cancellationToken);
        return NoContent();
    }

    private bool TryGetSessionId(out Guid sessionId) =>
        Guid.TryParse(User.FindFirstValue("sid"), out sessionId);

    private IActionResult SessionExpiredProblem() => ProblemResponse(
        StatusCodes.Status401Unauthorized,
        "AUTH_SESION_EXPIRADA",
        "Sesión expirada",
        "La sesión ya no está activa.");

    private ObjectResult ProblemResponse(int status, string code, string title, string detail) =>
        new(ApiProblemDetails.Create(HttpContext, status, code, title, detail))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
