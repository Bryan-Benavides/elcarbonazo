# Tareas — 004 Caja, cobros y comprobante interno

**Estado:** D0 funcional y técnico aprobado; C1 completado para desarrollo en `feature/004-caja-cobros`, con pruebas de migración y concurrencia en PostgreSQL temporal. C2 en curso: rutas y núcleo durable implementados parcialmente; ejecución real, almacenamiento protegido y pruebas de recuperación completas pendientes. No aplicar migraciones ni tocar bases persistentes. Aceptación prevista únicamente para demostración universitaria local aislada; no equivale a aptitud para operar un restaurante con dinero real.

**Revisión de concurrencia:** una prueba PostgreSQL demostró que la lectura MVCC por sí sola no impide preparar un cobro después del control de pendientes. El usuario autorizó revisar el plan; §5 ahora exige barrera global compartida para preparar y cerrar. Ambos órdenes se prueban mediante transacciones de modelo; las implementaciones C2/C3 aún inexistentes requieren sus propias pruebas de integración.

**Avance de matriz:** las pruebas `Existing_send_or_kitchen_and_cash_locks_serialize` y `Cash_destination_lock_and_release_or_group_close_serialize` verifican bloqueos reales de envío, cocina, liberación y cierre de grupo frente al modelo de caja, en ambos órdenes y destinos correspondientes. Las pruebas existentes también cubren carreras entre esos servicios. Quedan para C2/C3/C5 las carreras de los servicios reales de caja y la matriz completa de negocio.

**Prioridad para la demostración universitaria:** avanzar primero en la pantalla y el recorrido usable de apertura, cobro, comprobante, cierre y recuperación, manteniendo las comprobaciones esenciales de datos, permisos y no duplicación. No ampliar el alcance con endurecimiento o validaciones propias de producción; C6 queda pospuesto. Las tareas y pruebas ya exigidas por la especificación siguen abiertas hasta comprobarlas o aprobar expresamente su cambio; este orden no equivale a declarar el módulo terminado.

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

Avance parcial: normalización/hash y servicios de preparación, consulta, cancelación y reproducción terminal comparten lock UUID; las nuevas preparaciones toman barrera global y la de cobro bloquea/revalida destino, cuenta y turno. Las cuatro rutas REST usan autenticación y rol y limitan/validan cuerpos. Apertura, cierre y cobro usan ejecutores reales. El núcleo usa savepoint y transacción compartida; pruebas de apertura, cierre y cobro cubren repetición y rollback de auditoría. Faltan integración completa del diario, comprobantes consultables y pruebas adicionales de recuperación y carreras.

Preparación y ejecución de apertura vuelven a comprobar sesión/rol después de esperar el lock global; pruebas PostgreSQL con cambio de rol durante la espera confirmaron rechazo sin mutación. El lock global se adquiere antes del savepoint para conservarlo al registrar rechazos terminales.

El diario tiene validador, almacenamiento con reemplazo y bloqueo exclusivo y aprovisionador explícito de directorio nuevo, probados en carpeta temporal Windows con ACL real para la cuenta actual. Desktop ensambla el coordinador con sesión y transporte REST sin reintentos de escritura; el comando explícito de aprovisionamiento no se ejecuta al iniciar. Las pruebas con sesión real y transporte HTTP ficticio cubren persistencia previa, desconexión, reinicio, cambio de actor, consulta y cancelación; reconciliación de negocio falla cerrada hasta C3. Faltan integración desde la pantalla de caja, instalador empaquetado y pruebas extremo a extremo con operaciones reales. Por decisión del usuario no se exige otra cuenta Windows real para la demostración; su acceso efectivo no se considera validado para uso real. En host de prueba, una cancelación del cliente tras el commit de cancelación se recuperó mediante consulta sin repetir escritura; una interrupción antes del commit del ejecutor ficticio dejó la operación preparada. El savepoint también se probó tras un error PostgreSQL que aborta la transacción, y se comprobó que una sesión revocada mientras espera el lock no prepara la operación. No son pruebas de cobros reales ni validación LAN.

El coordinador permite solicitar ejecución solo de forma explícita tras observar la respuesta de preparación de la intención recién enviada. La observación se consume antes de enviar; tras respuesta incierta o reinicio, consultar `Preparada` no habilita una nueva ejecución y se conserva el diario para resolución humana. Jefa puede consultar y cancelar una intención local ajena con confirmación, sin ejecutarla como propietaria; la identidad del diario se conserva. Falta conectar estas acciones a la pantalla y reconciliar contra datos de negocio.

Reconciliación parcial: el transporte puede volver a consultar una apertura, cierre o cobro **completados**, validar el resumen del turno o el comprobante asociado a la misma operación y solicitar que un consumidor aplique esa fotografía antes de limpiar el diario. Pruebas con HTTP y consumidor ficticios conservan el diario si faltan datos, el resultado no es terminal o la aplicación de la fotografía falla. La pantalla inicial de recuperación registra un consumidor real; faltan integración con la futura pantalla de caja y pruebas con servicios reales antes de cerrar C2.

La API dispone de evidencia mínima autorizada para una operación rechazada o cancelada, incluida la distinción de barrera sin intención. Una prueba con PostgreSQL temporal cubre intención preparada, cancelación y barrera. La consulta por sí sola no permite limpiar el diario; faltan reconciliación completa de cuenta/turno, pantalla y pruebas de sesión y concurrencia.

Avance Desktop: para un cierre rechazado o cancelado con intención, el transporte verifica estado HTTP y durable, UUID, tipo y datos explícitos de evidencia, consulta el turno identificado y exige aplicar la fotografía antes de limpiar el diario. Pruebas con HTTP y consumidor ficticios cubren evidencia incompleta o contradictoria, rechazo de aplicación y cambio de actor. C2/C4 siguen abiertos.

Avance adicional parcial: para apertura rechazada/cancelada con intención se valida la evidencia y el turno actual, distinguiendo `turno: null` de un campo ausente. Para cobro rechazado/cancelado con intención se localiza la cuenta en el listado paginado y se obtiene el turno indicado; se solicita al consumidor aplicar ambos resúmenes juntos. El resumen de cuenta es informativo y no sustituye una nueva revisión completa antes de cobrar. La búsqueda tiene límite conservador de 100 páginas y falla cerrada si no alcanza la cuenta. Para una barrera sin intención, el transporte exige evidencia explícita de cancelación con IDs/tipo nulos, consulta el turno actual y solicita al consumidor invalidar todas las cuentas y comprobantes almacenados y exigir revisión nueva antes de escribir; no adivina el recurso. También se comprueba una generación local de sesión para no aceptar respuestas ni ejecutar una preparación observada antes de un nuevo login del mismo actor. Falta probar servicios reales y concurrencia de sesión de extremo a extremo.

Pantalla inicial de recuperación: Jefa y Encargado pueden abrirla desde Mesas y consultar, cancelar con confirmación y conciliar. El consumidor muestra la fotografía antes de limpiar el diario; esta vista no mantiene caché de cuentas/comprobantes ni dispone de acciones de cobro. Una prueba de ViewModel con transporte ficticio cubre confirmación y aplicación previa. No es la pantalla completa de caja; falta integrar sus futuras cachés con la invalidación de barreras y realizar revisión visual real.

- [ ] Implementar prepare/execute/query/cancel con UUID, contrato versionado, canonicalización/hash y autorización vigente.
- [ ] Resolver concurrentemente preparación/cancelación/ejecución, incluidas barreras antes de preparación, solicitudes tardías y mismo UUID con payload/actor divergente.
- [ ] Guardar mutación de dominio, auditoría y resultado terminal en transacción única; diseñar savepoints para rechazos deterministas y probar rollback.
- [x] Definir retención/archivo sin purgar operaciones no terminales ni permitir reejecución de UUID antiguo: `plan.md §4` conserva resultados completos al menos cinco años, prohíbe purga automática y exige tombstone permanente para compactación administrativa futura. No existe implementación de compactación en esta entrega.
- [ ] Implementar almacenamiento local protegido de identidad mínima de recuperación, con ACL/atomicidad/fallo cerrado y sin tokens/contraseñas; revisar con instalador Windows antes de aprobar.
- [ ] Probar timeout antes/después del commit, fallo de cuerpo/respuesta, reinicio de API/Desktop, sesión/rol revocado, actor distinto, intervención Jefa y concurrencia entre terminales.
- [ ] Verificar que `Preparada`/`No encontrada`/GET de negocio no desbloquean acciones, y que cancelación no revierte cobro ya confirmado.

## C3 — API de caja y reglas de dominio

- [x] Implementar apertura única; cero con confirmación; actor y fondo auditados. Verificada por HTTP y PostgreSQL temporal con repetición, concurrencia y fallo de auditoría. La consulta y presentación del turno siguen pendientes.
- [x] Implementar consulta paginada de cuentas elegibles/bloqueadas y motivos estables; permisos aplicados en API. La consulta incluye detalle histórico y bloquea intenciones preparadas visibles, sin exponer notas. Pruebas HTTP cubren mesa/grupo, cuentas legadas/canceladas, roles y cursores. Es una lectura informativa: el futuro ejecutor debe revalidar bajo locks y no puede asumir que una operación no visible haya terminado.
- [ ] Implementar preparación/ejecución de cobro total con revalidación de estado, líneas y total; efectivo insuficiente no persiste y vuelto exacto.
  - Ejecutor implementado y probado con efectivo, repetición, insuficiencia, cambio de comanda y fallo de auditoría; faltan pruebas adicionales de carreras y recuperación con los servicios reales.
- [ ] Implementar métodos tarjeta/transferencia tras confirmación externa humana; referencia validada y colisión advertida con confirmación auditable.
  - Implementado y probado secuencialmente por HTTP con tarjeta y transferencia, incluida referencia repetida y confirmación expresa; faltan pruebas de colisiones concurrentes.
- [ ] Persistir `pedido=Pagado`, cobro, snapshot de comprobante, auditoría y resultado terminal atómicamente; índice único impide doble pago.
- [ ] Implementar resumen/cierre, efectivo esperado sin egresos, conteo, diferencia/motivo, cuentas pendientes con aviso y bloqueo por operación incierta.
  - Cierre ejecutable y consultas de turno actual/resumen implementados; las lecturas de turno abierto usan fotografía consistente y las de turno cerrado conservan el snapshot. Pruebas cubren totales por método, diferencia, cuentas pendientes, intención preparada y fallo de auditoría. Listado paginado de pendientes dentro del resumen, aviso Desktop y carreras con ejecución real de cobro siguen pendientes.
- [x] Implementar consulta autenticada y paginada de comprobantes históricos, sin datos del cliente, con leyenda demo/no fiscal. Pruebas HTTP cubren rol, paginación, preservación ante cambios de catálogo y destino de grupo tras su cierre; la presentación Desktop sigue pendiente.
- [ ] Aplicar autorización RF-01 y tabla de roles en todas las rutas; denegar Supervisor/Cocina/Mesero donde corresponde.
- [ ] Añadir conflictos/códigos estables y comprobar no enumeración; probar fallo de auditoría con rollback.
- [ ] Integrar cambios explícitos a 003 para impedir nuevo envío al grupo con cuenta pagada mientras el grupo siga activo; conservar historial y compatibilidad de cuentas ya existentes.
  - La regla está implementada en envío. Una prueba con PostgreSQL temporal verifica el bloqueo, el cierre explícito, la creación de otro grupo con las mismas mesas y un nuevo envío sin alterar el pedido pagado histórico. Falta el recorrido completo con cobro real y las carreras aplicables.

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
