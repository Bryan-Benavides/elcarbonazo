namespace ElCarbonazo.Api.Entities;

public sealed class TokenRefreshSesion
{
    public long IdToken { get; set; }
    public Guid IdSesion { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset FechaEmision { get; set; }
    public DateTimeOffset? FechaConsumo { get; set; }

    public SesionUsuario Sesion { get; set; } = null!;
}
