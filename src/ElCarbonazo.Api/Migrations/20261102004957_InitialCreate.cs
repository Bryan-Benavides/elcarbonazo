using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mesa",
                columns: table => new
                {
                    id_mesa = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_grupo = table.Column<int>(type: "integer", nullable: true),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    capacidad = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mesa", x => x.id_mesa);
                });

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id_usuario = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contrasena_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    rol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuario", x => x.id_usuario);
                });

            migrationBuilder.InsertData(
                table: "mesa",
                columns: new[] { "id_mesa", "capacidad", "estado", "id_grupo", "numero" },
                values: new object[,]
                {
                    { 1, 4, "Libre", null, 1 },
                    { 2, 4, "Libre", null, 2 },
                    { 3, 4, "Libre", null, 3 },
                    { 4, 4, "Libre", null, 4 },
                    { 5, 4, "Libre", null, 5 },
                    { 6, 4, "Libre", null, 6 },
                    { 7, 4, "Libre", null, 7 },
                    { 8, 4, "Libre", null, 8 },
                    { 9, 4, "Libre", null, 9 },
                    { 10, 4, "Libre", null, 10 },
                    { 11, 4, "Libre", null, 11 },
                    { 12, 4, "Libre", null, 12 },
                    { 13, 4, "Libre", null, 13 },
                    { 14, 4, "Libre", null, 14 },
                    { 15, 4, "Libre", null, 15 }
                });

            migrationBuilder.InsertData(
                table: "usuario",
                columns: new[] { "id_usuario", "activo", "contrasena_hash", "email", "nombre", "rol" },
                values: new object[] { 1, true, "$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBPj/RK.PZvO.S", "admin@elcarbonazo.local", "admin", "Jefa" });

            migrationBuilder.CreateIndex(
                name: "IX_mesa_numero",
                table: "mesa",
                column: "numero",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mesa");

            migrationBuilder.DropTable(
                name: "usuario");
        }
    }
}
