using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class SecureAdminBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.usuario
                SET activo = false,
                    contrasena_hash = '!NEUTRALIZED-LEGACY-ADMIN!',
                    intentos_fallidos = 0,
                    bloqueo_hasta = NULL
                WHERE id_usuario = 1
                  AND nombre = 'admin'
                  AND email = 'admin@elcarbonazo.local'
                  AND rol = 'Jefa'
                  AND activo = true
                  AND contrasena_hash = '$2a$12$ldnQEKzFtA3tYTUTCuXBTOWQPHOHLphZT6zIMMFAusyK0dGgdH9GG';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "SecureAdminBootstrap no restaura credenciales conocidas. " +
                "Un rollback no puede reactivar el seed histórico inseguro.");
        }
    }
}
