using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sesion_usuario",
                columns: table => new
                {
                    id_sesion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<int>(type: "integer", nullable: false),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_actividad = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expiracion_absoluta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_revocacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sesion_usuario", x => x.id_sesion);
                    table.ForeignKey(
                        name: "FK_sesion_usuario_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "token_refresh_sesion",
                columns: table => new
                {
                    id_token = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_sesion = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", maxLength: 64, nullable: false),
                    fecha_emision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_consumo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_refresh_sesion", x => x.id_token);
                    table.ForeignKey(
                        name: "FK_token_refresh_sesion_sesion_usuario_id_sesion",
                        column: x => x.id_sesion,
                        principalTable: "sesion_usuario",
                        principalColumn: "id_sesion",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sesion_usuario_id_usuario",
                table: "sesion_usuario",
                column: "id_usuario",
                unique: true,
                filter: "fecha_revocacion IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_token_refresh_sesion_id_sesion",
                table: "token_refresh_sesion",
                column: "id_sesion",
                unique: true,
                filter: "fecha_consumo IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_token_refresh_sesion_token_hash",
                table: "token_refresh_sesion",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddUserSessions conserva historial de sesión y refresh-token; no admite rollback destructivo. " +
                "Restaure un respaldo verificado para volver al esquema anterior.");
        }
    }
}
