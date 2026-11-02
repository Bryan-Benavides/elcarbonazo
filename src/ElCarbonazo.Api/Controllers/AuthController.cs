using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AppDbContext context, JwtSettings jwtSettings) : ControllerBase
{
    private static readonly HashSet<string> RolesValidos = new(StringComparer.Ordinal)
    {
        "Jefa",
        "Encargado de Caja",
        "Mesero",
        "Personal de Cocina",
        "Supervisor"
    };

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre) ||
            string.IsNullOrWhiteSpace(request.Contrasena))
        {
            return Unauthorized();
        }

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Nombre == request.Nombre,
                cancellationToken);

        if (usuario is null ||
            !usuario.Activo ||
            !RolesValidos.Contains(usuario.Rol) ||
            !VerifyPassword(request.Contrasena, usuario.ContrasenaHash))
        {
            return Unauthorized();
        }

        var token = GenerateToken(usuario);
        return Ok(new LoginResponse(token, usuario.IdUsuario, usuario.Nombre, usuario.Rol));
    }

    private static bool VerifyPassword(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private string GenerateToken(Usuario usuario)
    {
        var claims = new Claim[]
        {
            new(JwtRegisteredClaimNames.Sub, usuario.IdUsuario.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new("rol", usuario.Rol),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSettings.Issuer,
            audience: jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
