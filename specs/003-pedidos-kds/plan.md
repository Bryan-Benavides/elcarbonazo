# Plan técnico — 003 Pedidos y KDS

**Estado: D0, D1 y API de D2/D3 completos; D4 de salón aceptada localmente.** D5 incorpora cola y transiciones KDS Desktop con pruebas automatizadas; la revisión visual y la integración de extremo a extremo siguen pendientes. Hay casos Unicode compartidos de API y Desktop. No se ha aplicado la migración a una base persistente.

## 1. Arquitectura y límites

- Reutilizar proyectos y versiones fijadas: API ASP.NET Core/.NET 10, EF Core/Npgsql; Desktop Avalonia 12/SukiUI/MVVM y DTOs propios. Controllers traducen HTTP y delegan en servicios; no acceden directamente a EF. Desktop y sus pruebas no referencian API, EF ni Npgsql.
- `SessionManager` sigue siendo dueño de tokens, sesión, TLS, actividad y navegación de seguridad. Una consulta periódica no constituye actividad ni renueva sesión por sí sola.
- La API es autoridad de validación, permisos, precio, estado, impuestos y auditoría. No confiar en disponibilidad, totales, rol ni versiones afirmadas por el cliente.
- Ningún nuevo paquete o cambio de versión se presupone. Si hiciera falta, justificar compatibilidad y aprobarlo por separado.

## 2. Modelo propuesto

### 2.1 Base actual y preservación

`pedido` ya existe con FK restrictivas, XOR, `NUMERIC(12,2)`, CHECK de importes y estados, e índices únicos parciales para pedidos activos por mesa o grupo (`AppDbContext`). La migración nueva debe preservar filas, claves, índices, secuencias y permisos; no modificar migraciones históricas ni depender de que la base esté vacía. La entidad actual no tiene detalles ni versión explícita expuesta. Aunque el diccionario de dominio enumera `producto`, `detalle_pedido` y `factura`, esas tablas no forman parte del esquema EF actual; confirmar físicamente la base solo mediante inventario autorizado de lectura.

### 2.2 Entidades nuevas de D1

| Entidad | Responsabilidad | Datos mínimos propuestos |
|---|---|---|
| `producto` | Catálogo de demostración; no inventario | ID, nombre, categoría, precio NIO, disponible, versión monotónica explícita |
| `comanda` | Una entrega inmutable de líneas a cocina, no la cuenta | ID, pedido, usuario emisor, fecha UTC del servidor, estado KDS, versión de concurrencia |
| `detalle_pedido` | Línea confirmada de una comanda | ID, comanda, producto FK restrictiva, nombre histórico, cantidad, precio unitario histórico, subtotal, notas limitadas |
| `envio_pedido` | Deduplicar un intento lógico y reproducir confirmación | `envioId` único, usuario, hash semántico, referencia a comanda, resultado/status/location confirmados |

Diseño D1: `producto` usa `id_producto` serial, nombre hasta 120, categoría, `precio NUMERIC(8,2)`, `disponible` y `version integer` positiva incrementada automáticamente por trigger al cambiar nombre/categoría/precio/disponibilidad; `comanda` usa `id_comanda` serial, `id_pedido`, `id_usuario` emisor, instante UTC, estado y el `xmin` nativo de PostgreSQL como token (no crear una columna DDL `xmin`); `detalle_pedido` usa `id_detalle` serial, FK a comanda y producto, nombre/precio históricos, `cantidad smallint`, subtotal `NUMERIC(12,2)` y nota NFC de hasta 150 escalares (vacía si no se ingresó); `envio_pedido` usa `envio_id UUID` único, emisor, hash hexadecimal SHA-256, FK única a comanda, status/location/JSON de respuesta original y fecha UTC. No se fusionan líneas repetidas. Todas las FK históricas son `RESTRICT`; constraints comprueban categorías, estados, rangos de precio/cantidad, NFC y aritmética del detalle. Índices cubren cola activa FIFO, comandas por cuenta, detalles por comanda y catálogo por disponibilidad/categoría.

Cada línea del request se conserva independiente, aunque repita producto y nota; no se fusiona ni elimina automáticamente. Las líneas confirmadas no se editan ni eliminan desde 003. `producto.disponible` no es stock. El catálogo de demostración se carga por herramienta explícita, idempotente y limitada a base temporal o entorno de demostración autorizado; no `HasData` con precios aplicados automáticamente en bases existentes.

La cuenta conserva fecha, responsable original y destino; cada comanda conserva su propio emisor. Las FK conservan historial (`RESTRICT`). La ausencia de línea `producto` existente, categorías incompatibles o pedido heredado sin comandas no se corrige inventando datos. La política aprobada para una cuenta activa heredada sin comandas es conservarla intacta, mostrarla como «cuenta anterior sin detalle disponible» solo lectura y bloquear nuevos envíos a su destino. No se crea comanda vacía ni se altera estado/precio/fecha/actor. La resolución requiere una operación futura explícita y auditable; no se incluirá botón de cerrar/pagar en 003. Pedidos heredados pagados/cancelados permanecen intactos; su consulta histórica queda fuera del MVP. Diseñar inventario no destructivo y precondiciones de migración: si una fila o estructura impide preservar reglas, abortar transaccionalmente con diagnóstico sin datos sensibles ni pérdida de datos.

### 2.3 Estados y agregación

Estados aprobados de comanda: `Abierta` (enviada/recibida y pendiente de iniciar), `En Preparación`, `Lista`; transiciones solo `Abierta` → `En Preparación` → `Lista`. La versión de concurrencia de una comanda usa `xmin` como token opaco de esa fila, siguiendo `Mesa`; nunca se ordenan tokens entre filas ni se tratan como reloj. Estado agregado aprobado para cuenta activa: `Abierto` si hay al menos una comanda abierta; de otro modo `En Preparación` si existe una en preparación; de otro modo `Listo` si todas están listas. Un envío nuevo reabre la cuenta a `Abierto` sin reiniciar comandas previas. No aplicar el cálculo a cuentas heredadas sin comandas: mostrarlas usando explícitamente el estado anterior como cuenta de solo lectura y bloquear envío al destino.

La cuenta se bloquea durante creación y transición de comandas para serializar el recálculo. No marcar `Pagado`/`Cancelado` ni liberar destino desde KDS. El índice de cuenta activa debe seguir protegiendo todos los valores activos, incluidos `Listo`; comprobar que una actualización concurrente no abre un hueco sin protección. El diseño final debe asegurar que toda ruta de escritura de estado aplique las mismas invariantes; una comprobación solo en UI es insuficiente.

### 2.4 Importe y datos validados

Regla fiscal aprobada para el alcance 003, basada en la información del usuario de que el negocio opera bajo cuota fija: no añadir ni desglosar IVA 15 %. Precios NIO con dos decimales, `subtotal` como suma de cantidades × precio histórico, `impuestos = 0` y `total = subtotal`, con `NUMERIC(12,2)` conforme al esquema existente. El sistema no presenta pedido como factura ni determina cumplimiento fiscal. Si el negocio informa un cambio de régimen, revisar con el responsable/asesor competente y aprobar una especificación/cambio antes de alterar cálculos.

Límites iniciales aprobados: máximo 50 líneas por envío, cantidad 1–99 por línea, precio unitario C$0.01–C$999,999.99, total máximo `NUMERIC(12,2)` (C$9,999,999,999.99), notas hasta 150 valores escalares Unicode después de NFC y body JSON máximo 64 KiB. Rechazar cero/negativos, decimales en cantidad, fuera de rango y overflow; validar el conteo antes de cargar productos. Cambiar estos límites requiere revisar especificación y pruebas.

El límite de nota cuenta valores escalares Unicode después de normalización NFC (no bytes UTF-8 ni unidades UTF-16; un par sustituto cuenta como un valor, una secuencia con marcas puede contar más de uno). API normaliza y persiste el mismo texto NFC; Desktop/API comparten pruebas de combinaciones y emoji. Rechazar controles no imprimibles salvo tabulación si el componente la admite; nunca registrar el contenido en auditoría técnica. El subtotal es la suma exacta `cantidad × precio histórico` por línea, escala 2, sin cálculo de impuesto; `impuestos=0`, `total=subtotal`. Ejemplos: 2 × C$10.10 = C$20.20; C$20.20 + C$0.80 = C$21.00. El nombre y precio histórico se capturan del catálogo dentro de la transacción.

Controlar cambios concurrentes de precio/disponibilidad con versión monotónica explícita de producto, revalidada bajo lock durante el envío. Una versión obsoleta devuelve `409 CATALOGO_CAMBIADO` sin persistir parcialmente; la respuesta incluye solo IDs y metadatos actuales necesarios para que la persona revise y cree una nueva intención con nuevo `envioId`. No se sustituye precio ni disponibilidad silenciosamente.

### 2.5 Idempotencia de envío — línea base aprobada

- `envioId` es UUID v4 creado por Desktop una sola vez al confirmar y permanece asociado al body inmutable durante reintentos/reconciliación de esa intención. No se reutiliza para una edición, cambio de destino, líneas, notas, cantidad ni versión de catálogo.
- Antes de bloquear destino/cuenta, serializar solicitudes del mismo `envioId` con un advisory lock PostgreSQL transaccional derivado de forma estable del UUID; mantener además índice `UNIQUE` sobre el UUID como autoridad durable. La colisión del hash de advisory solo serializa solicitudes no relacionadas y no afecta corrección. Tras adquirir el lock, buscar la clave: reproducir resultado si actor/hash coinciden o devolver conflicto si no; solo si no existe continuar.
- Persistir en tabla nueva con índice `UNIQUE`: `envioId`, `id_usuario`, hash SHA-256 de la solicitud semántica canónica, referencias a pedido/comanda creados y datos suficientes para reproducir la respuesta confirmada. FKs restrictivas. No guardar token ni notas en auditoría; el detalle confirmado ya conserva los datos de la comanda.
- El hash se calcula sobre una serialización UTF-8 canónica del contrato validado, no sobre bytes JSON arbitrarios: campos en orden fijo, números con formato invariante, destino/versiones/líneas incluidos, notas NFC y orden de líneas conservado; `envioId` excluido y actor asociado aparte. API canonicaliza; casos de prueba compartidos API/Desktop garantizan igualdad.
- Insertar el registro de resultado idempotente una vez creados sus destinos FK, pero antes del commit; registro, cambios de pedido/comanda/detalles/mesa, auditoría y resultado durable quedan dentro de la misma transacción. Un rollback libera el advisory lock y no deja clave consumida; un commit confirmado antes de perder respuesta deja clave y resultado recuperables. Reproducir solo tras autenticación y autorización actual; la clave no es secreta ni sustituye permisos.
- Mismo actor + ID + hash devuelve resultado original sin mutar. Mismo ID con otro hash devuelve `409 ENVIO_ID_REUTILIZADO` sin revelar resultado previo. Si peticiones idénticas compiten mientras la primera está abierta, la restricción/tx serializa: una comanda como máximo; la segunda devuelve resultado comprometido o conflicto transitorio que permita reconciliar, nunca crea una segunda.
- No caducar/limpiar claves mientras destino e historial existan; no definir retención destructiva en 003. La limpieza futura requiere diseño que pruebe que reintentos antiguos no vuelven a crear comandas.
- Desktop no repite automáticamente tras timeout/red/5xx. Durante la sesión conserva clave+body para una acción humana explícita de repetir esa solicitud. Tras cierre/reinicio no promete recuperar una intención solo en memoria; no declarar que la clave del servidor hace recuperable una clave que el cliente perdió. No usar un GET como prueba de terminación.
- Pruebas: repetición secuencial; mismo ID con body/actor distinto; dos conexiones sincronizadas con misma clave; timeout después del commit y respuesta perdida; rollback antes del commit; fallo de auditoría; reinicio de API; autorización revocada al reproducir; comprobar una comanda y mismo resultado.

## 3. Transacciones y concurrencia

1. Fijar y probar orden global de locks compatible con `GruposMesaService`: reservar/verificar primero la clave idempotente (sin mantener locks de destino antes de resolver una clave ya existente); para grupo, **grupo → mesas miembro en orden ascendente → pedido → comanda → productos en orden ascendente**; para mesa individual, mesa → pedido → comanda → productos en orden ascendente. Solo tomar recursos necesarios, pero nunca invertir el orden al compartirlos. Evaluar en diseño los locks reales de FK/índices y rutas 001; documentar cualquier ajuste antes de implementar.
2. Un envío a grupo bloquea grupo activo y mesas de ese grupo, luego cuenta y productos; revalida pertenencia y existencia de cuenta. Para destino individual bloquea mesa, comprueba que no pertenezca a grupo y que su estado permita la apertura (`Libre` o `Reservada`); `Ocupada` sin cuenta activa requiere conflicto y revisión, no asociar pedido silenciosamente. Primera cuenta + primer envío + ocupación + auditoría van en la misma transacción. Un índice único parcial complementa, no sustituye, los locks y validaciones.
3. El cambio KDS necesita serializar con envíos y cierre: identificar destino sin asumir que la primera lectura es vigente, adquirir locks en orden global, volver a cargar y revalidar cuenta/comanda y versión bajo lock antes de escribir. No bloquear primero la comanda y después buscar/bloquear el pedido si otra ruta usa el orden contrario. Evitar mantener locks durante tráfico HTTP a terceros.
4. Cambiar comanda, recalcular estado de pedido y auditar atómicamente. Usar tokens de concurrencia como igualdad, no relojes. Pruebas con conexiones PostgreSQL independientes, barreras deterministas y dos terminales; tratar bloqueo, cancelación, deadlock y rollback sin prometer éxito.
5. No suponer que un GET posterior cierra la carrera de una escritura incierta. Ante fallo de commit/respuesta no inventar rollback o éxito. Mantener bloqueo conservador y advertencia. La propuesta idempotente de §2.5 reduce duplicados solo si Desktop conserva la misma clave y body; no garantiza recuperación de una intención perdida al cerrar la aplicación ni permite declarar un timeout como éxito/fallo.

## 4. Contratos HTTP — línea base aprobada

JSON camelCase y JWT vigente, `ProblemDetails` con código estable, sin datos internos. Rutas de productos, pedidos y cocina implementadas:

| Método y ruta aprobada | Finalidad | Permisos |
|---|---|---|
| `GET /api/productos?disponibles=true&cursor=…&limite=…` | Catálogo seleccionable, versión y paginación | roles de salón autorizados |
| `GET /api/pedidos/activo?mesaId=…` o `?grupoId=…` | Cuenta activa, comandas y marcador de legado; exactamente un destino | roles de salón autorizados |
| `POST /api/pedidos/envios` | Abrir cuenta si no existe y enviar primera comanda, o añadir a cuenta activa; requiere `envioId` | roles de salón autorizados |
| `GET /api/cocina/cola?cursor=…&limite=…` | Comandas `Abierta`/`En Preparación` en orden fecha, ID | Cocina y roles de supervisión autorizados |
| `PUT /api/cocina/comandas/{id}/estado` | Body `{estado, versionComanda}`; transición con versión previa de esa fila | Cocina y roles de supervisión autorizados |

La ruta explícita evita presentar un único `POST /api/pedidos` ambiguo. Body propuesto: `{envioId, mesaId | grupoId, versionMesa (requerida para mesa), lineas:[{idProducto, versionProducto, cantidad, notas}]}`; XOR estricto, sin precios ni totales autoritativos. `versionMesa` se compara con `Mesa.Version` (`xmin` opaco); para grupo no hay versión de grupo en el modelo actual: se bloquean grupo y miembros en el orden definido, se verifica que siga activo y que las mesas sigan perteneciendo a él. `pedido` tampoco tiene versión propia; su serialización se hace mediante lock de fila/unique index, no con un campo ficticio `versionCuenta`. `envioId` se trata según §2.5. Respuesta confirmada incluye pedido/comanda, líneas con nombre/precio históricos, importes, estado, versiones disponibles y fecha UTC; no incluye datos no autorizados. Creación nueva devuelve `201 Created` y `Location`; una repetición idempotente reproduce resultado/status/location originales.

Errores de dominio usan `application/problem+json` y extensión `code` estable, sin stack, SQL ni detalles sensibles. Para validación de request, conservar el contrato 001: `400 ValidationProblemDetails`, `code=VALIDATION_ERROR` y `errors` por campo camelCase. Códigos de 003: 403 `AUTH_PROHIBIDO` (actual), 404 genérico por recurso inexistente/no accesible, 409 `MESA_CONFLICTO_CONCURRENCIA` (reutilizar el existente cuando aplique), `DESTINO_STATE_CONFLICT`, `CATALOGO_CAMBIADO`, `VERSION_CONFLICT`, `ENVIO_ID_REUTILIZADO`, `CUENTA_LEGADA_SIN_COMANDAS`, 413 `REQUEST_TOO_LARGE`, y 500 genérico. Para 401 se conservan los códigos actuales de sesión, no crear códigos paralelos. Incluir `traceId`; errores 409 pueden incluir versión/estado actual solo cuando el rol esté autorizado. No responder 200/201 si el commit no está confirmado. Timeout/5xx observado por Desktop tras iniciar la escritura se trata como incierto, no como error de negocio ni autorización para crear un ID nuevo automáticamente.

La consulta de cuenta devuelve 200 con `pedido:null` cuando no existe cuenta activa; cuenta legada sin comandas incluye indicador explícito `soloLectura=true` y motivo, y no devuelve líneas inventadas. Destino inexistente/no accesible devuelve 404 genérico. KDS y catálogo usan cursores opacos ligados al orden/filtro; página por defecto 25, máximo 100 y sin garantía de snapshot entre páginas. El endpoint de envío mide límite de body de 64 KiB y devuelve 413 `REQUEST_TOO_LARGE` con ProblemDetails.

En D3, la cola incluye `idComanda`, `idPedido`, destino (`idMesa`/`numeroMesa` o `idGrupo`), emisor, hora UTC, estado, versión opaca y líneas históricas de nombre/cantidad/notas. Devuelve `items` y `cursorSiguiente`; el cursor FIFO usa fecha e ID, sin snapshot entre páginas. Transición rechaza versión anterior con `VERSION_CONFLICT`, salto/retroceso con `DESTINO_STATE_CONFLICT` y no expone endpoints de pago o modificación de líneas. El resultado actualizado se devuelve solo tras commit; auditoría y recálculo son transaccionales.

**Matriz aprobada para especificar e implementar:**

| Rol | Consultar todas las cuentas/comandas y enviar | Cola KDS | Cambiar estado KDS |
|---|---:|---:|---:|
| Jefa | Sí | Sí | Sí |
| Supervisor | Sí | Sí | Sí |
| Mesero | Sí | No; estado desde cuenta | No |
| Encargado de Caja | Sí | No; estado desde cuenta | No |
| Personal de Cocina | No | Sí | Sí |

La comanda conserva a su emisor. Las consultas autorizadas de salón no se restringen al creador para permitir continuidad de turno. No se autoriza con ello anular líneas enviadas, pagar o cerrar cuentas. Cocina no obtiene permisos de modificación de mesas/grupos. La API aplica autorización en cada endpoint; Desktop no es frontera de seguridad.

## 5. Desktop y sincronización

- Vista de cuenta, borrador y catálogo accesible desde mesa o grupo; KDS como destino real solo cuando esté implementado y autorizado. ViewModels/servicios propios, bindings compilados, code-behind de vistas solo inicialización.
- Distinguir estado de cuenta y estado de cada comanda; no inferir pedidos ni ingredientes desde `Agrupada`. Mostrar listas en la cuenta mientras esté abierta; no añadir acuse de lectura. Indicador persistente compartido por estado de la cuenta, sin notificación push/sonido ni garantía de entrega. No afirmar retiro físico ni introducir parpadeo permanente.
- Polling aprobado: secuencial, intervalo nominal de 2 s y backoff 2→5→10→30 s ante fallos, siguiendo la cadencia de 001; no es SLA ni medición de render KDS. Sin solapamiento ni refresh automático de sesión. Cada flujo descarta lecturas obsoletas y coordina con mutaciones; integrar conscientemente `MesasRequestCoordinator` sin presumir que serializa nuevos recursos. No comparar `xmin` entre registros para ordenar eventos. Página por defecto 25, máxima 100; limitar conjunto activo visible y solicitar historial por separado.
- Sin red, últimas lecturas identificadas como desactualizadas, solo lectura, sin cola de mutaciones. Tras respuesta incierta bloquear acciones conflictivas y solicitar revisión; un GET no demuestra finalización. El bloqueo local de MVP no garantiza recuperación tras reinicio ni aislamiento entre terminales; documentar y no presentar como operación real segura.
- Validar pantalla táctil, ratón, teclado, contraste, tamaño mínimo y escalado. Mensajes de error, disponibilidad, confirmación y cambios de sesión deben conservarse accesibles; no incluir menús sin implementación.

## 6. Migración, despliegue y datos de ejemplo

Crear migración posterior a las existentes; nunca editar migraciones aplicadas. Primero inventario de solo lectura de esquema, restricciones, índices y conteos/estados de `pedido`, sin imprimir datos personales. Añadir tablas/índices nuevos y cambios aditivos mínimos; no recrear `pedido`, borrar/reescribir filas ni fabricar detalles. Las cuentas activas previas sin comanda se consultan en modo solo lectura y bloquean envío en API. Validar que los índices parciales existentes sigan incluyendo `Listo` y no debilitar XOR/FK/checks. Si falta una precondición o una incompatibilidad impide preservar invariantes, cancelar migración completa con diagnóstico seguro. Revisar SQL generado, FKs, tipos, precisión, índices, versiones y permisos runtime. Probar base vacía y esquema 001 representativo —sin pedido, activos con/sin comanda, pagado/cancelado, mesa y grupo—, rollback y preservación de filas/IDs/índices, solo en PostgreSQL temporal. No tocar base persistente sin autorización expresa y destino confirmado. Documentar respaldo y recuperación; no prometer rollback destructivo de datos nuevos.

Actualizar `deploy/runtime-grants.sql` con privilegios mínimos: `producto` SELECT y UPDATE solo de `version` para habilitar bloqueos de fila PostgreSQL; el trigger impide alterar esa versión directamente y runtime no puede cambiar datos de producto; `comanda` SELECT/INSERT y UPDATE solo de `estado`; `detalle_pedido` y `envio_pedido` SELECT/INSERT; `pedido` SELECT/INSERT y UPDATE solo de importes/estado, con USAGE/SELECT en su secuencia; secuencias nuevas con USAGE/SELECT. Probar estos permisos con un rol runtime temporal; nunca ampliar DDL ni UPDATE/DELETE de las tablas históricas. Para serializar liberación de mesa y primer envío, la ruta de cambio de estado de 001 adquiere `FOR UPDATE` sobre la mesa antes de consultar cuentas pendientes y conserva el lock hasta commit.

El catálogo de demostración usa nombres y precios explícitamente ficticios; los nombres llevan sufijo `DEMO` y el cliente deberá mostrar marca «DEMOSTRACIÓN». Los precios son datos de recorrido, no precios reales. El seeding es `dotnet run --project src/ElCarbonazo.Api -- --seed-demo-catalog`, solo con `ASPNETCORE_ENVIRONMENT=Development`, `DemoCatalog:Enabled=true` (por ejemplo `DemoCatalog__Enabled=true`) y base actual cuyo nombre comience `elcarbonazo_demo_` o `elcarbonazo_test_`. La ausencia de opt-in falla cerrada. El comando inserta solo si el catálogo está vacío; si ya coincide exactamente, no hace cambios; ante cualquier otro contenido aborta sin sobrescribir. No se ejecuta durante el arranque normal, no usa `HasData` ni endpoint público. Base aislada para aceptación, preparación y limpieza documentadas; las pruebas destructivas solo crean/eliminan bases `elcarbonazo_test_<uuid>` de su propia ejecución con permisos existentes. No ejecutar limpieza de base ajena.

La lista congelada de diez productos ficticios y sus precios se encuentra en `DemoCatalogSeeder.cs` y su regresión `Explicit_seed_is_repeatable_and_does_not_overwrite_a_non_demo_catalog`; no representa precios comerciales. `PedidosPersistenceTests` cubre aritmética, límites, histórico, rechazo completo y auditoría; `EnviosConcurrencyTests` verifica cambios de precio concurrentes.

## 7. Auditoría y pruebas

Auditar creación de cuenta/comanda (`ComandaEnviada`) y transición KDS (`ComandaEstadoCambiado`) con actor y referencias mínimas, en la misma transacción. Para envío registrar `idPedido`, `idComanda`, destino (`idMesa` o `idGrupo`), `envioId` y cantidad de líneas; para transición, `idPedido`, estado anterior y nuevo. `evento_auditoria.id_entidad` identifica la comanda. No registrar notas, nombres de productos, credenciales ni cuerpo HTTP. Mantener retención y permisos de `docs/domain.md`; un fallo de auditoría revierte negocio.

Pruebas por capas: validación de catálogo, notas, cantidades, precios, redondeos/overflow; integridad XOR/índices y migración de datos preexistentes; roles/401/403; cuenta única, grupo y mesa, transición y agregación; FIFO con empate; carreras abrir/abrir, abrir/liberar, enviar/cerrar grupo, enviar/transicionar, dos cocinas, fallos y rollback de auditoría; precio/disponibilidad concurrentes; timeout antes/después de commit y respuesta tardía; conservación de historial y no reintentos. Desktop: red, sesión, lectura obsoleta, estado incierto, reconocimiento y foco. Pruebas visuales/táctiles manuales con entorno aislado; ninguna compilación o captura aislada demuestra aceptación integral.

## 8. Puertas y evidencias

1. D0 está aprobado: implementar únicamente el alcance y contratos de `spec.md`; los cambios funcionales requieren actualización y aprobación previa.
2. Antes de aplicar migración, revisar SQL generado y probar migración idempotente/datos legados exclusivamente en PostgreSQL temporal. La aprobación actual no autoriza alterar bases persistentes.
3. Contratos y servicios con pruebas API, luego Desktop y pruebas con fakes; mantener 001 funcionando.
4. Secuencia final de formato → build → pruebas API → pruebas Desktop → auditoría de paquetes, más AcceptanceHost si se modifica o utiliza. Registrar ambiente y limitaciones; aceptación explícita del usuario.
5. Para operación real quedan pendientes recuperación durable, inventario y caja reales, seguridad de anulación, validación fiscal, LAN física y demás condiciones de despliegue; nunca afirmar que el MVP las cumple.
