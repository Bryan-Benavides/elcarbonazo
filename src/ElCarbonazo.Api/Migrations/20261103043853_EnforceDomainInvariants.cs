using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceDomainInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $migration$
                DECLARE
                    invalid_users text;
                    invalid_tables text;
                BEGIN
                    SELECT string_agg(id_usuario::text, ', ' ORDER BY id_usuario)
                    INTO invalid_users
                    FROM usuario
                    WHERE rol NOT IN ('Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor');

                    IF invalid_users IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden imponer roles válidos: revise id_usuario % y corrija los roles antes de reintentar.',
                            invalid_users;
                    END IF;

                    SELECT string_agg(id_mesa::text, ', ' ORDER BY id_mesa)
                    INTO invalid_tables
                    FROM mesa
                    WHERE numero NOT BETWEEN 1 AND 15
                       OR capacidad NOT BETWEEN 1 AND 20
                       OR estado NOT IN ('Libre', 'Ocupada', 'Reservada', 'Agrupada')
                       OR ((estado = 'Agrupada') <> (id_grupo IS NOT NULL));

                    IF invalid_tables IS NOT NULL THEN
                        RAISE EXCEPTION
                            'No se pueden imponer invariantes de mesa: revise id_mesa % y corrija los datos antes de reintentar.',
                            invalid_tables;
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_usuario_rol",
                table: "usuario",
                sql: "rol IN ('Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mesa_capacidad",
                table: "mesa",
                sql: "capacidad BETWEEN 1 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mesa_estado",
                table: "mesa",
                sql: "estado IN ('Libre', 'Ocupada', 'Reservada', 'Agrupada')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mesa_grupo_estado",
                table: "mesa",
                sql: "(estado = 'Agrupada') = (id_grupo IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mesa_numero",
                table: "mesa",
                sql: "numero BETWEEN 1 AND 15");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_usuario_rol",
                table: "usuario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mesa_capacidad",
                table: "mesa");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mesa_estado",
                table: "mesa");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mesa_grupo_estado",
                table: "mesa");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mesa_numero",
                table: "mesa");
        }
    }
}
