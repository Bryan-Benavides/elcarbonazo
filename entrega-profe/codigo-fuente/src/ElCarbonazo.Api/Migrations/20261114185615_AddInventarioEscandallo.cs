using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventarioEscandallo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingrediente",
                columns: table => new
                {
                    id_ingrediente = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nombre_clave = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false, collation: "C"),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stock_actual = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false, defaultValue: 0m),
                    stock_minimo = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false, defaultValue: 0m),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    fecha_creacion_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_actualizacion_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingrediente", x => x.id_ingrediente);
                    table.CheckConstraint("CK_ingrediente_nombre", "char_length(nombre) BETWEEN 1 AND 120 AND char_length(nombre_clave) BETWEEN 1 AND 240");
                    table.CheckConstraint("CK_ingrediente_stock", "stock_actual >= 0 AND stock_minimo >= 0");
                    table.CheckConstraint("CK_ingrediente_unidad", "unidad_medida IN ('lb', 'kg', 'oz', 'g', 'unidad', 'ml', 'lt')");
                    table.CheckConstraint("CK_ingrediente_version", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "receta",
                columns: table => new
                {
                    id_receta = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_producto = table.Column<int>(type: "integer", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notas_preparacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_actualizacion_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receta", x => x.id_receta);
                    table.CheckConstraint("CK_receta_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_receta_producto_id_producto",
                        column: x => x.id_producto,
                        principalTable: "producto",
                        principalColumn: "id_producto",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimiento_inventario",
                columns: table => new
                {
                    id_movimiento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_ingrediente = table.Column<int>(type: "integer", nullable: false),
                    tipo_movimiento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cantidad_delta = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    stock_anterior = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    stock_posterior = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    id_pedido = table.Column<int>(type: "integer", nullable: true),
                    id_comanda = table.Column<int>(type: "integer", nullable: true),
                    id_usuario = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    fecha_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimiento_inventario", x => x.id_movimiento);
                    table.CheckConstraint("CK_movimiento_motivo", "tipo_movimiento NOT IN ('AjustePositivo', 'AjusteNegativo') OR (motivo IS NOT NULL AND char_length(btrim(motivo)) BETWEEN 1 AND 250)");
                    table.CheckConstraint("CK_movimiento_origen", "(tipo_movimiento = 'ConsumoPedido' AND id_pedido IS NOT NULL AND id_comanda IS NOT NULL) OR (tipo_movimiento <> 'ConsumoPedido' AND id_pedido IS NULL AND id_comanda IS NULL)");
                    table.CheckConstraint("CK_movimiento_saldos", "stock_anterior >= 0 AND stock_posterior >= 0 AND stock_posterior = stock_anterior + cantidad_delta");
                    table.CheckConstraint("CK_movimiento_tipo_delta", "(tipo_movimiento IN ('EntradaCompra', 'AjustePositivo') AND cantidad_delta > 0) OR (tipo_movimiento IN ('AjusteNegativo', 'ConsumoPedido') AND cantidad_delta < 0)");
                    table.ForeignKey(
                        name: "FK_movimiento_inventario_comanda_id_comanda",
                        column: x => x.id_comanda,
                        principalTable: "comanda",
                        principalColumn: "id_comanda",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimiento_inventario_ingrediente_id_ingrediente",
                        column: x => x.id_ingrediente,
                        principalTable: "ingrediente",
                        principalColumn: "id_ingrediente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimiento_inventario_pedido_id_pedido",
                        column: x => x.id_pedido,
                        principalTable: "pedido",
                        principalColumn: "id_pedido",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_movimiento_inventario_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_receta",
                columns: table => new
                {
                    id_detalle_receta = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_receta = table.Column<int>(type: "integer", nullable: false),
                    id_ingrediente = table.Column<int>(type: "integer", nullable: false),
                    cantidad_requerida = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_receta", x => x.id_detalle_receta);
                    table.CheckConstraint("CK_detalle_receta_cantidad", "cantidad_requerida > 0");
                    table.ForeignKey(
                        name: "FK_detalle_receta_ingrediente_id_ingrediente",
                        column: x => x.id_ingrediente,
                        principalTable: "ingrediente",
                        principalColumn: "id_ingrediente",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_receta_receta_id_receta",
                        column: x => x.id_receta,
                        principalTable: "receta",
                        principalColumn: "id_receta",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detalle_receta_id_ingrediente",
                table: "detalle_receta",
                column: "id_ingrediente");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_receta_id_receta_id_ingrediente",
                table: "detalle_receta",
                columns: new[] { "id_receta", "id_ingrediente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingrediente_nombre_clave",
                table: "ingrediente",
                column: "nombre_clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_movimiento_inventario_id_comanda_id_ingrediente",
                table: "movimiento_inventario",
                columns: new[] { "id_comanda", "id_ingrediente" },
                unique: true,
                filter: "tipo_movimiento = 'ConsumoPedido'");

            migrationBuilder.CreateIndex(
                name: "IX_movimiento_inventario_id_ingrediente_id_movimiento",
                table: "movimiento_inventario",
                columns: new[] { "id_ingrediente", "id_movimiento" });

            migrationBuilder.CreateIndex(
                name: "IX_movimiento_inventario_id_pedido",
                table: "movimiento_inventario",
                column: "id_pedido");

            migrationBuilder.CreateIndex(
                name: "IX_movimiento_inventario_id_usuario",
                table: "movimiento_inventario",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "IX_receta_id_producto",
                table: "receta",
                column: "id_producto",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("La reversión automática de inventario está bloqueada para preservar su historial.");
        }
    }
}
