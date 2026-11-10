# Tareas — 004 Caja, cobros y comprobante interno

**Estado:** D0 funcional y técnico aprobado; C1 completado para desarrollo en `feature/004-caja-cobros`, con pruebas de migración y concurrencia en PostgreSQL temporal. C2 iniciado: normalización/hash internos en desarrollo; protocolo, autorización y diario aún no implementados. No aplicar migraciones ni tocar bases persistentes. Aceptación prevista únicamente para demostración universitaria local aislada; no equivale a aptitud para operar un restaurante con dinero real.

**Revisión de concurrencia:** una prueba PostgreSQL demostró que la lectura MVCC por sí sola no impide preparar un cobro después del control de pendientes. El usuario autorizó revisar el plan; §5 ahora exige barrera global compartida para preparar y cerrar. Ambos órdenes se prueban mediante transacciones de modelo; las implementaciones C2/C3 aún inexistentes requieren sus propias pruebas de integración.

**Avance de matriz:** las pruebas `Existing_send_or_kitchen_and_cash_locks_serialize` y `Cash_destination_lock_and_release_or_group_close_serialize` verifican bloqueos reales de envío, cocina, liberación y cierre de grupo frente al modelo de caja, en ambos órdenes y destinos correspondientes. Las pruebas existentes también cubren carreras entre esos servicios. Quedan para C2/C3/C5 las carreras de los servicios reales de caja y la matriz completa de negocio.

## D0 — Cierre documental y diseño aprobado

- [x] Acordar caja compartida, roles Jefa/Encargado, fondo cero, persistencia del turno tras logout y trazabilidad por actor.
- [x] Acordar cuentas elegibles solo con todas las comandas `Lista`, cuentas bloqueadas visibles, legadas/anómalas/cero bloqueadas.
- [x] Acordar cobro completo de un método, efectivo/vuelto, referencia externa y advertencia repetida; no verificación bancaria ni datos sensibles.
- [x] Acordar moneda NIO, impuestos cero para la demostración según información disponible, sin propina/FX y sin afirmar cumplimiento fiscal.
- [x] Acordar no liberar mesa/grupo con pago, cierre explícito de grupo antes de nuevo pedido en grupo y reconocer incompatibilidad con regla actual de 003.
- [x] Acordar conteo total, efectivo esperado sin egresos, diferencia con motivo, cierre con pendientes y no cierre con cobro incierto.
- [x] Acordar comprobante interno DEMOSTRACIÓN sin datos cliente, consulta en pantalla y sin anulación/edición.
- [x] Acordar recuperación durable para apertura/cobro/cierre, resolución por propietario/Jefa, cancelación con barrera y no reintentos automáticos.
- [x] Crear especificación funcional, plan técnico y tareas en este paquete, además de actualizar referencias de dominio/evolución.
- [x] Revisar consistencia entre `spec.md`, `plan.md`, `tasks.md`, `docs/domain.md` y especificaciones 003; adenda de grupo pagado quedó explícita como cambio pendiente de implementación.
- [x] Cerrar línea base de locks, matriz de recuperación, diario/ACL, retención, límites, referencias, rutas y códigos; C1 demuestra la compatibilidad efectiva con locks implementados y se detiene si pruebas contradicen la estrategia.
- [x] El usuario aprobó el diseño técnico D0 y autorizó iniciar C1. La autorización cubre desarrollo y pruebas temporales; no autoriza migrar una base persistente.

## C1 — Inventario y persistencia

- [x] Inspeccionar entidades/migraciones/grants actuales 001/003 en repositorio y confirmar el orden existente mesa → pedido → comanda; lectura de esquema persistente no realizada porque no se confirmó destino.
- [x] Implementar el modelo aprobado `turno_caja`, `cobro`, snapshot de líneas y `operacion_caja`, con precisión, CHECKs, número interno e inmutabilidad de turnos cerrados.
- [x] Diseñar y probar orden de locks con servicios 001/003 reales y transacciones PostgreSQL independientes que modelan el lado caja; probar preparación/cierre modelados en ambos órdenes. Repetir con servicios reales de caja en C2/C3/C5.
- [x] Implementar configuración Fluent API, entidades, FK restrictivas, CHECK, índices únicos/parciales y secuencias con privilegio mínimo.
- [x] Crear migración nueva `20261109224603_AddCajaPersistence`, sin modificar migraciones históricas ni activar auto-migrate; `Down` impide borrar evidencia.
- [x] Revisar migración generada y probar migración limpia, actualización desde 003 representativo, preservación de fila legada, unicidad de turno, forma inválida de efectivo y trigger de cierre, exclusivamente en PostgreSQL temporal.
- [x] Cubrir grants con rol runtime temporal; verificar permisos de actualización limitados y ausencia de INSERT/UPDATE/DELETE no concedidos en tablas de evidencia.
- [x] Cerrar la puerta C1 para desarrollo tras probar migración representativa con mesa, grupo activo, pedidos en cinco estados, comandas en tres estados y cuenta legada sin comanda. Los locks de alcance C1 se probaron; la estructura incompatible confirmó rollback sin cambios parciales en PostgreSQL temporal, y la barrera preparación/cierre está probada como modelo, no como servicio. Migración fuera de test requiere destino confirmado y autorización expresa por separado.

## C2 — Protocolo durable de operaciones de caja

- [ ] Implementar prepare/execute/query/cancel con UUID, contrato versionado, canonicalización/hash y autorización vigente.
- [ ] Resolver concurrentemente preparación/cancelación/ejecución, incluidas barreras antes de preparación, solicitudes tardías y mismo UUID con payload/actor divergente.
- [ ] Guardar mutación de dominio, auditoría y resultado terminal en transacción única; diseñar savepoints para rechazos deterministas y probar rollback.
- [ ] Definir retención/archivo sin purgar operaciones no terminales ni permitir reejecución de UUID antiguo.
- [ ] Implementar almacenamiento local protegido de identidad mínima de recuperación, con ACL/atomicidad/fallo cerrado y sin tokens/contraseñas; revisar con instalador Windows antes de aprobar.
- [ ] Probar timeout antes/después del commit, fallo de cuerpo/respuesta, reinicio de API/Desktop, sesión/rol revocado, actor distinto, intervención Jefa y concurrencia entre terminales.
- [ ] Verificar que `Preparada`/`No encontrada`/GET de negocio no desbloquean acciones, y que cancelación no revierte cobro ya confirmado.

## C3 — API de caja y reglas de dominio

- [ ] Implementar apertura única; cero con confirmación; actor y fondo auditados.
- [ ] Implementar consulta paginada de cuentas elegibles/bloqueadas y motivos estables; permisos aplicados en API.
- [ ] Implementar preparación/ejecución de cobro total con revalidación de estado, líneas y total; efectivo insuficiente no persiste y vuelto exacto.
- [ ] Implementar métodos tarjeta/transferencia tras confirmación externa humana; referencia validada y colisión advertida con confirmación auditable.
- [ ] Persistir `pedido=Pagado`, cobro, snapshot de comprobante, auditoría y resultado terminal atómicamente; índice único impide doble pago.
- [ ] Implementar resumen/cierre, efectivo esperado sin egresos, conteo, diferencia/motivo, cuentas pendientes con aviso y bloqueo por operación incierta.
- [ ] Implementar consulta autenticada y paginada de comprobantes históricos, sin datos del cliente, con leyenda demo/no fiscal.
- [ ] Aplicar autorización RF-01 y tabla de roles en todas las rutas; denegar Supervisor/Cocina/Mesero donde corresponde.
- [ ] Añadir conflictos/códigos estables y comprobar no enumeración; probar fallo de auditoría con rollback.
- [ ] Integrar cambios explícitos a 003 para impedir nuevo envío al grupo con cuenta pagada mientras el grupo siga activo; conservar historial y compatibilidad de cuentas ya existentes.

## C4 — Desktop caja

- [ ] Añadir DTOs/clientes REST y ViewModels sin referencia API/EF/Npgsql; bindings compilados y code-behind limitado a inicialización.
- [ ] Implementar apertura y resumen del turno; mostrar actor/estado y fondo incluido cero con confirmación.
- [ ] Implementar cuentas cobrables/bloqueadas con explicación y detalle histórico.
- [ ] Implementar flujo de confirmación, efectivo recibido/vuelto, método externo y referencia con advertencia repetida; nunca afirmar verificación bancaria.
- [ ] Implementar comprobante y consulta histórica con leyenda de demostración, y cierre con conteo/diferencia/motivo/pendientes.
- [ ] Integrar diario de recuperación, pausa, reconexión, resultado incierto y resolución humana; no reintentos automáticos ni desbloqueo por GET.
- [ ] Integrar navegación por roles solo a destinos implementados, sesión vigente, coordinator/polling serializado y descartado de lecturas obsoletas.
- [ ] Probar servicios/ViewModels con fakes, respuestas inválidas, campo ausente vs null, concurrencia de consulta/mutación y cambio de sesión.
- [ ] Realizar aceptación visual real con teclado/ratón, foco, contraste, escalado, tamaño mínimo, mensajes y desconexión; registrar evidencia y limitaciones.

## C5 — Integración, regresión y aceptación

- [ ] Correr flujos completos de mesa y grupo en datos ficticios/aislados: comanda → todas listas → cobro → comprobante → liberación/cierre explícito.
- [ ] Probar matriz de carreras de `plan.md §5` con PostgreSQL y conexiones independientes, con intercalaciones en ambos órdenes.
- [ ] Probar toda la recuperación por puntos de fallo y reinicios, una sola mutación como máximo y barrera segura.
- [ ] Probar migración desde estados/cuentas 003 activos, legados, pagados/cancelados y preservar históricos.
- [ ] Probar operación con rol runtime mínimo y ausencia de DDL/permisos de borrado.
- [ ] Ejecutar AcceptanceHost aparte si se crea/modifica/usa; bases de prueba se crean y limpian solo por el harness autorizado.
- [ ] Ejecutar secuencia final: `dotnet format ElCarbonazo.sln --verify-no-changes --no-restore` → build → tests API → tests Desktop → auditoría de paquetes vulnerables; no afirmar mediciones no realizadas.
- [ ] Presentar recorrido de demostración al usuario y obtener aceptación expresa de comportamiento y UI para perfil universitario.
- [ ] Actualizar `docs/validacion-004.md` con evidencia, entorno, decisiones y limitaciones. Crear solo cuando existan resultados observados; no anticipar aceptación.

## C6 — Requisitos previos a uso real (no incluidos en aceptación universitaria)

- [ ] Validar régimen fiscal, impuestos, documentos, numeración, correcciones y retenciones con negocio y asesor competente.
- [ ] Aprobar proceso de anulación/reembolso/incidente y conciliación con adquirentes/transferencias.
- [ ] Revisar privacidad, retención de referencias, separación de funciones, roles y procedimientos humanos.
- [ ] Completar evaluación de seguridad, backup/restauración, monitoreo, permisos, continuidad y recuperación ante desastre.
- [ ] Probar LAN física sin Internet, certificados, equipos, escala/pantallas, rendimiento observado y aceptación de restaurante.
- [ ] Definir datos reales, instalación, soporte, capacitación y plan de despliegue/migración con autorización y respaldo.

## Criterio de cierre

004 se marca terminado solo para la demostración local aislada una vez que C0 técnico esté aprobado, C1–C5 estén verificadas, las limitaciones se documenten y el usuario acepte el recorrido. Completar C1–C5 no marca C6 ni autoriza cobro real. No declarar producción, cumplimiento fiscal, validación LAN, cobertura o rendimiento sin evidencia específica.
