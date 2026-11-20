-- Run only as the migration/database administrator after creating the roles.
-- The bootstrap login is temporary and must not be used by the long-running API.
GRANT CONNECT ON DATABASE elcarbonazo TO elcarbonazo_runtime, elcarbonazo_bootstrap;
GRANT USAGE ON SCHEMA public TO elcarbonazo_runtime, elcarbonazo_bootstrap;

GRANT SELECT ON public.usuario TO elcarbonazo_runtime;
GRANT UPDATE (contrasena_hash, intentos_fallidos, bloqueo_hasta)
    ON public.usuario TO elcarbonazo_runtime;
GRANT SELECT, UPDATE (estado, id_grupo, permite_pedido_tras_anulacion) ON public.mesa TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE ON public.grupo_mesa TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.grupo_mesa_miembro TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE (subtotal, impuestos, total, estado) ON public.pedido TO elcarbonazo_runtime;
-- PostgreSQL row locks require UPDATE on at least one column. The version trigger
-- prevents direct version changes and runtime cannot update product data columns.
GRANT SELECT, UPDATE (version) ON public.producto TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.comanda TO elcarbonazo_runtime;
GRANT UPDATE (estado) ON public.comanda TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.detalle_pedido TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.envio_pedido TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE ON public.sesion_usuario TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.token_refresh_sesion TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.evento_auditoria TO elcarbonazo_runtime;

-- Cash persistence: durable operation arguments are immutable after preparation;
-- runtime may only transition status/result/cancellation metadata.
GRANT SELECT, INSERT ON public.operacion_caja TO elcarbonazo_runtime;
GRANT UPDATE (estado, fecha_terminal, status_resultado, codigo_resultado, resultado_json,
              id_usuario_cancelacion, fecha_cancelacion)
    ON public.operacion_caja TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.turno_caja TO elcarbonazo_runtime;
GRANT UPDATE (estado, fecha_cierre, id_usuario_cierre, id_operacion_cierre,
              efectivo_esperado, efectivo_contado, diferencia, total_efectivo,
              total_tarjeta, total_transferencia, cantidad_cuentas_pendientes,
              motivo_diferencia)
    ON public.turno_caja TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.cobro, public.comprobante_cobro_linea TO elcarbonazo_runtime;

-- Inventory ledger is append-only; recipe lines are replaceable current configuration.
GRANT SELECT, INSERT ON public.ingrediente, public.receta TO elcarbonazo_runtime;
GRANT UPDATE (nombre, nombre_clave, unidad_medida, stock_actual, stock_minimo, activo, fecha_actualizacion_utc, version)
    ON public.ingrediente TO elcarbonazo_runtime;
GRANT UPDATE (activa, notas_preparacion, fecha_actualizacion_utc, version) ON public.receta TO elcarbonazo_runtime;
GRANT SELECT, INSERT, DELETE ON public.detalle_receta TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.movimiento_inventario TO elcarbonazo_runtime;

-- 006: immutable evidence, limited state transitions and expiring authorization counters.
GRANT SELECT, INSERT ON public.consumo_detalle, public.anulacion_pedido TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.reservacion TO elcarbonazo_runtime;
GRANT UPDATE (estado, cancelada_utc) ON public.reservacion TO elcarbonazo_runtime;
GRANT UPDATE (revision_detalles) ON public.comanda TO elcarbonazo_runtime;
GRANT UPDATE (cancelado) ON public.detalle_pedido TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.operacion_anulacion, public.autorizacion_anulacion, public.alerta_anulacion TO elcarbonazo_runtime;
GRANT UPDATE (estado, fecha_terminal_utc, status_resultado, codigo_resultado, resultado_json)
    ON public.operacion_anulacion TO elcarbonazo_runtime;
GRANT UPDATE (fecha_consumo_utc) ON public.autorizacion_anulacion TO elcarbonazo_runtime;
GRANT UPDATE (fecha_acuse_utc, id_usuario_acuse) ON public.alerta_anulacion TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.limite_autorizacion TO elcarbonazo_runtime;
-- Runtime may record a key change but cannot provision, recreate or delete the singleton.
GRANT SELECT ON public.estado_hmac_autorizacion TO elcarbonazo_runtime;
GRANT UPDATE (huella, espera_hasta_utc) ON public.estado_hmac_autorizacion TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.anulacion_pedido_id_anulacion_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.reservacion_id_reservacion_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.autorizacion_anulacion_id_autorizacion_seq TO elcarbonazo_runtime;

GRANT USAGE, SELECT ON SEQUENCE public.grupo_mesa_id_grupo_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.pedido_id_pedido_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.producto_id_producto_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.comanda_id_comanda_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.detalle_pedido_id_detalle_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.token_refresh_sesion_id_token_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.evento_auditoria_id_evento_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.turno_caja_id_turno_caja_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.cobro_id_cobro_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.cobro_numero_comprobante_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.comprobante_cobro_linea_id_comprobante_cobro_linea_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.ingrediente_id_ingrediente_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.receta_id_receta_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.detalle_receta_id_detalle_receta_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.movimiento_inventario_id_movimiento_seq TO elcarbonazo_runtime;

GRANT SELECT, INSERT, UPDATE ON public.usuario TO elcarbonazo_bootstrap;
GRANT USAGE, SELECT ON SEQUENCE public.usuario_id_usuario_seq TO elcarbonazo_bootstrap;
GRANT SELECT, INSERT ON public.evento_auditoria TO elcarbonazo_bootstrap;
GRANT USAGE, SELECT ON SEQUENCE public.evento_auditoria_id_evento_seq TO elcarbonazo_bootstrap;
REVOKE ALL ON SCHEMA public FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE UPDATE, DELETE, TRUNCATE ON public.evento_auditoria FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE UPDATE, DELETE, TRUNCATE ON public.movimiento_inventario FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE UPDATE, DELETE, TRUNCATE ON public.consumo_detalle, public.anulacion_pedido FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
