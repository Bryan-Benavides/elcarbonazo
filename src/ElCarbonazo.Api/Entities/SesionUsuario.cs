namespace ElCarbonazo.Api.Entities;

public sealed class SesionUsuario
{
    public Guid IdSesion { get; set; }
    public int IdUsuario { get; set; }
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset UltimaActividad { get; set; }
    public DateTimeOffset ExpiracionAbsoluta { get; set; }
    public DateTimeOffset? FechaRevocacion { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public ICollection<TokenRefreshSesion> TokensRefresh { get; set; } = [];
}
