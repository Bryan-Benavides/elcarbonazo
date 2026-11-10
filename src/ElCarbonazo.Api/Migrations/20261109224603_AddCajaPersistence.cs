using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElCarbonazo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCajaPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operacion_caja",
                columns: table => new
                {
                    id_operacion = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario_propietario = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    version_contrato = table.Column<short>(type: "smallint", nullable: true),
                    argumentos = table.Column<string>(type: "jsonb", nullable: true),
                    hash_argumentos = table.Column<string>(type: "character(64)", maxLength: 64, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_creacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_terminal = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_resultado = table.Column<short>(type: "smallint", nullable: true),
                    codigo_resultado = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    resultado_json = table.Column<string>(type: "jsonb", nullable: true),
                    id_usuario_cancelacion = table.Column<int>(type: "integer", nullable: true),
                    fecha_cancelacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operacion_caja", x => x.id_operacion);
                    table.CheckConstraint("CK_operacion_caja_cancelacion", "((estado = 'Cancelada' AND id_usuario_cancelacion IS NOT NULL AND fecha_cancelacion IS NOT NULL) OR (estado <> 'Cancelada' AND id_usuario_cancelacion IS NULL AND fecha_cancelacion IS NULL))");
                    table.CheckConstraint("CK_operacion_caja_estado", "estado IN ('Preparada', 'Completada', 'Rechazada', 'Cancelada')");
                    table.CheckConstraint("CK_operacion_caja_payload", "((tipo IS NULL AND version_contrato IS NULL AND argumentos IS NULL AND hash_argumentos IS NULL AND estado = 'Cancelada') OR (tipo IS NOT NULL AND tipo IN ('AbrirTurno', 'CobrarCuenta', 'CerrarTurno') AND version_contrato IS NOT NULL AND version_contrato = 1 AND argumentos IS NOT NULL AND jsonb_typeof(argumentos) = 'object' AND hash_argumentos IS NOT NULL AND hash_argumentos ~ '^[0-9a-f]{64}$'))");
                    table.CheckConstraint("CK_operacion_caja_resultado", "((estado = 'Preparada' AND fecha_terminal IS NULL AND status_resultado IS NULL AND codigo_resultado IS NULL AND resultado_json IS NULL) OR (estado <> 'Preparada' AND fecha_terminal IS NOT NULL AND status_resultado IS NOT NULL AND status_resultado BETWEEN 200 AND 499 AND codigo_resultado IS NOT NULL AND resultado_json IS NOT NULL AND jsonb_typeof(resultado_json) = 'object'))");
                    table.ForeignKey(
                        name: "FK_operacion_caja_usuario_id_usuario_cancelacion",
                        column: x => x.id_usuario_cancelacion,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operacion_caja_usuario_id_usuario_propietario",
                        column: x => x.id_usuario_propietario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turno_caja",
                columns: table => new
                {
                    id_turno_caja = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_apertura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    id_usuario_apertura = table.Column<int>(type: "integer", nullable: false),
                    fondo_inicial = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    id_operacion_apertura = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    id_usuario_cierre = table.Column<int>(type: "integer", nullable: true),
                    id_operacion_cierre = table.Column<Guid>(type: "uuid", nullable: true),
                    efectivo_esperado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    efectivo_contado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    diferencia = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    total_efectivo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    total_tarjeta = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    total_transferencia = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    cantidad_cuentas_pendientes = table.Column<int>(type: "integer", nullable: true),
                    motivo_diferencia = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turno_caja", x => x.id_turno_caja);
                    table.CheckConstraint("CK_turno_caja_cierre", "((estado = 'Abierto' AND fecha_cierre IS NULL AND id_usuario_cierre IS NULL AND id_operacion_cierre IS NULL AND efectivo_esperado IS NULL AND efectivo_contado IS NULL AND diferencia IS NULL AND total_efectivo IS NULL AND total_tarjeta IS NULL AND total_transferencia IS NULL AND cantidad_cuentas_pendientes IS NULL AND motivo_diferencia IS NULL) OR (estado = 'Cerrado' AND fecha_cierre IS NOT NULL AND id_usuario_cierre IS NOT NULL AND id_operacion_cierre IS NOT NULL AND efectivo_esperado IS NOT NULL AND efectivo_esperado >= 0 AND efectivo_contado IS NOT NULL AND efectivo_contado >= 0 AND diferencia IS NOT NULL AND diferencia = efectivo_contado - efectivo_esperado AND total_efectivo IS NOT NULL AND total_efectivo >= 0 AND total_tarjeta IS NOT NULL AND total_tarjeta >= 0 AND fecha_cierre >= fecha_apertura AND total_transferencia IS NOT NULL AND total_transferencia >= 0 AND cantidad_cuentas_pendientes IS NOT NULL AND cantidad_cuentas_pendientes >= 0 AND ((diferencia = 0 AND motivo_diferencia IS NULL) OR (diferencia <> 0 AND motivo_diferencia IS NOT NULL AND length(btrim(motivo_diferencia)) > 0))))");
                    table.CheckConstraint("CK_turno_caja_estado", "estado IN ('Abierto', 'Cerrado')");
                    table.CheckConstraint("CK_turno_caja_fondo", "fondo_inicial >= 0");
                    table.CheckConstraint("CK_turno_caja_motivo_diferencia", "motivo_diferencia IS NULL OR (char_length(motivo_diferencia) <= 500 AND motivo_diferencia = normalize(motivo_diferencia, NFC) AND motivo_diferencia !~ '[[:cntrl:]]')");
                    table.ForeignKey(
                        name: "FK_turno_caja_operacion_caja_id_operacion_apertura",
                        column: x => x.id_operacion_apertura,
                        principalTable: "operacion_caja",
                        principalColumn: "id_operacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turno_caja_operacion_caja_id_operacion_cierre",
                        column: x => x.id_operacion_cierre,
                        principalTable: "operacion_caja",
                        principalColumn: "id_operacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turno_caja_usuario_id_usuario_apertura",
                        column: x => x.id_usuario_apertura,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turno_caja_usuario_id_usuario_cierre",
                        column: x => x.id_usuario_cierre,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cobro",
                columns: table => new
                {
                    id_cobro = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    numero_comprobante = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_pedido = table.Column<int>(type: "integer", nullable: false),
                    id_turno_caja = table.Column<int>(type: "integer", nullable: false),
                    id_usuario = table.Column<int>(type: "integer", nullable: false),
                    id_operacion = table.Column<Guid>(type: "uuid", nullable: false),
                    metodo_pago = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    impuestos = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    efectivo_recibido = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    vuelto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    referencia_externa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    verificacion_externa_confirmada = table.Column<bool>(type: "boolean", nullable: false),
                    referencia_repetida_confirmada = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cobro", x => x.id_cobro);
                    table.CheckConstraint("CK_cobro_metodo", "((metodo_pago = 'Efectivo' AND efectivo_recibido IS NOT NULL AND vuelto IS NOT NULL AND efectivo_recibido >= total AND vuelto = efectivo_recibido - total AND referencia_externa IS NULL AND NOT verificacion_externa_confirmada AND NOT referencia_repetida_confirmada) OR (metodo_pago IN ('Tarjeta', 'Transferencia') AND efectivo_recibido IS NULL AND vuelto IS NULL AND referencia_externa IS NOT NULL AND length(btrim(referencia_externa)) > 0 AND verificacion_externa_confirmada))");
                    table.CheckConstraint("CK_cobro_montos", "subtotal > 0 AND impuestos = 0 AND total = subtotal AND total > 0");
                    table.CheckConstraint("CK_cobro_numero_comprobante", "numero_comprobante > 0");
                    table.CheckConstraint("CK_cobro_referencia", "referencia_externa IS NULL OR (char_length(referencia_externa) BETWEEN 1 AND 100 AND referencia_externa = normalize(referencia_externa, NFC) AND referencia_externa = btrim(referencia_externa) AND referencia_externa !~ '[[:cntrl:]]')");
                    table.ForeignKey(
                        name: "FK_cobro_operacion_caja_id_operacion",
                        column: x => x.id_operacion,
                        principalTable: "operacion_caja",
                        principalColumn: "id_operacion",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cobro_pedido_id_pedido",
                        column: x => x.id_pedido,
                        principalTable: "pedido",
                        principalColumn: "id_pedido",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cobro_turno_caja_id_turno_caja",
                        column: x => x.id_turno_caja,
                        principalTable: "turno_caja",
                        principalColumn: "id_turno_caja",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cobro_usuario_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuario",
                        principalColumn: "id_usuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comprobante_cobro_linea",
                columns: table => new
                {
                    id_comprobante_cobro_linea = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_cobro = table.Column<long>(type: "bigint", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    id_detalle_origen = table.Column<int>(type: "integer", nullable: false),
                    nombre_producto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cantidad = table.Column<short>(type: "smallint", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprobante_cobro_linea", x => x.id_comprobante_cobro_linea);
                    table.CheckConstraint("CK_comprobante_cobro_linea_cantidad", "cantidad BETWEEN 1 AND 99");
                    table.CheckConstraint("CK_comprobante_cobro_linea_detalle", "id_detalle_origen > 0");
                    table.CheckConstraint("CK_comprobante_cobro_linea_importes", "precio_unitario BETWEEN 0.01 AND 999999.99 AND subtotal = cantidad * precio_unitario");
                    table.CheckConstraint("CK_comprobante_cobro_linea_nombre", "char_length(nombre_producto) BETWEEN 1 AND 120 AND nombre_producto = normalize(nombre_producto, NFC)");
                    table.CheckConstraint("CK_comprobante_cobro_linea_orden", "orden > 0");
                    table.ForeignKey(
                        name: "FK_comprobante_cobro_linea_cobro_id_cobro",
                        column: x => x.id_cobro,
                        principalTable: "cobro",
                        principalColumn: "id_cobro",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION public.guard_turno_caja_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF OLD.estado <> 'Abierto' OR NEW.estado <> 'Cerrado' THEN
                        RAISE EXCEPTION 'Un turno de caja solo puede cerrarse una vez.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_turno_caja_cierre_inmutable';
                    END IF;

                    IF ROW(NEW.id_turno_caja, NEW.fecha_apertura, NEW.id_usuario_apertura,
                           NEW.fondo_inicial, NEW.id_operacion_apertura)
                       IS DISTINCT FROM
                       ROW(OLD.id_turno_caja, OLD.fecha_apertura, OLD.id_usuario_apertura,
                           OLD.fondo_inicial, OLD.id_operacion_apertura) THEN
                        RAISE EXCEPTION 'Los datos de apertura del turno son inmutables.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_turno_caja_apertura_inmutable';
                    END IF;

                    RETURN NEW;
                END
                $function$;

                CREATE TRIGGER TR_turno_caja_solo_cierre
                BEFORE UPDATE ON public.turno_caja
                FOR EACH ROW EXECUTE FUNCTION public.guard_turno_caja_update();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_cobro_id_operacion",
                table: "cobro",
                column: "id_operacion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cobro_id_pedido",
                table: "cobro",
                column: "id_pedido",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cobro_id_usuario",
                table: "cobro",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "IX_cobro_metodo_referencia",
                table: "cobro",
                columns: new[] { "metodo_pago", "referencia_externa" });

            migrationBuilder.CreateIndex(
                name: "IX_cobro_numero_comprobante",
                table: "cobro",
                column: "numero_comprobante",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cobro_turno_fecha_id",
                table: "cobro",
                columns: new[] { "id_turno_caja", "fecha_hora", "id_cobro" });

            migrationBuilder.CreateIndex(
                name: "IX_comprobante_cobro_linea_cobro_detalle",
                table: "comprobante_cobro_linea",
                columns: new[] { "id_cobro", "id_detalle_origen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comprobante_cobro_linea_cobro_orden",
                table: "comprobante_cobro_linea",
                columns: new[] { "id_cobro", "orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operacion_caja_id_usuario_cancelacion",
                table: "operacion_caja",
                column: "id_usuario_cancelacion");

            migrationBuilder.CreateIndex(
                name: "IX_operacion_caja_propietario_estado_fecha",
                table: "operacion_caja",
                columns: new[] { "id_usuario_propietario", "estado", "fecha_creacion" });

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_fecha_apertura",
                table: "turno_caja",
                column: "fecha_apertura");

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_id_usuario_apertura",
                table: "turno_caja",
                column: "id_usuario_apertura");

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_id_usuario_cierre",
                table: "turno_caja",
                column: "id_usuario_cierre");

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_operacion_apertura",
                table: "turno_caja",
                column: "id_operacion_apertura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_operacion_cierre",
                table: "turno_caja",
                column: "id_operacion_cierre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_turno_caja_unico_abierto",
                table: "turno_caja",
                column: "estado",
                unique: true,
                filter: "estado = 'Abierto'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "La migración crea registros de caja y evidencia de cobros; no se permite un rollback que los elimine. " +
                "Use una migración correctiva o restaure un respaldo verificado.");
        }
    }
}
