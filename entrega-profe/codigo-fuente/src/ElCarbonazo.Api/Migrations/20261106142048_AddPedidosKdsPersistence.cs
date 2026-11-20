using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPedidosKdsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comanda",
                columns: table => new
                {
                    id_comanda = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_pedido = table.Column<int>(type: "integer", nullable: false),
                    id_usuario = table.Column<int>(type: "integer", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comanda", x => x.id_comanda);
                    table.CheckConstraint("CK_comanda_estado", "estado IN ('Abierta', 'En Preparación', 'Lista')");
                    table.ForeignKey(
                        name: "FK_comanda_pedido_id_pedido",
                        column: x => x.id_pedido,
                        principalTable: "pedido",
                        principalColumn: "id_pedido",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comanda_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "producto",
                columns: table => new
                {
                    id_producto = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    precio = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    disponible = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto", x => x.id_producto);
                    table.CheckConstraint("CK_producto_categoria", "categoria IN ('Comida', 'Bebida', 'Licor')");
                    table.CheckConstraint("CK_producto_nombre", "char_length(nombre) BETWEEN 1 AND 120 AND nombre = normalize(nombre, NFC)");
                    table.CheckConstraint("CK_producto_precio", "precio BETWEEN 0.01 AND 999999.99");
                    table.CheckConstraint("CK_producto_version", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "envio_pedido",
                columns: table => new
                {
                    envio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<int>(type: "integer", nullable: false),
                    hash_solicitud = table.Column<string>(type: "character(64)", maxLength: 64, nullable: false),
                    id_comanda = table.Column<int>(type: "integer", nullable: false),
                    status_code = table.Column<short>(type: "smallint", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envio_pedido", x => x.envio_id);
                    table.CheckConstraint("CK_envio_pedido_hash_solicitud", "hash_solicitud ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_envio_pedido_response_json", "jsonb_typeof(response_json) = 'object'");
                    table.CheckConstraint("CK_envio_pedido_status_code", "status_code = 201");
                    table.ForeignKey(
                        name: "FK_envio_pedido_comanda_id_comanda",
                        column: x => x.id_comanda,
                        principalTable: "comanda",
                        principalColumn: "id_comanda",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_envio_pedido_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detalle_pedido",
                columns: table => new
                {
                    id_detalle = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_comanda = table.Column<int>(type: "integer", nullable: false),
                    id_producto = table.Column<int>(type: "integer", nullable: false),
                    nombre_producto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cantidad = table.Column<short>(type: "smallint", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    notas = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detalle_pedido", x => x.id_detalle);
                    table.CheckConstraint("CK_detalle_pedido_cantidad", "cantidad BETWEEN 1 AND 99");
                    table.CheckConstraint("CK_detalle_pedido_nombre_producto", "char_length(nombre_producto) BETWEEN 1 AND 120 AND nombre_producto = normalize(nombre_producto, NFC)");
                    table.CheckConstraint("CK_detalle_pedido_notas", "char_length(notas) <= 150 AND notas = normalize(notas, NFC)");
                    table.CheckConstraint("CK_detalle_pedido_precio_unitario", "precio_unitario BETWEEN 0.01 AND 999999.99");
                    table.CheckConstraint("CK_detalle_pedido_subtotal", "subtotal >= 0 AND subtotal = cantidad * precio_unitario");
                    table.ForeignKey(
                        name: "FK_detalle_pedido_comanda_id_comanda",
                        column: x => x.id_comanda,
                        principalTable: "comanda",
                        principalColumn: "id_comanda",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detalle_pedido_producto_id_producto",
                        column: x => x.id_producto,
                        principalTable: "producto",
                        principalColumn: "id_producto",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION public.bump_producto_version()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF ROW(NEW.nombre, NEW.categoria, NEW.precio, NEW.disponible)
                       IS DISTINCT FROM ROW(OLD.nombre, OLD.categoria, OLD.precio, OLD.disponible) THEN
                        NEW.version = OLD.version + 1;
                    ELSE
                        NEW.version = OLD.version;
                    END IF;
                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER TR_producto_increment_version
                BEFORE UPDATE ON public.producto
                FOR EACH ROW EXECUTE FUNCTION public.bump_producto_version();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_comanda_estado_fecha_hora_id_comanda",
                table: "comanda",
                columns: new[] { "estado", "fecha_hora", "id_comanda" });

            migrationBuilder.CreateIndex(
                name: "IX_comanda_id_pedido_id_comanda",
                table: "comanda",
                columns: new[] { "id_pedido", "id_comanda" });

            migrationBuilder.CreateIndex(
                name: "IX_comanda_id_usuario",
                table: "comanda",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "IX_detalle_pedido_id_comanda_id_detalle",
                table: "detalle_pedido",
                columns: new[] { "id_comanda", "id_detalle" });

            migrationBuilder.CreateIndex(
                name: "IX_detalle_pedido_id_producto",
                table: "detalle_pedido",
                column: "id_producto");

            migrationBuilder.CreateIndex(
                name: "IX_envio_pedido_id_comanda",
                table: "envio_pedido",
                column: "id_comanda",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_envio_pedido_id_usuario",
                table: "envio_pedido",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "IX_producto_disponible_categoria_nombre_id_producto",
                table: "producto",
                columns: new[] { "disponible", "categoria", "nombre", "id_producto" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "La migración crea historial de pedidos y comandas; no se permite un rollback que lo elimine. " +
                "Use una migración correctiva o restaure un respaldo verificado.");
        }
    }
}
