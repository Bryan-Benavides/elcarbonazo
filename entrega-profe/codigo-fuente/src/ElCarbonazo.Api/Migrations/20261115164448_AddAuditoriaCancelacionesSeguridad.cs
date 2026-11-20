using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditoriaCancelacionesSeguridad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_movimiento_origen",
                table: "movimiento_inventario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_movimiento_tipo_delta",
                table: "movimiento_inventario");

            migrationBuilder.DropCheckConstraint(
                name: "CK_comanda_estado",
                table: "comanda");

            migrationBuilder.AddColumn<long>(
                name: "id_anulacion",
                table: "movimiento_inventario",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "cancelado",
                table: "detalle_pedido",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "revision_detalles",
                table: "comanda",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "anulacion_pedido",
                columns: table => new
                {
                    id_anulacion = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_pedido = table.Column<int>(type: "integer", nullable: false),
                    id_comanda = table.Column<int>(type: "integer", nullable: false),
                    id_detalle = table.Column<int>(type: "integer", nullable: true),
                    cantidad_anulada = table.Column<int>(type: "integer", nullable: false),
                    monto_revertido = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    estado_comanda_previo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_usuario_solicita = table.Column<int>(type: "integer", nullable: false),
                    id_usuario_autoriza = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    inventario_revertido = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anulacion_pedido", x => x.id_anulacion);
                    table.CheckConstraint("CK_anulacion_cantidad_monto", "cantidad_anulada > 0 AND monto_revertido >= 0 AND monto_revertido <> 'NaN'::numeric");
                    table.CheckConstraint("CK_anulacion_estado", "estado_comanda_previo IN ('Abierta', 'En Preparación', 'Lista')");
                    table.CheckConstraint("CK_anulacion_motivo", "char_length(motivo) BETWEEN 5 AND 250 AND motivo = btrim(motivo) AND motivo = normalize(motivo, NFC) AND motivo !~ '[[:cntrl:]]'");
                    table.ForeignKey(
                        name: "FK_anulacion_pedido_comanda_id_comanda",
                        column: x => x.id_comanda,
                        principalTable: "comanda",
                        principalColumn: "id_comanda",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_anulacion_pedido_detalle_pedido_id_detalle",
                        column: x => x.id_detalle,
                        principalTable: "detalle_pedido",
                        principalColumn: "id_detalle",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_anulacion_pedido_pedido_id_pedido",
                        column: x => x.id_pedido,
                        principalTable: "pedido",
                        principalColumn: "id_pedido",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_anulacion_pedido_usuario_id_usuario_autoriza",
                        column: x => x.id_usuario_autoriza,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_anulacion_pedido_usuario_id_usuario_solicita",
                        column: x => x.id_usuario_solicita,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consumo_detalle",
                columns: table => new
                {
                    id_detalle = table.Column<int>(type: "integer", nullable: false),
                    id_ingrediente = table.Column<int>(type: "integer", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumo_detalle", x => new { x.id_detalle, x.id_ingrediente });
                    table.CheckConstraint("CK_consumo_detalle_cantidad", "cantidad > 0 AND cantidad <> 'NaN'::numeric");
                    table.ForeignKey(
                        name: "FK_consumo_detalle_detalle_pedido_id_detalle",
                        column: x => x.id_detalle,
                        principalTable: "detalle_pedido",
                        principalColumn: "id_detalle",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consumo_detalle_ingrediente_id_ingrediente",
                        column: x => x.id_ingrediente,
                        principalTable: "ingrediente",
                        principalColumn: "id_ingrediente",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "limite_autorizacion",
                columns: table => new
                {
                    id_usuario_solicita = table.Column<int>(type: "integer", nullable: false),
                    hash_nombre = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    intentos_fallidos = table.Column<int>(type: "integer", nullable: false),
                    bloqueo_hasta_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_actividad_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_limite_autorizacion", x => new { x.id_usuario_solicita, x.hash_nombre });
                    table.CheckConstraint("CK_limite_autorizacion_hash", "hash_nombre ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_limite_autorizacion_intentos", "intentos_fallidos BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_limite_autorizacion_usuario_id_usuario_solicita",
                        column: x => x.id_usuario_solicita,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operacion_anulacion",
                columns: table => new
                {
                    id_operacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario_propietario = table.Column<int>(type: "integer", nullable: false),
                    hash_intencion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_creacion_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_terminal_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_resultado = table.Column<short>(type: "smallint", nullable: true),
                    codigo_resultado = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    resultado_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operacion_anulacion", x => x.id_operacion);
                    table.CheckConstraint("CK_operacion_anulacion_estado", "estado IN ('Autorizada', 'Completada', 'Rechazada', 'Cancelada')");
                    table.CheckConstraint("CK_operacion_anulacion_hash", "hash_intencion IS NULL OR hash_intencion ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_operacion_anulacion_resultado", "(estado = 'Autorizada' AND hash_intencion IS NOT NULL AND fecha_terminal_utc IS NULL AND status_resultado IS NULL AND codigo_resultado IS NULL AND resultado_json IS NULL) OR (estado IN ('Completada', 'Rechazada', 'Cancelada') AND fecha_terminal_utc IS NOT NULL AND fecha_terminal_utc >= fecha_creacion_utc AND status_resultado IS NOT NULL AND ((estado IN ('Completada', 'Cancelada') AND status_resultado BETWEEN 200 AND 299) OR (estado = 'Rechazada' AND status_resultado BETWEEN 400 AND 499)) AND codigo_resultado IS NOT NULL AND char_length(btrim(codigo_resultado)) > 0 AND resultado_json IS NOT NULL AND jsonb_typeof(resultado_json) = 'object' AND (hash_intencion IS NOT NULL OR estado = 'Cancelada'))");
                    table.ForeignKey(
                        name: "FK_operacion_anulacion_usuario_id_usuario_propietario",
                        column: x => x.id_usuario_propietario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alerta_anulacion",
                columns: table => new
                {
                    id_anulacion = table.Column<long>(type: "bigint", nullable: false),
                    fecha_acuse_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    id_usuario_acuse = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerta_anulacion", x => x.id_anulacion);
                    table.CheckConstraint("CK_alerta_acuse", "(fecha_acuse_utc IS NULL AND id_usuario_acuse IS NULL) OR (fecha_acuse_utc IS NOT NULL AND id_usuario_acuse IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_alerta_anulacion_anulacion_pedido_id_anulacion",
                        column: x => x.id_anulacion,
                        principalTable: "anulacion_pedido",
                        principalColumn: "id_anulacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerta_anulacion_usuario_id_usuario_acuse",
                        column: x => x.id_usuario_acuse,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "autorizacion_anulacion",
                columns: table => new
                {
                    id_autorizacion = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_operacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_sesion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario_autoriza = table.Column<int>(type: "integer", nullable: false),
                    hash_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha_emision_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_expiracion_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_consumo_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_autorizacion_anulacion", x => x.id_autorizacion);
                    table.CheckConstraint("CK_autorizacion_fecha", "fecha_expiracion_utc > fecha_emision_utc AND fecha_expiracion_utc <= fecha_emision_utc + interval '2 minutes' AND (fecha_consumo_utc IS NULL OR fecha_consumo_utc >= fecha_emision_utc)");
                    table.CheckConstraint("CK_autorizacion_hash", "hash_token ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_autorizacion_anulacion_operacion_anulacion_id_operacion",
                        column: x => x.id_operacion,
                        principalTable: "operacion_anulacion",
                        principalColumn: "id_operacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_autorizacion_anulacion_sesion_usuario_id_sesion",
                        column: x => x.id_sesion,
                        principalTable: "sesion_usuario",
                        principalColumn: "id_sesion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_autorizacion_anulacion_usuario_id_usuario_autoriza",
                        column: x => x.id_usuario_autoriza,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movimiento_inventario_id_anulacion_id_ingrediente",
                table: "movimiento_inventario",
                columns: new[] { "id_anulacion", "id_ingrediente" },
                unique: true,
                filter: "id_anulacion IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_movimiento_origen",
                table: "movimiento_inventario",
                sql: "(tipo_movimiento = 'ConsumoPedido' AND id_pedido IS NOT NULL AND id_comanda IS NOT NULL AND id_anulacion IS NULL) OR (tipo_movimiento = 'ReversionCancelacion' AND id_pedido IS NOT NULL AND id_comanda IS NOT NULL AND id_anulacion IS NOT NULL AND motivo IS NOT NULL AND char_length(btrim(motivo)) BETWEEN 1 AND 250) OR (tipo_movimiento NOT IN ('ConsumoPedido', 'ReversionCancelacion') AND id_pedido IS NULL AND id_comanda IS NULL AND id_anulacion IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_movimiento_tipo_delta",
                table: "movimiento_inventario",
                sql: "(tipo_movimiento IN ('EntradaCompra', 'AjustePositivo', 'ReversionCancelacion') AND cantidad_delta > 0) OR (tipo_movimiento IN ('AjusteNegativo', 'ConsumoPedido') AND cantidad_delta < 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_comanda_estado",
                table: "comanda",
                sql: "estado IN ('Abierta', 'En Preparación', 'Lista', 'Cancelada')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_comanda_revision_detalles",
                table: "comanda",
                sql: "revision_detalles > 0");

            migrationBuilder.CreateIndex(
                name: "IX_alerta_anulacion_id_usuario_acuse",
                table: "alerta_anulacion",
                column: "id_usuario_acuse");

            migrationBuilder.CreateIndex(
                name: "IX_anulacion_pedido_id_comanda",
                table: "anulacion_pedido",
                column: "id_comanda");

            migrationBuilder.CreateIndex(
                name: "IX_anulacion_pedido_id_detalle",
                table: "anulacion_pedido",
                column: "id_detalle");

            migrationBuilder.CreateIndex(
                name: "IX_anulacion_pedido_id_pedido",
                table: "anulacion_pedido",
                column: "id_pedido");

            migrationBuilder.CreateIndex(
                name: "IX_anulacion_pedido_id_usuario_autoriza",
                table: "anulacion_pedido",
                column: "id_usuario_autoriza");

            migrationBuilder.CreateIndex(
                name: "IX_anulacion_pedido_id_usuario_solicita",
                table: "anulacion_pedido",
                column: "id_usuario_solicita");

            migrationBuilder.CreateIndex(
                name: "IX_autorizacion_anulacion_hash_token",
                table: "autorizacion_anulacion",
                column: "hash_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_autorizacion_anulacion_id_operacion",
                table: "autorizacion_anulacion",
                column: "id_operacion");

            migrationBuilder.CreateIndex(
                name: "IX_autorizacion_anulacion_id_sesion",
                table: "autorizacion_anulacion",
                column: "id_sesion");

            migrationBuilder.CreateIndex(
                name: "IX_autorizacion_anulacion_id_usuario_autoriza",
                table: "autorizacion_anulacion",
                column: "id_usuario_autoriza");

            migrationBuilder.CreateIndex(
                name: "IX_consumo_detalle_id_ingrediente",
                table: "consumo_detalle",
                column: "id_ingrediente");

            migrationBuilder.CreateIndex(
                name: "IX_operacion_anulacion_id_usuario_propietario",
                table: "operacion_anulacion",
                column: "id_usuario_propietario");

            migrationBuilder.AddForeignKey(
                name: "FK_movimiento_inventario_anulacion_pedido_id_anulacion",
                table: "movimiento_inventario",
                column: "id_anulacion",
                principalTable: "anulacion_pedido",
                principalColumn: "id_anulacion",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE comanda ADD CONSTRAINT uq_006_comanda_pedido UNIQUE (id_comanda, id_pedido);
                ALTER TABLE detalle_pedido ADD CONSTRAINT uq_006_detalle_comanda UNIQUE (id_detalle, id_comanda);
                ALTER TABLE anulacion_pedido ADD CONSTRAINT uq_006_anulacion_destino UNIQUE (id_anulacion, id_pedido, id_comanda);
                ALTER TABLE anulacion_pedido ADD CONSTRAINT fk_006_anulacion_comanda_pedido
                    FOREIGN KEY (id_comanda, id_pedido) REFERENCES comanda (id_comanda, id_pedido) ON DELETE RESTRICT;
                ALTER TABLE anulacion_pedido ADD CONSTRAINT fk_006_anulacion_detalle_comanda
                    FOREIGN KEY (id_detalle, id_comanda) REFERENCES detalle_pedido (id_detalle, id_comanda) ON DELETE RESTRICT;
                ALTER TABLE movimiento_inventario ADD CONSTRAINT fk_006_movimiento_anulacion_destino
                    FOREIGN KEY (id_anulacion, id_pedido, id_comanda)
                    REFERENCES anulacion_pedido (id_anulacion, id_pedido, id_comanda) ON DELETE RESTRICT;

                CREATE TABLE estado_hmac_autorizacion (
                    id smallint PRIMARY KEY CHECK (id = 1),
                    provisionado boolean NOT NULL DEFAULT false,
                    huella varchar(64),
                    espera_hasta_utc timestamptz,
                    CONSTRAINT ck_estado_hmac_integridad CHECK (
                        (NOT provisionado AND huella IS NULL AND espera_hasta_utc IS NULL)
                        OR (provisionado AND huella IS NOT NULL AND huella ~ '^[0-9a-f]{64}$')
                    )
                );
                INSERT INTO estado_hmac_autorizacion (id) VALUES (1);

                CREATE FUNCTION ec_006_proteger_operacion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.estado <> 'Autorizada' OR NEW.estado NOT IN ('Completada', 'Rechazada', 'Cancelada') THEN
                        RAISE EXCEPTION 'Transición de operación de anulación inválida' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER tr_006_proteger_operacion BEFORE UPDATE ON operacion_anulacion
                    FOR EACH ROW EXECUTE FUNCTION ec_006_proteger_operacion();

                CREATE FUNCTION ec_006_proteger_consumo_autorizacion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.fecha_consumo_utc IS NOT NULL OR NEW.fecha_consumo_utc IS NULL THEN
                        RAISE EXCEPTION 'Consumo de autorización inválido' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER tr_006_proteger_consumo BEFORE UPDATE ON autorizacion_anulacion
                    FOR EACH ROW EXECUTE FUNCTION ec_006_proteger_consumo_autorizacion();

                CREATE FUNCTION ec_006_proteger_acuse() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.fecha_acuse_utc IS NOT NULL OR NEW.fecha_acuse_utc IS NULL OR NEW.id_usuario_acuse IS NULL THEN
                        RAISE EXCEPTION 'Acuse de anulación inválido' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER tr_006_proteger_acuse BEFORE UPDATE ON alerta_anulacion
                    FOR EACH ROW EXECUTE FUNCTION ec_006_proteger_acuse();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("La reversión automática de cancelaciones está bloqueada para preservar su evidencia.");
        }
    }
}
