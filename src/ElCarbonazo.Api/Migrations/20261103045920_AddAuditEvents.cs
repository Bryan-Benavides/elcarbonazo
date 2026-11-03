using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evento_auditoria",
                columns: table => new
                {
                    id_evento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_usuario = table.Column<int>(type: "integer", nullable: true),
                    id_sesion = table.Column<Guid>(type: "uuid", nullable: true),
                    direccion_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    id_instalacion = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_entidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    id_entidad = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detalles = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evento_auditoria", x => x.id_evento);
                    table.CheckConstraint("CK_evento_auditoria_resultado", "resultado IN ('Exitoso', 'Rechazado', 'Error')");
                    table.ForeignKey(
                        name: "FK_evento_auditoria_sesion_usuario_id_sesion",
                        column: x => x.id_sesion,
                        principalTable: "sesion_usuario",
                        principalColumn: "id_sesion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evento_auditoria_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evento_auditoria_fecha_hora",
                table: "evento_auditoria",
                column: "fecha_hora");

            migrationBuilder.CreateIndex(
                name: "IX_evento_auditoria_id_sesion",
                table: "evento_auditoria",
                column: "id_sesion");

            migrationBuilder.CreateIndex(
                name: "IX_evento_auditoria_id_usuario_fecha_hora",
                table: "evento_auditoria",
                columns: new[] { "id_usuario", "fecha_hora" });

            migrationBuilder.CreateIndex(
                name: "IX_evento_auditoria_tipo_entidad_id_entidad_fecha_hora",
                table: "evento_auditoria",
                columns: new[] { "tipo_entidad", "id_entidad", "fecha_hora" });

            migrationBuilder.CreateIndex(
                name: "IX_evento_auditoria_tipo_evento_fecha_hora",
                table: "evento_auditoria",
                columns: new[] { "tipo_evento", "fecha_hora" });

            migrationBuilder.Sql("""
                CREATE FUNCTION public.reject_evento_auditoria_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'evento_auditoria es append-only; agregue un evento correctivo.';
                END
                $function$;

                CREATE TRIGGER TR_evento_auditoria_append_only
                BEFORE UPDATE OR DELETE ON public.evento_auditoria
                FOR EACH ROW EXECUTE FUNCTION public.reject_evento_auditoria_mutation();

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddAuditEvents contiene historial de seguridad y operación; no admite rollback destructivo. " +
                "Restaure un respaldo verificado para volver al esquema anterior.");
        }
    }
}
