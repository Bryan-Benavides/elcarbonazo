using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReservaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reservacion",
                columns: table => new
                {
                    id_reservacion = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_mesa = table.Column<int>(type: "integer", nullable: false),
                    nombre_referencia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cantidad_personas = table.Column<short>(type: "smallint", nullable: false),
                    inicio_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    creada_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelada_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservacion", x => x.id_reservacion);
                    table.CheckConstraint("CK_reservacion_estado", "(estado = 'Activa' AND cancelada_utc IS NULL) OR (estado = 'Cancelada' AND cancelada_utc IS NOT NULL AND cancelada_utc >= creada_utc)");
                    table.CheckConstraint("CK_reservacion_intervalo", "inicio_utc < fin_utc AND fin_utc <= inicio_utc + interval '8 hours'");
                    table.CheckConstraint("CK_reservacion_personas", "cantidad_personas BETWEEN 1 AND 20");
                    table.CheckConstraint("CK_reservacion_referencia", "char_length(nombre_referencia) BETWEEN 1 AND 60 AND nombre_referencia = btrim(nombre_referencia) AND nombre_referencia = normalize(nombre_referencia, NFC) AND nombre_referencia !~ '[[:cntrl:]]'");
                    table.ForeignKey(
                        name: "FK_reservacion_mesa_id_mesa",
                        column: x => x.id_mesa,
                        principalTable: "mesa",
                        principalColumn: "id_mesa",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservacion_id_mesa_estado_inicio_utc",
                table: "reservacion",
                columns: new[] { "id_mesa", "estado", "inicio_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("La migración de reservaciones conserva historial; no se permite eliminarlo.");
        }
    }
}
