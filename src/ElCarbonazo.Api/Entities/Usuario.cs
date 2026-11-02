namespace ElCarbonazo.Api.Entities;

public class Usuario
{
    public int IdUsuario { get; set; }
    public string Nombre { get; set; } = null!;
    public string ContrasenaHash { get; set; } = null!;
    public string Rol { get; set; } = null!;
    public string Email { get; set; } = null!;
    public bool Activo { get; set; } = true;
}