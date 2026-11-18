# Pruebas del software — El Carbonazo

**UNIVERSIDAD NACIONAL DE INGENIERÍA — DACTIC**
**Programa Académico de Ingeniería de Sistemas · Asignatura: Ingeniería de Software I**
**Sistema:** El Carbonazo — POS para restaurante (prototipo universitario local, offline-first; no autorizado para dinero real ni operación del restaurante)

**Objetivo de este documento:** demostrar el cumplimiento de los requerimientos funcionales (RF) y no funcionales (RNF) mediante pruebas **funcionales**, de **validación**, de **integración** y de **aceptación**, con matriz de pruebas, incidencias registradas, correcciones aplicadas y resultados finales.

**Rigor de la evidencia:** cada caso cita su fuente verificable — prueba automatizada (archivo y nombre exacto de la prueba, tal como figura en el código), ejecución de `tests/ElCarbonazo.AcceptanceHost`, documento de validación (`docs/validacion-*.md`) o registro de bitácora (`docs/bitacora-errores-y-lecciones.md`). Lo no validado se declara como no validado; ninguna prueba automatizada sustituye la revisión visual ni la prueba física LAN que correspondan.

---

## 1. Estrategia y entorno de pruebas

### 1.1 Niveles de prueba

| Nivel | Dónde vive | Qué verifica | Aislamiento |
|---|---|---|---|
| **Unitario / ViewModel (Desktop)** | `tests/ElCarbonazo.Desktop.Tests` (28 archivos; todos clases de prueba) | ViewModel, coordinadores, transporte, sesión, configuración — con servicios simulados (fakes) | Sin base; servicios HTTP sustituidos |
| **Integración (API + base de datos)** | `tests/ElCarbonazo.Api.Tests` (21 archivos: 19 clases de prueba + 2 archivos de soporte) | Controllers, servicios, EF Core, migraciones, concurrencia, permisos, auditoría | PostgreSQL **temporal** `elcarbonazo_test_<uuid>` por fixture; se crea y elimina en cada suite |
| **Integración extremo a extremo** | `tests/ElCarbonazo.AcceptanceHost` (fuera de la solución) | Los servicios REST reales del Desktop contra la API y PostgreSQL temporales (smokes `--caja-smoke`, `--pedidos-smoke`, `--inventario-smoke`, `--catalogo-defensa-smoke`; entornos manuales `--pedidos`, `--smoke`, `--cadencia`, `--medir`) | Base temporal propia + HTTP loopback (127.0.0.1); sesión **adaptada** (no valida ventana, render ni TLS real) |
| **Aceptación** | Revisión en ejecución por el usuario, registrada en `docs/validacion-*.md` | Comportamiento real en pantalla y recorrido integrado | Entornos de demostración aislados (`elcarbonazo_demo_local`, anfitrión temporal) |

Regla de aislamiento: **ninguna prueba destructiva apunta a la base de desarrollo `elcarbonazo`** (`AGENTS.md`); las pruebas API leen la conexión de `ELCARBONAZO_TEST_ADMIN_CONNECTION` o, de no existir, la de Development, siempre para crear/eliminar su propia base temporal.

### 1.2 Secuencia de aceptación (regresión estándar)

```bash
dotnet format ElCarbonazo.sln --verify-no-changes --no-restore
dotnet build ElCarbonazo.sln
dotnet test tests/ElCarbonazo.Api.Tests/ElCarbonazo.Api.Tests.csproj --no-build --no-restore
dotnet test tests/ElCarbonazo.Desktop.Tests/ElCarbonazo.Desktop.Tests.csproj --no-build --no-restore
dotnet list ElCarbonazo.sln package --vulnerable --include-transitive
```

Además, tras compilar el anfitrión por separado: `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --caja-smoke` (y los demás smokes según módulo). `Directory.Build.props` trata **las advertencias como errores**, así que un build correcto es también una puerta de calidad.

### 1.3 Evolución de la suite por cierre (regresiones completas documentadas)

| Cierre | Pruebas API | Pruebas Desktop | Evidencia |
|---|---|---|---|
| T6 — Desktop (módulo 001) | 9 | 79 | `docs/validacion-t6.md` |
| D6 — Pedidos/KDS (cierre 003; 9/11/2026) | 50 | 141 | `docs/validacion-d6.md` |
| 004 — Caja (aceptación local; posterior al smoke de 13/11/2026) | 125 | 232 | `docs/validacion-004.md` |
| 005 — Inventario (cierre C4) | 141 | 262 | `docs/validacion-005.md` |
| 006-auditoria (avance parcial; módulo pausado) | 144–159 | 262–265 | `docs/validacion-006.md` (regresiones parciales de 144 a 159 API en el curso de su avance) |
| **Regresión vigente del proyecto** | **163–165** | **267–268** | `specs/006-reservaciones-mesas/tasks.md` y `specs/007-reporte-ventas/tasks.md` (163/267); `specs/entrega-defensa-local/tasks.md` (163/267 y, tras `--catalogo-defensa-smoke`, 165/268); formato, build y auditoría NuGet limpios |

Cada fila es la corrida **completa** de cada suite registrada en el documento de su cierre; las series crecen de 9/79 a 163–165/267–268 porque cada incidente corregido quedó cubierto con regresión automatizada (§5).

---

## 2. Catálogo de requerimientos (trazabilidad)

Código usado en la matriz. Fuente: especificaciones SDD de `specs/<módulo>/spec.md` y `docs/constitution.md`.

### 2.1 Requerimientos funcionales

| Código | Requerimiento | Fuente |
|---|---|---|
| **RF-01** | Autenticación de usuarios (login con credenciales, inicio y cierre de sesión) | `specs/001-auth-mesas-mvp/spec.md` |
| **RF-02** | Ciclo de vida de la sesión: actividad, inactividad (15 min), tope absoluto (8 h), refresh rotatorio y revocación | 001 + `SessionSecurityPolicy.cs` |
| **RF-03** | Mapa de mesas con estados y cambios autorizados (liberar/ocupar) | 001 |
| **RF-04** | Agrupación de mesas (solo libres sin grupo) y cuentas compartidas de grupo | 001/003 + `docs/domain.md` |
| **RF-05** | Pedido de salón: catálogo, borrador, confirmación y envío idempotente; lectura de cuenta (individual/grupo) | `specs/003-pedidos-kds/spec.md` |
| **RF-06** | Cocina (KDS): cola FIFO de comandas y transiciones `Abierta → En Preparación → Lista` | 003 |
| **RF-07** | Caja: apertura/cierre de turno y operaciones de cobro (efectivo/tarjeta/transferencia) | `specs/004-caja-cobros/spec.md` |
| **RF-08** | Caja: comprobantes con líneas y consulta histórica paginada | 004 |
| **RF-09** | Caja: recuperación ante resultado incierto (diario local, conciliación, sin reintento) | 004 + `docs/domain.md` |
| **RF-10** | Inventario: ingredientes, movimientos (kardex) y alertas | `specs/005-inventario-escandallo/spec.md` |
| **RF-11** | Inventario: recetas activas y consumo de stock **atómico con el envío** de la comanda | 005 + `docs/domain.md` |
| **RF-12** | Reservaciones: disponibilidad, creación y cancelación de reserva | `specs/006-reservaciones-mesas/spec.md` |
| **RF-13** | Reporte de ventas del turno/día | `specs/007-reporte-ventas/tasks.md` (módulo aprobado e implementado; pendiente su revisión visual) |
| **RF-14** | Anulación de pedido con autorización superior (HMAC), reversión y alertas KDS — **MÓDULO PAUSADO: avance parcial, no funcionalidad aceptada** | `specs/006-auditoria-cancelaciones-seguridad/spec.md` |

### 2.2 Requerimientos no funcionales

| Código | Requerimiento | Fuente |
|---|---|---|
| **RNF-01** | Disponibilidad local (offline-first) y funcionamiento con API y Desktop en el mismo/dos equipos sin Internet | `docs/constitution.md` |
| **RNF-02** | Concurrencia: sin pérdidas de actualización, sin doble ejecución, sin lecturas obsoletas aplicadas | 003/004 + `docs/domain.md` |
| **RNF-03** | Incertidumbre: ante fallo de escritura no hay reintento automático; un GET no demuestra que la escritura terminó; se requiere reconciliación y revisión humana | 004/005 + `docs/domain.md` |
| **RNF-04** | Seguridad de acceso: JWT de 5 min, revocación por sesión, autorización por rol en cada operación, limitación de intentos | 001 + `Program.cs` |
| **RNF-05** | Auditoría y trazabilidad inmutable (append-only) de los hechos relevantes | 001/004/006 + `EnforceAuditAppendOnly` |
| **RNF-06** | No exposición de secretos: sin credenciales, tokens ni cadenas de conexión en interfaces, logs o auditoría | `AGENTS.md`, `README.md` |
| **RNF-07** | Interfaz usable legible (UI aprobada: estado textual + color, contrastes válidos) en el perfil universitario | `docs/diseno-visual-propuesta.md`, `AGENTS.md` |
| **RNF-08** | Arranque y configuración: `desktop.json` con `idInstalacion` UUID, perfiles HTTP/HTTPS correctamente asociados, validación TLS | `AGENTS.md`, `README.md`, ERR-008 (bitácora) |

---

## 3. Matriz de pruebas (ejecutadas y pendientes declaradas)

Estructura exigida: **Código | Requerimiento | Caso de prueba | Resultado esperado | Resultado obtenido | Estado**.
Estado: `Aprobado` (la evidencia cumplió lo esperado) o `No aprobado` (pendiente de ejecutar). Los `Aprobado¹` marcan cobertura automatizada completa con **revisión visual pendiente** (CP-54/55); los `Aprobado*` el avance parcial documentado del módulo **pausado** de RF-14. Los recuentos de casos por clase citados son las declaraciones `[Fact]`/`[Theory]` de cada archivo; los totales ejecutados de cada suite (incluida la expansión de `[Theory]` con datos) figuran en §1.3 y §6.1. Las evidencias citadas se listan al pie de cada matriz.

### Matriz A — Pruebas funcionales

| Código | Requerimiento | Caso de prueba | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| CP-01 | RF-01 | Login con credenciales válidas (éxito y rechazo seguro) | Sesión iniciada y navegación al mapa; ante rechazo, mensaje seguro y contraseña limpiada | `LoginViewModelTests` (4 casos: `SuccessNavigatesAndClearsPassword`, `RejectedLoginShowsSafeMessageAndClearsPassword`, `NetworkFailureDoesNotNavigateOrRetainPassword`, `PasswordVisibilityStartsHiddenAndHasAccessibleLabel`) | Aprobado |
| CP-02 | RF-02 | Actividad humana limitada, inactividad y expiración absoluta; polling sin registrar actividad humana | Refresco de actividad limitado; advertencia/expiración a 15 min de inactividad; tope absoluto 8 h; logout incluso si la red falla | `SessionBehaviorTests` (8 casos, incl. `LogoutClearsSessionEvenWhenServerCannotBeReached` y `PollingDoesNotRefreshOrReportHumanActivity`), `SessionRaceTests` (3 casos); cobertura focalizada de T6 ("actividad humana limitada, polling sin actividad, advertencia/expiración por inactividad y logout ante fallo de red") | Aprobado |
| CP-03 | RF-02 | Refresh concurrente (single-flight) y respuesta tardía tras logout | Un solo refresh simultáneo ante interacciones concurrentes; refresh tardío no restaura sesión revocada; advertencia/expiración por inactividad limpia la sesión | `SessionRaceTests` (3 casos: `LateRefreshCannotRestoreSessionAfterLogout`, `ConcurrentHumanInteractionsShareOneRefresh`, `InactivityWarningAndExpiryClearSession`); corrección de ERR-004 con regresión | Aprobado |
| CP-04 | RF-03 | Contrato HTTP del mapa de 15 mesas, cambios de estado y grupos | Todas las mesas con número, capacidad y estado; transiciones válidas aplicadas; inválidas rechazadas | Evidencia automatizada de T5 (API sobre base temporal) + `MesasPollingLifecycleTests` (26 casos) y `MesasApiServiceTests` (6 casos); corrida del cierre T6: 9 pruebas API y 79 Desktop | Aprobado |
| CP-05 | RF-03 | Liberación de mesa exige confirmación y versión vigente | Sin confirmación no hay mutación; con versión obsoleta se rechaza | `ReleaseRequiresConfirmationAndUsesCurrentVersion` (`MesasPollingLifecycleTests.cs`); corrección de ERR-003 | Aprobado |
| CP-06 | RF-04 | Agrupar solo mesas libres sin grupo; cierre de grupo con confirmación | Grupo creado con sus miembros; cierre rechazado si hay cuenta pendiente; el rechazo no se reintenta solo | Rutas de agrupación en `ApiFlowTests` (35 casos); `GroupFailureIsNotRetriedAndRequiresReconciliation` (`MesasPollingLifecycleTests.cs`); corrección de ERR-007 | Aprobado |
| CP-07 | RF-05 | Catálogo, borrador y envío de pedido idempotente | Pedido persistido una sola vez con sus líneas y notas; repetición de la misma clave no duplica | `PedidosPersistenceTests` (12 casos, incl. envío atómico/idempotente), `PedidosSalonViewModelTests` (14 casos), `--pedidos-smoke` (envío y repetición idempotente) | Aprobado |
| CP-08 | RF-05 | Lectura de cuenta activa individual, de grupo y heredada de solo lectura | El grupo se lee como grupo (no como mesa individual); la cuenta vacía es resultado válido, no error | `ProductosPedidoApiServiceTests` (4 casos: `Account_reads_correct_group_without_treating_it_as_individual_table`, `Empty_account_is_a_valid_result`, ...); `--pedidos-smoke` (cuenta individual y de grupo) | Aprobado |
| CP-09 | RF-06 | Cola KDS FIFO y transiciones de comanda | Comandas aparecen en orden, avanzan a `Lista` y desaparecen de la cola al completarse; siguen visibles en la cuenta | `CocinaViewModelTests` (6 casos), `CocinaApiServiceTests` (4 casos), `--pedidos-smoke` (transición hasta `Lista`); aceptación D5 | Aprobado |
| CP-10 | RF-07 | Apertura de turno, cobros en efectivo, tarjeta y transferencia, y cierre con conteo | Turno único abierto; cobro persistido una vez por cuenta; cierre con totales por método y motivo de diferencia | `CajaOperacionesTests` (13 casos), `CajaCierreCarrerasTests` (8 casos), `--caja-smoke` (cobros en los tres métodos, vuelto, cierre con conteo) | Aprobado |
| CP-11 | RF-08 | Historial paginado de comprobantes con detalle validado | Páginas consistentes; detalle validado antes de aplicarse; el historial no invalida la selección ni el turno | `CajaRecoveryTransportTests` (21 casos, incl. `Receipt_history_pages_and_validates_detail_before_application`) + `--caja-smoke` (5 comprobantes consultados, uno con líneas) | Aprobado |
| CP-12 | RF-09 | Pérdida de la respuesta del cobro: recuperación por diario | La consulta del resultado durable + conciliación aplican el comprobante **sin repetir la ejecución**; diario liberado | `CajaRecoveryCoordinatorTests` (9 casos); ejecución documentada del 13/11/2026: en el segundo cobro "descartó deliberadamente una respuesta HTTP exitosa" y la consulta durable con la conciliación despejaron el diario, "sin repetir la ejecución" (`validacion-004.md`) | Aprobado |
| CP-13 | RF-10 | Ingredientes, movimientos y alertas de inventario | Movimientos exactos, auditados y versionados; validación de lecturas y alertas por stock | `InventarioServiceTests` (3 casos), `InventarioLecturaTests` (8 casos), `--inventario-smoke` (creación, entrada, edición, kardex) | Aprobado |
| CP-14 | RF-11 | Envío con receta: consumo atómico y reproducción sin duplicar | El envío consume stock según receta en la misma transacción; el replay no consume dos veces; sin receta activa o sin stock se rechaza | `InventarioEnviosTests` (7 casos, incl. `Send_requires_recipe_aggregates_stock_and_replay_does_not_consume_twice`); `--caja-smoke` "verificó consumo por envío y ausencia de un segundo descuento al cobrar" (`validacion-005.md`) | Aprobado |
| CP-15 | RF-12 | Reservaciones: disponibilidad, creación y cancelación | Disponibilidad por franja; cancelación libera el slot y deja la reserva fuera de la agenda activa | `ReservacionesAndReporteTests` (3 casos); `ReservacionesReporteViewModelTests` (`Agenda_hides_cancelled_items_and_counts_only_active_reservations`); lección L-02 de la bitácora (lista solo reservaciones activas) | Aprobado ¹ |
| CP-16 | RF-13 | Reporte de ventas del turno/día | Agregación de comprobantes históricos con fronteras semiabiertas y top 10; rango de zona de Managua convertido | `ReservacionesAndReporteTests` (`Report_aggregates_historical_receipts_with_half_open_boundaries_and_top_ten`), `ReservacionesReporteViewModelTests` (`Report_converts_managua_range_and_displays_results`) | Aprobado ¹ |
| CP-17 | RF-14 (pausado) | Autorización superior HMAC: intención durable, secreto único, expiración | Se emite un secreto aleatorio una sola vez; solo se guarda su huella (SHA-256); expiración acotada; vinculación intención/operación | `AnulacionAutorizacionTests` (1 caso: `Authorization_binds_operation_to_intention_and_stores_only_token_hash`), `HmacProvisionPersistenceTests` (7 casos): flujo completo con PostgreSQL temporal, "sin guardar clave ni huella en auditoría" (`validacion-006.md`) | Aprobado* |
| CP-18 | RF-14 (pausado) | Ejecución de anulación: reversión exacta del snapshot, una sola vez | La anulación revierte exactamente su snapshot y reproduce el resultado terminal; barrera idempotente ante concurrencia y sesión revocada | `AnulacionEjecucionTests` (1 caso: `Authorized_execution_reverses_exact_snapshot_once_and_replays_terminal_result`), `AnulacionRecuperacionTests` (3 casos); prueba e2e de servicio con "repetición terminal sin duplicar stock" (`validacion-006.md`) | Aprobado* |

¹ **Aprobado con limitación declarada:** funcionalidad implementada y probada en código/base; la **revisión visual final de la vista** permanece pendiente (matriz D, CP-54/CP-55).
\* **Aprobado (avance parcial de módulo PAUSADO):** corresponde al avance conservado de `specs/006-auditoria-cancelaciones-seguridad`, que **no es una funcionalidad aceptada** del sistema: la UI Desktop no está cableada a la autorización ni a la ejecución de anulación (ver §2.1 y `validacion-006.md`).

### Matriz B — Pruebas de validación (datos válidos e inválidos)

| Código | Requerimiento | Caso de prueba | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| CP-19 | RNF-04 / RF-07 | Referencia externa repetida en tarjeta/transferencia | Rechazo **durable** de la coincidencia; conciliación y segunda confirmación explícita con nuevo UUID antes de reintentar | `ApiFlowTests` (`Caja_concurrent_external_reference_collision_records_only_one_unconfirmed_payment`, `Caja_external_reference_collision_requires_explicit_confirmation`); `--caja-smoke`: "referencia de tarjeta repetida produjo rechazo durable, conciliación y exigencia de segunda confirmación" (`validacion-004.md`) | Aprobado |
| CP-20 | RNF-04 | Bloqueo por 5 intentos de login fallidos + límite de 10 peticiones/min por IP | Bloqueo de 5 min tras 5 fallos; 429 con `Retry-After`; el primer 429 de cada ventana por IP auditado una sola vez | `LoginSecurityPolicy` (5/5 min) y rate limiter con auditoría `LoginRateLimitAuditGate` (una entrada por `ip:ventana`, `Program.cs`); aserción de la cabecera `Retry-After` en `ApiFlowTests` (`ApiFlowTests.cs:2082`) y matriz de rutas de seguridad | Aprobado |
| CP-21 | RNF-08 | Header `X-Installation-Id` ausente o inválido | 400 `VALIDATION_ERROR` sin procesar la petición | Middleware verificado en `Program.cs`; cubierto en la matriz de seguridad de `ApiFlowTests` | Aprobado |
| CP-22 | RF-05 / RF-02 | Datos inválidos de pedido: cantidades, notas, producto inexistente, desbordamiento | No se crea ningún pedido ni comanda; los límites de Unicode/precio/historial se aplican | `PedidosPersistenceTests` (`Invalid_quantities_notes_and_unavailable_products_never_create_orders`, `Order_limits_unicode_price_history_and_overflow_are_enforced`) | Aprobado |
| CP-23 | RF-05 | Unicode y validación coherente entre API y persistencia | Los mismos casos Unicode pasan o fallan igual en validación y en base | `PedidosPersistenceTests` (`Shared_unicode_cases_match_api_validation_and_persistence`) | Aprobado |
| CP-24 | RF-11 / RF-05 | Envío sin stock disponible o sin receta activa | Envío rechazado con faltantes; el borrador se conserva; la UI muestra faltantes solo con datos coherentes | `InventarioEnviosTests` (`Send_requires_recipe_aggregates_stock_and_replay_does_not_consume_twice`); `PedidosSalonViewModelTests` (`Stock_rejection_preserves_draft_and_only_displays_valid_shortage`) | Aprobado |
| CP-25 | RNF-05 | Fallo inyectado de auditoría en cada escritura | Rollback íntegro de la operación (caja/pedido/turno/inventario) incluida la intención idempotente | `CajaOperacionesTests` (3 pruebas `*_audit_failure_rolls_back_*`), `InventarioEnviosTests` (`Audit_failure_rolls_back_order_stock_and_consumption`), `PedidosPersistenceTests` (`Audit_failure_rolls_back_order_table_and_idempotency_record`) | Aprobado |
| CP-26 | RNF-02 | Esquema preexistente incompatible con la migración | La migración **aborta** dentro de su transacción, sin esquema parcial ni pérdida de datos | `PedidosPersistenceTests` (`Incompatible_existing_catalog_table_aborts_migration_without_partial_schema_changes`), `CajaPersistenceTests` (`Incompatible_cash_table_aborts_migration_transaction_without_losing_existing_data`) | Aprobado |
| CP-27 | RNF-02 | Restricciones de persistencia ante datos inválidos | Un solo turno abierto a la vez; forma de cobro válida; kardex y CHECK de snapshot rechazan inválidos (incluido `NaN`) | `CajaPersistenceTests` (`Persistence_constraints_enforce_one_open_shift_and_valid_cash_payment_shape`), `InventarioPersistenceTests` (`Constraints_reject_invalid_stock_duplicates_and_invalid_ledger_entries`), `CancelacionesPersistenceTests` (`Constraints_reject_invalid_operations_and_snapshots`); verificación `NaN::numeric > 0` en bitácora (L-04) | Aprobado |
| CP-28 | RF-12 | Disponibilidad de reservaciones con frontera semiabierta y capacidad | Rango `[inicio, fin)`; rechazo si no hay capacidad; un slot único para la misma mesa/franja | `ReservacionesAndReporteTests` (`Availability_uses_half_open_ranges_and_checks_capacity`, `Concurrent_reservations_for_same_table_allow_exactly_one_and_cancel_releases_slot`) | Aprobado |
| CP-29 | RF-05 / RNF-03 | Reintento manual tras rechazo de envío | El borrador no se libera para una nueva intención sin reconciliar la respuesta incierta | `PedidosSalonViewModelTests` (`Rejected_manual_replay_does_not_release_uncertainty`) | Aprobado |
| CP-30 | RF-09 | Diario de caja con operación pendiente y UUID incorrecto | Una segunda instalación no sobrescribe el diario pendiente; UUID ajenos no lo leen | `CajaRecoveryStoreTests` (`Competing_process_models_cannot_replace_a_pending_operation`); validación en Windows real: "segunda ejecución rechazó sobrescribir un diario con operación pendiente y un UUID incorrecto no pudo leerlo" (`validacion-004.md`) | Aprobado |

### Matriz C — Pruebas de integración (comunicación entre componentes)

| Código | Requerimiento | Caso de prueba | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| CP-31 | RNF-02 | GET y mutación simultáneos en el mapa de mesas | La mutación espera a que termine la lectura previa; la lectura obsoleta nunca se aplica; ni una ni otra se solapan | `MesasRequestCoordinatorTests` (7 casos, incl. `OldReadFinishesBeforeMutationAndCannotApply`, `FailedReadPreventsMutationsAndReadsDoNotOverlap`) — corregió ERR-005 | Aprobado |
| CP-32 | RNF-03 | Respuesta incierta de escritura (salón, cocina, inventario) | Sin reintento automático; acciones bloqueadas hasta reconciliación; una lectura posterior **no** desbloquea | `PedidosSalonViewModelTests` (`Unexpected_response_io_failure_is_uncertain_and_cannot_create_new_intention`), `CocinaViewModelTests` (`Uncertain_write_remains_blocked_after_successful_read_and_cannot_return`), `InventarioEscrituraCoordinatorTests` (11 casos, incl. `Uncertain_write_is_not_repeated_or_unlocked_by_later_calls`) | Aprobado |
| CP-33 | RNF-02 | Carreras reales de caja (prepare/execute × cerrar turno, ambos órdenes) | Esperas observadas en PostgreSQL; resultado durable y auditoría consistentes; cancelar un cobro confirmado no lo revierte | `CajaCierreCarrerasTests` (8 casos) con bloqueos observados en `pg_stat_activity`; "ambos órdenes" verificados (`validacion-004.md`); `Cancellation_before_commit_leaves_operation_prepared_and_can_be_cancelled` en `CajaOperacionesTests` | Aprobado |
| CP-34 | RNF-02 | Carreras de envío × cocina × caja (ambos órdenes) | Envío, transición KDS y cobro se serializan correctamente; versiones obsoletas rechazadas | `EnviosConcurrencyTests` (17 casos: `Existing_send_or_kitchen_and_cash_locks_serialize`, `Two_kitchens_serialize_and_reject_stale_version`, `Paid_group_requires_explicit_close_before_new_send`) | Aprobado |
| CP-35 | RNF-02 / RF-11 | Dos envíos compiten por el último stock | Solo uno consume; el otro se rechaza; una sola línea de consumo | `InventarioEnviosTests` (`Competing_orders_only_consume_last_stock_once`) | Aprobado |
| CP-36 | RNF-02 / RF-12 | Dos reservaciones concurrentes para la misma mesa y franja | Exactamente una se crea; la cancelación libera el slot | `ReservacionesAndReporteTests` (`Concurrent_reservations_for_same_table_allow_exactly_one_and_cancel_releases_slot`) | Aprobado |
| CP-37 | RNF-04 | Cambio/revocación de rol mientras se espera el lock global de caja | Tras adquirir el lock se revalida sesión y rol; la preparación/ejecución no mutan datos | `CajaOperacionesTests` (`Revoked_role_while_waiting_for_global_lock_blocks_prepare_and_execution`); corrección "revalidar tras adquirir el lock global" (lección L-12) con conexiones PostgreSQL independientes | Aprobado |
| CP-38 | RNF-02 / RF-02 | Cambio de sesión durante lecturas/escrituras en curso | La lectura en vuelo se cancela/descarta; el diario conserva la intención original; un actor nuevo no ejecuta la preparación ajena | `MesasPollingLifecycleTests` (`DisposingCancelsInflightReadAndStopsPolling`, `TerminalSessionStateCancelsInflightRead`), `CajaRecoveryCoordinatorTests` (`Session_change_during_write_keeps_original_journal_until_explicit_recovery`, `Same_actor_new_session_cannot_execute_previous_preparation_or_clear_journal`) | Aprobado |
| CP-39 | RNF-01 / RF-05/06/11 | Recorrido extremo a extremo: pedido → envío (escandallo) → cocina `Lista` → cuenta | Ciclo completo con API real, PostgreSQL temporal y los servicios REST del Desktop; idempotencia y consumo únicos verificados | `--pedidos-smoke`, `--caja-smoke` y `--inventario-smoke` ejecutados (ver §4: ejecución del 13/11/2026 y cierre 005); sesión adaptada: no valida ventana ni LAN | Aprobado |
| CP-40 | RNF-08 | Configuración de entorno: desarrollo/producción, HTTPS, identidad de instalación, override | Override de base URL solo en Development; HTTPS verificado en producción; `idInstalacion` exigido incluso con override; destino no local rechazado | Cobertura focalizada de T6 ("configuración de producción/desarrollo, HTTPS, identidad de instalación y override de desarrollo con archivos de prueba aislados"); `validacion-004.md` ("rechazo de un destino no local, sin modificar el diario") | Aprobado |
| CP-41 | RNF-06 / RNF-04 | Fallo TLS vs fallo de red; evento de seguridad TLS | El fallo TLS se informa como fallo de seguridad (distinto de la red) y **bloquea y limpia** la sesión | `TransportAndClockTests` (3 casos, incl. `TlsFailureIsReportedAsSecurityFailure`), `SessionBehaviorTests` (`TlsSecurityEventBlocksAndClearsSession`) | Aprobado |
| CP-42 | RNF-06 | No exposición de credenciales/secretos | La contraseña se limpia tras login (éxito o rechazo); la auditoría no guarda correo ni credencial; los logs no contienen secretos | `LoginViewModelTests` (`SuccessNavigatesAndClearsPassword`, `RejectedLoginShowsSafeMessageAndClearsPassword`); "sin guardar correo/credencial en auditoría" (`validacion-006.md`); reglas de `AGENTS.md` | Aprobado |
| CP-43 | RNF-05 | Auditoría append-only bajo rol de ejecución | UPDATE/DELETE sobre evidencia y auditoría rechazados por permisos; eventos insertables por el rol runtime solo donde corresponde | Ejercicio de grants en PostgreSQL temporal (bitácora/`validacion-006.md`: "se rechazaron UPDATE/DELETE de evidencia y auditoría por falta de permisos"); `CancelacionesPersistenceTests` (`Authorization_and_alert_constraints_enforce_identity_and_expiration`) | Aprobado |
| CP-44 | RNF-08 | API y Desktop con perfiles de puerto inconsistentes (HTTP 5261 vs HTTPS 7191) | El login conecta solo cuando el perfil de la API coincide con la URL de Desktop; solución por perfil correcto, no por bypass de TLS | Causa confirmada y corregida (ERR-008); el usuario verificó login y desconexión/reconexión funcionando (T5/T6) | Aprobado |
| CP-45 | RNF-01 | Ciclo real de polling contra API temporal (6 GET) | Cadencia nominal ~2 s, **máximo 1 GET simultáneo**, sin solapes | `--cadencia` (5/11/2026): intervalos entre inicios 2007,4–2015,2 ms; duraciones de GET 6,6–37,4 ms; "máximo de GET simultáneos: uno" (`validacion-t5.md`) | Aprobado |

### Matriz D — Pruebas de aceptación (perfil universitario local)

| Código | Requerimiento | Caso de prueba | Resultado esperado | Resultado obtenido | Estado |
|---|---|---|---|---|---|
| CP-46 | RF-01/03, RNF-07 | Recorrido general en pantalla: login, mapa, acciones, agrupación, desconexión/reconexión, cierre de sesión | Las funciones responden como especificado y son legibles | Revisión visual informada por el usuario: "el usuario informó haber probado login, mapa, acciones, agrupación, desconexión/reconexión, cierre de sesión, colores de botones y acceso de Cocina" (`validacion-t6.md`) | Aprobado |
| CP-47 | RF-03 / RNF-01 | Sincronización real del mapa (mesa 04 libre↔ocupada) cambiada desde la consola de medición y observada en Desktop | El estado observable cambia tras la confirmación de la API (estimación de convergencia, sin cronometraje) | Verificado por el usuario en el entorno temporal; 47 confirmaciones de PUT: mín 9 ms, máx 53 ms, media 15,91 ms (duración HTTP **solo**, sin render); estimación visual 1–1,5 s declarada como aproximación (`validacion-t5.md`) | Aprobado |
| CP-48 | RF-05, RNF-07 | Aceptación D4 — pedido de salón en pantalla | Puntos 1, 2, 3, 5, 6 y 7 del checklist: conexión/catálogo, borrador, confirmación/envío, cuenta heredada de solo lectura, botón deshabilitado, interacción/layout | "El usuario confirmó que verificó satisfactoriamente los puntos 1, 2, 3, 5, 6 y 7… La captura posterior muestra catálogo cargado, cuenta 3 abierta, dos comandas abiertas y total C$ 490.00" (`validacion-d4.md`) | Aprobado |
| CP-49 | RF-06, RNF-07 | Aceptación D5 — cocina en pantalla | Comandas aparecen, avanzan a lista, desaparecen de la cola y persisten en la cuenta con estado listo; revisión de teclado/táctil, foco, FIFO, notas y reconexión | "El usuario confirmó que las comandas aparecen, avanzan por preparación a lista y desaparecen de la cola al completarse; en la cuenta de salón permanecen visibles con estado listo" (`validacion-d5.md`) | Aprobado |
| CP-50 | RF-03/05/06 / RNF-01 | Aceptación D6 — integración de dos terminales (salón + cocina) | Recorrido integrado funcionando; módulo 003 cerrado | "El usuario confirmó que el recorrido integrado funciona y pidió cerrar el módulo" (`validacion-d6.md`); sin migraciones a base persistente en ese entorno | Aprobado |
| CP-51 | RF-07/08/09 / RNF-07 | Aceptación 004 — caja en ejecución con ventana real | Apertura, cobro efectivo, historial y cierre correctos; revisión práctica de "escala, teclado, ratón y desconexión" | El usuario confirmó: «Realmente todo está bien escala teclado ratón y desconexión todo anda en orden» (aceptación local registrada en `docs/validacion-004.md`); recorrido en `elcarbonazo_demo_local`: pedido ficticio de mesa 1 → cocina → apertura con fondo cero → cobro efectivo → histórico → cierre → mesa `Libre`, pedido `Pagado`, turno `Cerrado`, diario `SinPendientes`; recorrido de grupo con tarjeta "con confirmación externa simulada" (sin validez bancaria ni fiscal) (`validacion-004.md`) | Aprobado |
| CP-52 | RF-10/11 / RNF-07 | Aceptación 005 — inventario en ejecución | Revisiones de inventario correctas en pantalla | El usuario confirmó: «las revisiones [...] están bien»; cierre C4 con 141 API/262 Desktop y smokes (`validacion-005.md`) | Aprobado |
| CP-53 | RF-03 (roles) | Consultas de Cocina sin habilitar acciones de mesa; Jefa no libera/cierra con cuenta pendiente | Los permisos se reflejan en la UI y en la API | "El usuario verificó visualmente con el anfitrión temporal que Cocina puede consultar sin habilitar acciones de mesa o agrupación, y que Jefa no puede liberar la mesa ni cerrar el grupo preparados con pedidos pendientes" (`validacion-t5.md`) | Aprobado |
| CP-54 | RF-12 | Revisión visual final de la vista de reservaciones | Pantalla verificada en ejecución (agenda, crear, cancelar, estados) | **No ejecutada aún**: tareas de API y Desktop marcadas como completadas en `specs/006-reservaciones-mesas/tasks.md` (migración `20261117012501_AddReservaciones`, aplicada a `elcarbonazo` con autorización del usuario) y regresión de 163 API/267 Desktop aprobada; la única tarea abierta del módulo es la "revisión visual de la vista real" | No aprobado |
| CP-55 | RF-13 | Validación visual del reporte de ventas y documentación explícita de impuestos = 0/no fiscal | Pantalla verificada en ejecución | **No ejecutada aún**: tareas de API y Desktop marcadas como completadas y regresión de 163 API/267 Desktop aprobada; la única tarea abierta es "validar UI visualmente y documentar explícitamente que impuestos = 0/no fiscal" (`specs/007-reporte-ventas/tasks.md`) | No aprobado |
| CP-56 | RNF-07 | Confirmación visual del botón Cocina con su nuevo estilo de navegación | Botón visible con estilo igual al elemento activo de Mesas | **Pendiente** (cambio incorporado después de la revisión D5; su apariencia final se solicitó confirmar durante D6 y no consta confirmación) | No aprobado |

**Resumen de la matriz:** 53 casos `Aprobado` (incluidos 2 `Aprobado*` de avance parcial del módulo pausado y 2 `Aprobado¹` con revisión visual declaradamente pendiente) y 3 casos `No aprobado`; los 3 se corresponden **exclusivamente con revisiones visuales pendientes**, no con fallos de funcionamiento: no hay ningún caso funcional o de validación fallado en las suites ni en las validaciones registradas.

**Evidencia citada por matriz:**
- **A:** `tests/ElCarbonazo.Desktop.Tests/` (LoginViewModelTests, SessionBehaviorTests, SessionRaceTests, MesasPollingLifecycleTests, MesasApiServiceTests, PedidosSalonViewModelTests, ProductosPedidoApiServiceTests, CocinaViewModelTests, CocinaApiServiceTests, CajaRecoveryCoordinatorTests, CajaRecoveryTransportTests, InventarioLecturaTests, ReservacionesReporteViewModelTests), `tests/ElCarbonazo.Api.Tests/` (PedidosPersistenceTests, ApiFlowTests, CajaOperacionesTests, CajaCierreCarrerasTests, InventarioServiceTests, InventarioEnviosTests, ReservacionesAndReporteTests, AnulacionAutorizacionTests, AnulacionEjecucionTests, AnulacionRecuperacionTests, HmacProvisionPersistenceTests), `tests/ElCarbonazo.AcceptanceHost` (`--pedidos-smoke`, `--caja-smoke`, `--inventario-smoke`), `docs/validacion-t5.md`, `validacion-t6.md`, `validacion-d5.md`, `validacion-004.md`, `validacion-005.md`, `validacion-006.md`.
- **B:** `tests/ElCarbonazo.Api.Tests/` (ApiFlowTests, PedidosPersistenceTests, InventarioEnviosTests, CajaPersistenceTests, InventarioPersistenceTests, CancelacionesPersistenceTests, CajaOperacionesTests), `src/ElCarbonazo.Api/Security/LoginSecurityPolicy.cs`, `src/ElCarbonazo.Api/Security/LoginRateLimitAuditGate.cs`, `src/ElCarbonazo.Api/Program.cs` (rate limiter y middleware), `docs/bitacora-errores-y-lecciones.md` (L-02, L-04, verificación `NaN`).
- **C:** `tests/ElCarbonazo.Desktop.Tests/` (MesasRequestCoordinatorTests, PedidosSalonViewModelTests, CocinaViewModelTests, InventarioEscrituraCoordinatorTests, CajaRecoveryCoordinatorTests, TransportAndClockTests, SessionBehaviorTests), `tests/ElCarbonazo.Api.Tests/` (CajaCierreCarrerasTests, CajaOperacionesTests, EnviosConcurrencyTests, InventarioEnviosTests, ReservacionesAndReporteTests), `tests/ElCarbonazo.AcceptanceHost` (`--cadencia` y smokes), `docs/validacion-t5.md`, `validacion-004.md`, `docs/bitacora-errores-y-lecciones.md` (ERR-005, ERR-008, L-12).
- **D:** `docs/validacion-t5.md`, `validacion-t6.md`, `validacion-d4.md`, `validacion-d5.md`, `validacion-d6.md`, `validacion-004.md`, `validacion-005.md` y los `tasks.md` de `specs/006-reservaciones-mesas` y `specs/007-reporte-ventas`.

---

## 4. Ejecuciones documentadas de los entornos integrados (AcceptanceHost)

Registro de las ejecuciones registradas de cada modo, todas con base PostgreSQL temporal `elcarbonazo_test_<uuid>`, API en HTTP loopback (127.0.0.1) y limpieza de recursos al terminar:

| Ejecución | Fecha documentada | Alcance verificado | Fuente |
|---|---|---|---|
| `--caja-smoke` (cobro efectivo) | 13/11/2026 | Turno abierto, cuentas, **2 cobros en efectivo** con total/recibido/vuelto y estado pagado; en el segundo cobro se **descartó deliberadamente** la respuesta HTTP exitosa y la consulta durable con la conciliación despejaron el diario sin repetir la ejecución | `validacion-004.md` |
| `--caja-smoke` (cobros mixtos) | Sin fecha explícita (historial del cierre 004; suite de 220 casos Desktop) | Efectivo + tarjeta + transferencia; referencia de tarjeta repetida → rechazo durable + segunda confirmación; cierre con totales C$20 tarjeta, C$10 transferencia, C$20 efectivo | `validacion-004.md` |
| `--caja-smoke` (recuperación) | Tarea C2 de 004 (sin fecha explícita) | Reconstruyó almacenamiento/coordinador/transporte/ViewModel de Desktop tras perder la respuesta del cobro (reinicio de Desktop **simulado por reconstrucción**, no reinicio de proceso); recuperación y conciliación **sin repetir ejecución**; aparte, una prueba HTTP reinició el host API real tras apertura confirmada y verificó una sola apertura durable | `validacion-004.md` |
| `--pedidos-smoke` | Cierre D6 (9/11/2026) | Cuenta individual y de grupo, envío/repetición idempotente, importes, transición hasta `Lista` y lectura de cuenta | `validacion-d6.md` |
| `--inventario-smoke` | Cierre 005 C4 (sin fecha explícita) | Creación, entrada, edición, kardex y receta por ViewModel + REST + base temporal; recorrido integrado mesa+grupo: pedido → cocina → cobro → comprobante → liberación/cierre **sin segundo descuento** | `validacion-005.md` |
| `--catalogo-defensa-smoke` | Fase 1 `entrega-defensa-local` (sin fecha explícita) | Catálogo de defensa (90 productos / 116 ingredientes / 90 recetas / 452 líneas) verificando pedidos individual y grupo, Cocina, Caja y ausencia de doble consumo; el primer intento completo de la API agotó cinco minutos y la repetición pasó | `specs/entrega-defensa-local/tasks.md`, bitácora |
| `--cadencia` | 5/11/2026 | 6 GET reales sobre VM+servicio Desktop contra API temporal: intervalos entre inicios 2007,4–2015,2 ms (2012,7; 2013,7; 2007,4; 2013,0; 2015,2); duraciones de GET 6,6–37,4 ms; máximo de GET simultáneos: uno (ver CP-45) | `validacion-t5.md` |

**Límite declarado de estos entornos:** usan sesión adaptada, no abren ventana, no prueban render, teclado, ACL, LAN ni TLS reales, y una interrupción forzada puede dejar recursos temporales que deben identificarse antes de limpiarse.

---

## 5. Incidencias encontradas y correcciones realizadas

Fuente: `docs/bitacora-errores-y-lecciones.md` (21 entradas ERR-001…ERR-021 y 15 lecciones confirmadas sin número). El estado de cada entrada se transcribe de su propia cabecera.

### 5.1 Incidencias ERR (síntoma → causa → corrección → prevención/evidencia)

| Código | Área | Estado | Síntoma / hallazgo | Causa | Corrección aplicada |
|---|---|---|---|---|---|
| ERR-001 | UI (mesas) | Corregido, confirmado visualmente por el usuario | Animación de hover no se observaba aunque el XAML compilaba | El selector dependía de una relación con la plantilla del botón que no llegaba a la tarjeta | Estado visual aplicado al borde de la tarjeta + plantilla propia para el botón de mesa |
| ERR-002 | UI (mesas) | Corregido, colores confirmados | Texto de botones ilegible sobre fondo oscuro | La plantilla heredada imponía otro color al contenido | Tema propio para los botones del mapa con estados normal/hover/foco/deshabilitado ("Carbón & Fuego") |
| ERR-003 | UI (mesas) | Corregido (con prueba) | La confirmación de liberación no era identificable | La opción no era visible en el flujo | Confirmación en cuadro central con acciones explícitas cancelar/confirmar; cobertura `ReleaseRequiresConfirmationAndUsesCurrentVersion` |
| ERR-004 | Sesión (Desktop) | Corregido con regresión | `NullReferenceException` en refresh rechazado en vez de "sesión expirada" | La tarea de refresh podía completarse síncronamente y soltaba el campo compartido antes de guardar la referencia | Conservar la tarea en local antes de limpiar el campo; pruebas de completado síncrono/diferido y consumidores concurrentes |
| ERR-005 | Concurrencia (mesas) | Corregido con pruebas | La mutación podía iniciar antes de que terminara el GET previo | Invalidar la generación no cancela ni espera la solicitud física | El coordinador reserva la mutación, invalida y **espera** el fin del GET; no envía si falla o cambia la sesión |
| ERR-006 | Transporte (mesas) | Corregido y probado | El timeout no cubría la deserialización JSON | El plazo terminaba antes del procesamiento del cuerpo | El GET de mesas usa un token que abarca solicitud **y** deserialización; verificado con cancelación posterior |
| ERR-007 | UI/lógica (grupos) | Corregido y probado | Un rechazo de cierre de grupo mostraba «Grupo cerrado» | El código de éxito no verificaba el resultado real | El mensaje de éxito solo se aplica ante éxito real; el rechazo pide reconciliación (`GroupFailureIsNotRetriedAndRequiresReconciliation`) |
| ERR-008 | Entorno (API/Desktop) | Causa identificada, corregida | Login sin conexión a la API | La API arrancó en perfil HTTP (5261) y Desktop apuntaba a HTTPS (7191) | Iniciar la API con el perfil que coincide con Desktop; no desactivar TLS; dejarlo en las instrucciones de arranque |
| ERR-009 | Medición | Aclarado; medición E2E pendiente | Los "47 valores" se interpretaron como tiempo de render | El instrumento solo medía la duración HTTP del PUT | Se documentan por separado: PUT (9–53 ms), cadencia medida (`--cadencia`) y estimación visual 1–1,5 s, cada una con sus límites |
| ERR-010 | Herramienta de medición | Corregido en la herramienta | Un 401 cerraba el anfitrión con excepción | Se asumía sesión autorizada permanente | El anfitrión informa el código y pide acción humana para volver a iniciar sesión; 5xx/transporte tras envío = resultado potencialmente incierto |
| ERR-011 | UI (ventana) | Ajustado; **pendiente confirmación del usuario** en su configuración/escalado | Barra clara y maximize/restaurar dudoso | Plantilla `GlassCard` de SukiUI; interceptación de hit-test de SukiUI | Botón propio con rol `User`, fondo oscuro, iconos reducidos; verificado localmente maximizar/restaurar por eventos de sistema |
| ERR-012 | Concurrencia (API) | Corregido y probado (ambas intercalaciones, conexiones independientes) | Liberar mesa consultaba cuentas pendientes **fuera** del bloqueo | La lectura previa a la transacción permitía abrir la cuenta después | La mutación inicia transacción y bloquea la fila **antes** de comprobar cuentas; el envío D2 toma el mismo bloqueo (`EnviosConcurrencyTests`) |
| ERR-013 | Seguridad | Corregido con regresión | El token conservaba el rol anterior tras cambio de rol/inactividad | No se revalidaba usuario/rol por petición | Cada solicitud autenticada comprueba usuario activo y rol conforme al claim; regenera el token si hay discrepancia |
| ERR-014 | Datos (catálogo) | Corregido; regresión agregada | Catálogo demo duplicado aceptado como idéntico | Se comparaban conteo y permisos, no nombres distintos | Exigir todos los nombres distintos antes de aceptar segunda ejecución; `Duplicate_demo_name_with_missing_product_is_rejected_without_changes` |
| ERR-015 | Contrato (pedido) | Corregido con prueba Desktop | `{}` confundido con `{"pedido":null}` (cuenta vacía) | El default `null` no distinguía ausencia de campo de campo explícitamente nulo | Exigir el campo `pedido` explícito para aceptar; ante ausencia: respuesta inválida y sin habilitar escritura |
| ERR-016 | Incertidumbre (envío) | Corregido con fallo inyectado | Un fallo inesperado (IOException) podía salir **sin** marcar la intención incierta | Se enumeraban solo ciertos tipos de excepción | Cualquier excepción no tratada tras el inicio conserva la intención y bloquea las acciones conflictivas (`Unexpected_response_io_failure_is_uncertain_and_cannot_create_new_intention`) |
| ERR-017 | UI (pedidos) | Diagnóstico mejorado; **revisión visual pendiente** | «Sin datos actuales» y botón deshabilitado poco distinguible | El VM agrupaba errores HTTP distintos en un solo mensaje; prioridad de estilo sobre deshabilitado | Diferenciación 401/403/404/5xx/datos inválidos sin filtrar internos + estilo `Button.primario:disabled` explícito |
| ERR-018 | Rendimiento (EF) | Consulta identificada; estrategia declarada; **impacto no medido** | Dos `MultipleCollectionIncludeWarning` en `--pedidos-smoke` | `CuentasService.GetActivaAsync` carga la jerarquía de comandas/detalles en una consulta | Se mantiene `AsSingleQuery()` + `RepeatableRead` sin prometer mejora; cualquier división se evalúa con datos representativos antes de cambiar |
| ERR-019 | Proceso de validación | Aclarado y documentado | Se pidió verificar una transición de Cocina desde una vista aún no implementada (D4) | Se confundió la API D3 con la pantalla KDS D5 | Se restableció el paso para D5; prevención: contrastar cada paso manual contra `tasks.md` y contra la UI implementada |
| ERR-020 | KDS (polling) | Corregido con prueba automatizada | El polling de KDS seguía consultando durante la pausa de sesión | El VM publicaba la pausa pero el ciclo no la observaba | Cocina suspende el polling en `Paused`, reanuda ante sesión activa y detiene al cerrar (`Paused_session_blocks_mutation_and_recovers_only_after_read`) |
| ERR-021 | Esquema/base (pedidos) | Causa confirmada; migración según destino | Pedidos sin catálogo/cuenta vía HTTPS: `42P01` relación `producto` ausente | El proceso API estaba conectado a una base sin la migración D1 de 003 | No se aplicó migración sin autorización: confirmar nombre exacto de base, entorno y destino; respaldo si hay datos; `dotnet ef database update` con la configuración de esa API (límite documentado en D6) |

### 5.2 Lecciones confirmadas (registros sin número ERR, inicio de la bitácora)

| # | Hecho observado | Control aplicado |
|---|---|---|
| L-01 | El código vigente dependía de la migración 006 sin aplicarse a la base local (`EnviosService` escribe en `consumo_detalle`, creada por esa migración); la migración de reservaciones sigue a 006 en el historial EF | Aplicación a `elcarbonazo` **con autorización explícita** del usuario; SQL revisado sin `DROP`/`TRUNCATE`/eliminación de pedidos; verificación del catálogo solo con base temporal antes de cargar |
| L-02 | La agenda de reservaciones mostraba canceladas con botón «Cancelar» | Listado únicamente de reservaciones activas (límite 500); los canceladas permanecen en base con auditoría; Desktop filtra por estado |
| L-03 | Los cambios de ocupación/incertidumbre del inventario no notificaban `PuedeEscribir` y las lecturas posteriores sustituían mensajes de operación | Notificación de la propiedad, conservación del mensaje y bloqueo de escritura ante resultado incierto ("una consulta no demuestra que la escritura anterior terminó") |
| L-04 | `NaN::numeric > 0` es verdadero en PostgreSQL | Los nuevos CHECK de cantidad de snapshot e importe de anulación rechazan explícitamente `NaN` (probado); las columnas anteriores **no** están acreditadas |
| L-05 | El CHECK de resultado terminal aceptaba JSON no-objeto o status incompatible | Modelo y migración exigen JSON de objeto, código no vacío, fecha coherente y rango HTTP por estado; triggers impiden reescribir terminales y deshacer consumo de autorización |
| L-06 | Al añadir columnas de 006, dos pruebas históricas fallaban con el modelo EF actual | Los datos previos se habían construido con contexto de prueba que ignora las propiedades posteriores; **no** se editan migraciones antiguas |
| L-07 | Consulta del histórico de comprobantes falló (`42P01`, relación `cobro` ausente en esa instancia) | Confirmar instancia, destino y migraciones antes de modificar bases; la API no migra sola; no tablas manuales |
| L-08 | El detalle histórico hacía dos GET del mismo comprobante y reutilizaba una aplicación que invalidaba selección y turno | Validar la respuesta una sola vez; aplicar el historial sin tocar la revisión del cobro; descalificar al cambiar la sesión |
| L-09 | El JSONB puede reproducir un resultado con propiedades reordenadas o espacios distintos | Comparar semánticamente el JSON al verificar reproducción; no se asume igualdad textual |
| L-10 | El lector de caja descartaba `huellaLineas`, por lo que la fotografía no podía construir el contrato de cobro | El lector conserva y valida la huella y exige los campos esenciales de la cuenta cobrable; la API revalida bajo bloqueo |
| L-11 | La identidad del actor (solo ID de usuario) no distinguía dos sesiones del mismo usuario | Generación local que cambia al iniciar sesión nueva; se verifica que no cambió al recibir la respuesta; el refresh no la modifica |
| L-12 | La preparación/ejecución de apertura esperaban el lock global después de verificar el rol, sin revalidar | Tras adquirir el lock se revalida sesión y rol; el lock se toma fuera del savepoint de dominio |
| L-13 | `SendHumanAsync` reintenta tras refresh, comportamiento incompatible con las mutaciones recuperables de caja | Caja usa `SendCajaOnceAsync`: captura el token de la identidad esperada, **no** refresca ni reintenta; prueba con HTTP ficticio verifica un solo envío |
| L-14 | Una carpeta temporal con ACL restringida era rechazada por reglas heredadas del ancestro | Verificar propietarios de todos los ancestros; ignorar ACE de solo herencia solo en esa evaluación; rechazar permisos efectivos de borrado/eliminación/cambio de propiedad; sin exigir otra cuenta real por decisión del usuario (acceso multi-cuenta **sin** validar) |
| L-15 | La lectura MVCC no bloquea inserciones futuras; un CHECK aceptaba resultados `UNKNOWN` por NULL | Prepare de cobro y cierre comparten barrera transaccional y revalidan bajo ella (ambos órdenes probados); el CHECK exige tipo y versión no nulos; "no usar una consulta sin coordinación como prueba de ausencia" |

**Distribución de estados de incidencias:** corregidas con regresión automatizada: ERR-001, 002, 003, 004, 005, 006, 007, 010, 012, 013, 014, 015, 016, 020. Causa confirmada con corrección de proceso/entorno: ERR-008, 019, 021. Ajustadas en herramientas/estrategia con medición pendiente: ERR-009, 018. Ajustadas con confirmación final pendiente del usuario o revisión visual: ERR-011, 017.

---

## 6. Resultados finales

### 6.1 Regresión final del proyecto (estado a 17/11/2026)

| Puerta | Resultado |
|---|---|
| `dotnet format ElCarbonazo.sln --verify-no-changes` | Correcto |
| `dotnet build ElCarbonazo.sln` | Correcto, **0 advertencias, 0 errores** (advertencias tratadas como errores) |
| Pruebas API (`ElCarbonazo.Api.Tests`) | **163–165 aprobadas** según la corrida registrada (163 en `tasks.md` de reservaciones/reporte; 165 tras `--catalogo-defensa-smoke` en `specs/entrega-defensa-local/tasks.md`) |
| Pruebas Desktop (`ElCarbonazo.Desktop.Tests`) | **267–268 aprobadas** (mismas fuentes) |
| Auditoría NuGet (`--vulnerable --include-transitive`) | Sin vulnerabilidades reportadas por los orígenes consultados |
| Smokes de AcceptanceHost | `--caja-smoke`, `--pedidos-smoke`, `--inventario-smoke` y `--catalogo-defensa-smoke` registrados como ejecutados en sus momentos respectivos (con los límites del §4) |

### 6.2 Cobertura de la matriz por nivel (síntesis)

- **Funcional:** 14 requerimientos RF ejercitados; todos cubiertos con prueba automatizada **y** aceptación del usuario, salvo RF-12 y RF-13 (automatizadas; revisión visual pendiente — CP-54/55) y RF-14 (módulo pausado, avance parcial probado — CP-17/18).
- **Validación:** 12 casos (CP-19…CP-30) de datos válidos e inválidos y límites, todos Aprobados; mecanismos verificados: validación HTTP 400 problem+json con errores por campo, bloqueo de login (5 fallos → 5 min) y rate limit (429 con `Retry-After`), restricciones de esquema, idempotencia, referencias duraderas y protección de diario.
- **Integración:** 15 casos (CP-31…CP-45) entre componentes (API↔base, Desktop↔API, caja⇄envío⇄cocina⇄inventario), todos Aprobados, con bloqueos observados en PostgreSQL temporal.
- **Aceptación:** 11 casos (CP-46…CP-56): 8 Aprobados en el perfil universitario local y 3 No aprobados, **exclusivamente pendientes de revisión visual**, registrados como pendientes en los `tasks.md` de sus módulos.

### 6.3 Lo que estas pruebas **no** acreditan (declaración obligatoria)

1. No son prueba física LAN ni aptitud para producción (perfil vigente: demostración universitaria local).
2. No acreditan medición cronometrada de render/convergencia (solo la estimación visual de 1–1,5 s y las duraciones HTTP de PUT, documentadas con sus límites).
3. No ejecutaron medicción cuantitativa de cobertura de código; el umbral ≥80 % permanece como objetivo previo a producción.
4. No validan verificación bancaria de tarjeta/transferencia ni validez fiscal de los comprobantes (estos son internos).
5. No validan acceso multi-cuenta a los archivos de diario (decisión de no exigir otra cuenta real en la demostración) ni instalador empaquetado.
6. No cubren la matriz completa de carreras de caja (C5) ni la totalidad de intercalaciones de caja con anulaciones (módulo pausado).
7. No reemplazan la validación visual pendiente (CP-54, CP-55, CP-56 y el checklist completo de foco/teclado/escalado).

---

## 7. Reproducibilidad de las pruebas

```powershell
# Desde la raíz del repositorio
dotnet format ElCarbonazo.sln --verify-no-changes --no-restore
dotnet build ElCarbonazo.sln
dotnet test tests/ElCarbonazo.Api.Tests/ElCarbonazo.Api.Tests.csproj --no-build --no-restore
dotnet test tests/ElCarbonazo.Desktop.Tests/ElCarbonazo.Desktop.Tests.csproj --no-build --no-restore
dotnet list ElCarbonazo.sln package --vulnerable --include-transitive

# Anfitrión de aceptación (fuera de la solución; primero el build Debug de la API)
dotnet build tests/ElCarbonazo.AcceptanceHost/ElCarbonazo.AcceptanceHost.csproj
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --pedidos-smoke
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --caja-smoke
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --inventario-smoke
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --catalogo-defensa-smoke

# Entornos manuales (mismos requisitos; dejan el entorno abierto hasta Ctrl+C)
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --pedidos
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --cadencia
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --medir
```

Notas de ejecución: las pruebas API requieren PostgreSQL con permiso de crear/eliminar bases (`ELCARBONAZO_TEST_ADMIN_CONNECTION` o la conexión de Development) y **siempre** trabajan sobre su propia base `elcarbonazo_test_<uuid>`; ninguna prueba destructiva debe apuntarse a una base existente. Un intento fallado del anfitrión puede dejar recursos temporales: identificar su origen antes de limpiarlos. No se imprimen ni se conservan en este documento secretos (contraseñas, tokens, cadenas de conexión), conforme a las reglas del proyecto.