# Tareas — 003 Pedidos y monitor KDS

**Estado:** D0 y D1 completos; D2 en ejecución. El catálogo paginado está implementado; consulta de cuenta y envío siguen pendientes. Implementar conforme a `spec.md`/`plan.md`; cualquier cambio de alcance requiere actualizar y aprobar la especificación antes de codificarlo. No aplicar migraciones ni modificar bases persistentes sin autorización expresa y destino confirmado. `002-operaciones-seguras` sigue pospuesto y separado.

## D0 — Acuerdos y diseño

- [x] Aprobar roles por operación y acceso a cuentas ajenas: roles de salón consultan todas las mesas y envían; Jefa/Supervisor/Cocina operan KDS; Mesero/Caja ven estado desde cuenta; Cocina no consulta/envía cuentas ni cambia mesas/grupos.
- [x] Aprobar agregación: comandas Abierta → En Preparación → Lista; cuenta agrega Abierto si alguna comanda está Abierta, si no En Preparación cuando alguna lo está, y Listo cuando todas están Listas. Un envío nuevo la devuelve a Abierto sin reiniciar comandas previas.
- [x] Aprobar visibilidad del estado `Lista`: visible para todos los roles de salón autorizados a consultar cuentas, sin afirmar que el plato fue retirado ni añadir acuse de lectura.
- [x] Aprobar cuentas activas heredadas sin comandas como solo lectura, visibles como cuenta anterior sin detalle y bloqueantes de nuevos envíos al destino; conservar filas/estado, no inventar líneas ni pagos y exigir resolución futura explícita/auditable.
- [x] Decidir para el régimen de cuota fija informado: no añadir IVA 15 %, `impuestos = 0`, `total = subtotal`; no presentar pedidos como facturas.
- [x] Documentar y aprobar `envioId` persistido, deduplicación transaccional y cobertura de carreras/resultado incierto; no reintentar escrituras automáticamente ni tomar GET como confirmación.
- [x] Documentar y aprobar límites iniciales, validación Unicode, aritmética `impuestos=0`/`total=subtotal`, contratos HTTP/ProblemDetails, conflictos, cursor/paginación y criterios de aceptación en `plan.md`.
- [x] Documentar y aprobar orden de locks candidato, coordinación de sondeo, migración no destructiva, datos de demostración y recuperación limitada sin asumir implementación.
- [x] Revisar y aprobar la línea base técnica de `plan.md`, incluidos máximos, líneas repetidas, idempotencia, HTTP, polling y paginación.
- [x] Revisar el orden de locks frente a `GruposMesaService`; cerrar reglas de auditoría, aviso persistente y resultado incierto. La revisión documental no sustituye las pruebas de concurrencia de D1/D2.

## D1 — Persistencia y migraciones

- [x] Diseñar entidades, FK restrictivas, CHECK, precisión, índices, versiones y compatibilidad con `pedido` existente.
- [x] Diseñar persistencia de idempotencia y hash semántico; respuesta durable en la misma transacción que cuenta/comanda/detalle/auditoría, con índice único y retención sin caducidad en 003.
- [x] Diseñar `producto.version` monotónica; mapear `Mesa.Version` y `Comanda.Version` a tokens opacos `xmin` y validar igualdad, nunca ordenarlos.
- [x] Implementar migración nueva sin alterar migraciones históricas; abortar de forma atómica ante colisión incompatible y preservar legados.
- [x] Probar aplicación limpia, actualización desde 001 con cuenta activa legada sin comandas, detección de tabla incompatible y ausencia de pérdidas, solo en PostgreSQL temporal.
- [x] Probar `deploy/runtime-grants.sql` con roles temporales y verificar privilegios mínimos de tablas/secuencias y ausencia de permisos de escritura indebidos.
- [x] Preparar catálogo de demostración con comando explícito, idempotente y transaccional, restringido a Development y bases `elcarbonazo_demo_*`/`elcarbonazo_test_*`; probar que no sobrescribe un catálogo distinto.

## D2 — API de catálogo y cuentas

- [x] Implementar `GET /api/productos` con autorización de salón, filtro de disponibilidad, cursor opaco ligado al filtro y páginas de 1–100 (25 por defecto).
- [ ] Implementar contratos validados de consulta de cuenta y envío; validar autorización, destino XOR y reglas de grupo/mesa.
- [ ] Congelar nombres/precios de demostración y calcular importes en API; pruebas de límites, cambio de precio/disponibilidad, aritmética exacta (sin impuesto), rechazo completo y auditoría.
- [ ] Confirmar primer envío y ocupación en transacción; envíos posteriores crean comanda adicional en la misma cuenta. Comprobar unicidad y respuesta/versiones.
- [ ] Probar idempotencia en repetición secuencial/concurrente, mismo ID con contenido distinto, timeout tras commit, rollback/fallo de auditoría y reinicio de API; comprobar resultado original y ausencia de duplicados.
- [ ] Probar carreras independientes abrir/abrir, enviar/liberar y enviar/cerrar grupo; verificar auditoría atómica y que 001 conserva comportamiento.

## D3 — API de cocina

- [ ] Implementar cola FIFO con desempate y transición autorizada con versión, revalidación y recálculo de cuenta.
- [ ] Probar dos cocinas y concurrencia con envíos; validar que una comanda lista sale de cola pero sigue visible en cuenta y otra nueva no altera líneas históricas.
- [ ] Probar permisos, errores, datos heredados, auditoría y rollback. No habilitar anulación, edición de enviados ni pago.

## D4 — Desktop salón

- [ ] Implementar navegación real, catálogo demostrativo, borrador local, categorías, cantidad y notas accesibles; distinguir visualmente borrador y confirmación.
- [ ] Implementar resumen de cuenta/comandas y aviso de lista según contrato aprobado; consultar únicamente por REST.
- [ ] Coordinar polling/mutaciones y sesión; tratar desconexión, conflictos, cambios de precio y resultado incierto sin reintentos automáticos ni afirmaciones falsas.
- [ ] Probar ViewModels/servicios con fakes, flujos de mesa/grupo, lectura obsoleta, expiración y acceso por rol.

## D5 — Desktop cocina

- [ ] Implementar KDS táctil y teclado, estados, notas, FIFO y transiciones; el color no es el único indicador.
- [ ] Probar conectividad, actualización, estados vacíos, concurrencia y restricciones de rol; no declarar tiempos de render no medidos.

## D6 — Integración y aceptación

- [ ] Ejecutar migraciones y pruebas API solo en bases de prueba aisladas, incluida la compatibilidad de 001 y pruebas de fallo/reinicio según alcance aprobado.
- [ ] Ejecutar formato → build → pruebas API → pruebas Desktop → auditoría de paquetes. Si se usa o cambia AcceptanceHost, compilarlo/probarlo aparte de la solución.
- [ ] Validar visualmente y con usuario: táctil, teclado, foco, contraste, tamaño mínimo/escalado, desconexión, selección, cola y alertas; registrar evidencia y pendientes.
- [ ] Documentar demostración, datos ficticios, decisiones, limitación de pagos/inventario y ausencia de recuperación definitiva. Mantener fuera de producción hasta resolver los requisitos correspondientes.
- [ ] Solicitar aceptación del usuario; no marcar completo por compilar ni por preparar esta documentación.
