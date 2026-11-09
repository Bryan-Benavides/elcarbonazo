# Tareas — 004 Caja, cobros y comprobante interno

**Estado:** D0 funcional y técnico aprobado por el usuario; paquete documental consistente y listo para iniciar C1. Implementación **no iniciada**. No aplicar migraciones ni tocar bases persistentes. Aceptación prevista únicamente para demostración universitaria local aislada; no equivale a aptitud para operar un restaurante con dinero real.

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

- [ ] Inspeccionar entidades/migraciones/grants actuales 001/003 en repositorio; comprobar con lectura autorizada el esquema únicamente si se confirma destino. No imprimir secretos.
- [ ] Implementar el modelo aprobado `turno_caja`, `cobro`, snapshot de líneas y `operacion_caja`, incluyendo precisión, CHECKs, retención y número interno.
- [ ] Diseñar y demostrar orden de locks en matriz de flujos con recursos 001/003 y transacciones PostgreSQL independientes.
- [ ] Implementar configuración Fluent API, entidades, FK restrictivas, CHECK, índices únicos/parciales y secuencias con privilegio mínimo.
- [ ] Crear migración nueva, sin modificar migraciones históricas ni activar auto-migrate.
- [ ] Revisar SQL generado y permisos; probar migración limpia, desde 001+003 representativo, precondición incompatible/rollback y preservación de filas exclusivamente en PostgreSQL temporal.
- [ ] Cubrir constraints e índices y grants con rol runtime temporal; verificar que no hay UPDATE/DELETE indebidos.
- [ ] Cerrar la puerta C1 antes de ejecutar migración fuera de test; base persistente requiere destino confirmado y autorización expresa por separado.

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
