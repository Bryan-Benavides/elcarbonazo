using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ElCarbonazo.Api.Security;

public sealed class JwtTokenGenerator(
    IOptions<JwtSettings> jwtOptions,
    TimeProvider timeProvider) : IJwtTokenGenerator
{
    private readonly JwtSettings _settings = jwtOptions.Value;

    public string Generate(Usuario usuario, Guid sessionId)
    {
        var claims = new Claim[]
        {
            new(JwtRegisteredClaimNames.Sub, usuario.IdUsuario.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new("rol", usuario.Rol),
            new("sid", sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(_settings.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: timeProvider.GetUtcNow().UtcDateTime.Add(SessionSecurityPolicy.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
