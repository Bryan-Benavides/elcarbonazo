# Plan técnico — 003 Pedidos y KDS

**Estado: diseño documental; no implementado.** Están aprobados el alcance, la matriz de roles, estados, política de cuentas heredadas y regla fiscal recogidos en `spec.md`. Las secciones marcadas «propuesta técnica» ofrecen una línea base implementable, no una decisión comercial aprobada. El usuario debe revisar los contratos y límites antes de D1/D2; no se debe inventar aprobación tácita. Leer `spec.md`, `tasks.md`, `docs/domain.md`, la constitución y las lecciones relacionadas antes de programar.

## 1. Arquitectura y límites

- Reutilizar proyectos y versiones fijadas: API ASP.NET Core/.NET 10, EF Core/Npgsql; Desktop Avalonia 12/SukiUI/MVVM y DTOs propios. Controllers traducen HTTP y delegan en servicios; no acceden directamente a EF. Desktop y sus pruebas no referencian API, EF ni Npgsql.
- `SessionManager` sigue siendo dueño de tokens, sesión, TLS, actividad y navegación de seguridad. Una consulta periódica no constituye actividad ni renueva sesión por sí sola.
- La API es autoridad de validación, permisos, precio, estado, impuestos y auditoría. No confiar en disponibilidad, totales, rol ni versiones afirmadas por el cliente.
- Ningún nuevo paquete o cambio de versión se presupone. Si hiciera falta, justificar compatibilidad y aprobarlo por separado.

## 2. Modelo propuesto

### 2.1 Base actual y preservación

`pedido` ya existe con FK restrictivas, XOR, `NUMERIC(12,2)`, CHECK de importes y estados, e índices únicos parciales para pedidos activos por mesa o grupo (`AppDbContext`). La migración nueva debe preservar filas, claves, índices, secuencias y permisos; no modificar migraciones históricas ni depender de que la base esté vacía. La entidad actual no tiene detalles ni versión explícita expuesta. Aunque el diccionario de dominio enumera `producto`, `detalle_pedido` y `factura`, esas tablas no forman parte del esquema EF actual; confirmar físicamente la base solo mediante inventario autorizado de lectura.

### 2.2 Entidades nuevas candidatas

| Entidad | Responsabilidad | Datos mínimos propuestos |
|---|---|---|
| `producto` | Catálogo de demostración; no inventario | ID, nombre, categoría, precio NIO, disponible, versión para detectar cambio |
| `comanda` | Una entrega inmutable de líneas a cocina, no la cuenta | ID, pedido, usuario emisor, fecha UTC del servidor, estado KDS, versión de concurrencia |
| `detalle_pedido` | Línea confirmada de una comanda | ID, comanda, producto FK restrictiva, nombre histórico, cantidad, precio unitario histórico, subtotal, notas limitadas |
| `envio_pedido` (nombre candidato) | Deduplicar un intento lógico y reproducir confirmación | `envioId` único, usuario, hash semántico, referencias a pedido/comanda, resultado/status/location confirmados |

Nombres y columnas definitivos se fijan antes de migrar. Definir unicidad o permitir líneas repetidas con notas diferentes explícitamente; las líneas confirmadas no se editan ni eliminan desde 003. `producto.disponible` no es stock. El catálogo de demostración se carga por herramienta explícita, idempotente y limitada a base temporal o entorno de demostración autorizado; no `HasData` con precios aplicados automáticamente en bases existentes.

La cuenta conserva fecha, responsable original y destino; cada comanda conserva su propio emisor. Las FK conservan historial (`RESTRICT`). La ausencia de línea `producto` existente, categorías incompatibles o pedido heredado sin comandas no se corrige inventando datos. La política aprobada para una cuenta activa heredada sin comandas es conservarla intacta, mostrarla como «cuenta anterior sin detalle disponible» solo lectura y bloquear nuevos envíos a su destino. No se crea comanda vacía ni se altera estado/precio/fecha/actor. La resolución requiere una operación futura explícita y auditable; no se incluirá botón de cerrar/pagar en 003. Pedidos heredados pagados/cancelados permanecen intactos; su consulta histórica queda fuera del MVP. Diseñar inventario no destructivo y precondiciones de migración: si una fila o estructura impide preservar reglas, abortar transaccionalmente con diagnóstico sin datos sensibles ni pérdida de datos.

### 2.3 Estados y agregación

Estados aprobados de comanda: `Abierta` (enviada/recibida y pendiente de iniciar), `En Preparación`, `Lista`; transiciones solo `Abierta` → `En Preparación` → `Lista`. La versión de concurrencia de una comanda será un token opaco de esa fila (propuesta: PostgreSQL `xmin`, según patrón de `Mesa`) o una columna explícita decidida en D1; nunca se ordenan tokens entre filas ni se tratan como reloj. Estado agregado aprobado para cuenta activa: `Abierto` si hay al menos una comanda abierta; de otro modo `En Preparación` si existe una en preparación; de otro modo `Listo` si todas están listas. Un envío nuevo reabre la cuenta a `Abierto` sin reiniciar comandas previas. No aplicar el cálculo a cuentas heredadas sin comandas: mostrarlas usando explícitamente el estado anterior como cuenta de solo lectura y bloquear envío al destino.

La cuenta se bloquea durante creación y transición de comandas para serializar el recálculo. No marcar `Pagado`/`Cancelado` ni liberar destino desde KDS. El índice de cuenta activa debe seguir protegiendo todos los valores activos, incluidos `Listo`; comprobar que una actualización concurrente no abre un hueco sin protección. El diseño final debe asegurar que toda ruta de escritura de estado aplique las mismas invariantes; una comprobación solo en UI es insuficiente.

### 2.4 Importe y datos validados

Regla fiscal aprobada para el alcance 003, basada en la información del usuario de que el negocio opera bajo cuota fija: no añadir ni desglosar IVA 15 %. Precios NIO con dos decimales, `subtotal` como suma de cantidades × precio histórico, `impuestos = 0` y `total = subtotal`, con `NUMERIC(12,2)` conforme al esquema existente. El sistema no presenta pedido como factura ni determina cumplimiento fiscal. Si el negocio informa un cambio de régimen, revisar con el responsable/asesor competente y aprobar una especificación/cambio antes de alterar cálculos.

**Propuesta técnica conservadora, no límite comercial aprobado:** máximo 50 líneas por envío, cantidad 1–99 por línea, precio unitario entre C$0.01 y C$999,999.99, total máximo compatible con `NUMERIC(12,2)` (C$9,999,999,999.99), notas de hasta 150 caracteres Unicode y body JSON máximo 64 KiB. Rechazar cero, negativos, decimales en cantidad, valores no finitos/fuera de rango y cualquier overflow; validar cantidad de líneas antes de materializar/cargar productos. Un límite posterior más bajo puede aprobarse por operación. Estos valores deben confirmarse con el negocio antes de congelar el contrato.

Propuesta de conteo de nota: hasta 150 valores escalares Unicode después de normalización NFC (no bytes UTF-8 ni unidades UTF-16; un par sustituto cuenta como un valor, una secuencia con marcas puede contar más de uno). Desktop y API deben compartir pruebas de combinaciones/emoji. Rechazar controles no imprimibles salvo tabulación si el componente la admite; nunca registrar el contenido en auditoría técnica. El subtotal es la suma exacta `cantidad × precio histórico` por línea, escala 2, sin cálculo de impuesto; `impuestos=0`, `total=subtotal`. Ejemplos: 2 × C$10.10 = C$20.20; C$20.20 + C$0.80 = C$21.00. El nombre y precio histórico se capturan del catálogo dentro de la transacción.

Controlar cambios concurrentes de precio/disponibilidad con versión monotónica de producto o huella exacta, revalidada bajo lock durante el envío. Una versión obsoleta devuelve `409 CATALOGO_CAMBIADO` sin persistir parcialmente; la respuesta incluye solo IDs y metadatos actuales necesarios para que la persona revise y cree una nueva intención con nuevo `envioId`. No se sustituye precio ni disponibilidad silenciosamente.

### 2.5 Idempotencia de envío — propuesta técnica recomendada

- `envioId` es UUID v4 creado por Desktop una sola vez al confirmar y permanece asociado al body inmutable durante reintentos/reconciliación de esa intención. No se reutiliza para una edición, cambio de destino, líneas, notas, cantidad ni versión de catálogo.
- Antes de bloquear destino/cuenta, serializar solicitudes del mismo `envioId` con un advisory lock PostgreSQL transaccional derivado de forma estable del UUID; mantener además índice `UNIQUE` sobre el UUID como autoridad durable. La colisión del hash de advisory solo serializa solicitudes no relacionadas y no afecta corrección. Tras adquirir el lock, buscar la clave: reproducir resultado si actor/hash coinciden o devolver conflicto si no; solo si no existe continuar.
- Persistir en tabla nueva con índice `UNIQUE`: `envioId`, `id_usuario`, hash SHA-256 de la solicitud semántica canónica, referencias a pedido/comanda creados y datos suficientes para reproducir la respuesta confirmada. FKs restrictivas. No guardar token ni notas en auditoría; el detalle confirmado ya conserva los datos de la comanda.
- El hash se calcula sobre una serialización UTF-8 canónica del contrato validado, no sobre bytes JSON arbitrarios: campos en orden fijo, números con formato invariante, destino/versiones/líneas incluidos y orden de líneas conservado; `envioId` excluido y actor asociado aparte. Definir una sola canonicalización de Unicode antes de implementar y compartir casos de prueba API/Desktop.
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

## 4. Contratos HTTP propuestos — línea base pendiente de revisión

JSON camelCase y JWT vigente, `ProblemDetails` con código estable, sin datos internos. Rutas candidatas, no endpoints existentes:

| Método y ruta candidata | Finalidad | Permisos propuestos |
|---|---|---|
| `GET /api/productos?disponibles=true&cursor=…&limite=…` | Catálogo seleccionable, versión y paginación | roles de salón autorizados |
| `GET /api/pedidos/activo?mesaId=…` o `?grupoId=…` | Cuenta activa, comandas y marcador de legado; exactamente un destino | roles de salón autorizados |
| `POST /api/pedidos/envios` | Abrir cuenta si no existe y enviar primera comanda, o añadir a cuenta activa; requiere `envioId` | roles de salón autorizados |
| `GET /api/cocina/cola?cursor=…&limite=…` | Comandas `Abierta`/`En Preparación` en orden fecha, ID | Cocina y roles de supervisión autorizados |
| `PUT /api/cocina/comandas/{id}/estado` | Body `{estado, versionComanda}`; transición con versión previa de esa fila | Cocina y roles de supervisión autorizados |

La ruta explícita evita presentar un único `POST /api/pedidos` ambiguo. Body propuesto: `{envioId, mesaId | grupoId, versionMesa (requerida para mesa), lineas:[{idProducto, versionProducto, cantidad, notas}]}`; XOR estricto, sin precios ni totales autoritativos. `versionMesa` se compara con `Mesa.Version` (`xmin` opaco); para grupo no hay versión de grupo en el modelo actual: se bloquean grupo y miembros en el orden definido, se verifica que siga activo y que las mesas sigan perteneciendo a él. `pedido` tampoco tiene versión propia; su serialización se hace mediante lock de fila/unique index, no con un campo ficticio `versionCuenta`. `envioId` se trata según §2.5. Respuesta confirmada incluye pedido/comanda, líneas con nombre/precio históricos, importes, estado, versiones disponibles y fecha UTC; no incluye datos no autorizados. Creación nueva devuelve `201 Created` y `Location`; una repetición idempotente reproduce resultado/status/location originales.

Errores de dominio usan `application/problem+json` y extensión `code` estable, sin stack, SQL ni detalles sensibles. Para validación de request, conservar el contrato 001: `400 ValidationProblemDetails`, `code=VALIDATION_ERROR` y `errors` por campo camelCase. Códigos candidatos de 003: 403 `AUTH_PROHIBIDO` (actual), 404 genérico por recurso inexistente/no accesible, 409 `MESA_CONFLICTO_CONCURRENCIA` (reutilizar el existente cuando aplique), `DESTINO_STATE_CONFLICT`, `CATALOGO_CAMBIADO`, `VERSION_CONFLICT`, `ENVIO_ID_REUTILIZADO`, `CUENTA_LEGADA_SIN_COMANDAS`, 413 `REQUEST_TOO_LARGE` solo si se configura respuesta estable para el límite de body, y 500 genérico. Para 401 se conservan los códigos actuales de sesión, no crear códigos paralelos. Incluir `traceId`; errores 409 pueden incluir versión/estado actual solo cuando el rol esté autorizado. No responder 200/201 si el commit no está confirmado. Timeout/5xx observado por Desktop tras iniciar la escritura se trata como incierto, no como error de negocio ni autorización para crear un ID nuevo automáticamente.

La consulta de cuenta devuelve 200 con `pedido:null` cuando no existe cuenta activa; cuenta legada sin comandas incluye indicador explícito `soloLectura=true` y motivo, y no devuelve líneas inventadas. Destino inexistente/no accesible devuelve 404 genérico. KDS y catálogo usan cursores opacos ligados al orden/filtro; propuesta de máximo 100 registros por página y 25 por defecto, sin garantía de snapshot entre páginas. Revisar este contrato con la implementación existente de ProblemDetails y autenticación antes de congelarlo.

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
- Distinguir estado de cuenta y estado de cada comanda; no inferir pedidos ni ingredientes desde `Agrupada`. Mostrar listas en la cuenta mientras esté abierta; no añadir acuse de lectura. Propuesta de aviso: indicador persistente compartido por estado de la cuenta, sin notificación push/sonido ni garantía de entrega. No afirmar retiro físico ni introducir parpadeo permanente.
- Propuesta de polling: secuencial, intervalo nominal de 2 s y backoff 2→5→10→30 s ante fallos, siguiendo la cadencia local documentada para 001; esto no es SLA ni medición de render de KDS. Sin solapamiento ni refresh automático de sesión. Cada flujo descarta lecturas obsoletas y coordina con mutaciones; integrar conscientemente `MesasRequestCoordinator` sin presumir que serializa nuevos recursos. No comparar `xmin` entre registros para ordenar eventos. Paginación propuesta máxima 100 por lectura; limitar el conjunto activo visible y solicitar historial por separado.
- Sin red, últimas lecturas identificadas como desactualizadas, solo lectura, sin cola de mutaciones. Tras respuesta incierta bloquear acciones conflictivas y solicitar revisión; un GET no demuestra finalización. El bloqueo local de MVP no garantiza recuperación tras reinicio ni aislamiento entre terminales; documentar y no presentar como operación real segura.
- Validar pantalla táctil, ratón, teclado, contraste, tamaño mínimo y escalado. Mensajes de error, disponibilidad, confirmación y cambios de sesión deben conservarse accesibles; no incluir menús sin implementación.

## 6. Migración, despliegue y datos de ejemplo

Crear migración posterior a las existentes; nunca editar migraciones aplicadas. Primero inventario de solo lectura de esquema, restricciones, índices y conteos/estados de `pedido`, sin imprimir datos personales. Añadir tablas/índices nuevos y cambios aditivos mínimos; no recrear `pedido`, borrar/reescribir filas ni fabricar detalles. Las cuentas activas previas sin comanda se consultan en modo solo lectura y bloquean envío en API. Validar que los índices parciales existentes sigan incluyendo `Listo` y no debilitar XOR/FK/checks. Si falta una precondición o una incompatibilidad impide preservar invariantes, cancelar migración completa con diagnóstico seguro. Revisar SQL generado, FKs, tipos, precisión, índices, versiones y permisos runtime. Probar base vacía y esquema 001 representativo —sin pedido, activos con/sin comanda, pagado/cancelado, mesa y grupo—, rollback y preservación de filas/IDs/índices, solo en PostgreSQL temporal. No tocar base persistente sin autorización expresa y destino confirmado. Documentar respaldo y recuperación; no prometer rollback destructivo de datos nuevos.

El catálogo de demostración usa nombres/precios explícitamente ficticios o suministrados por el usuario; mostrar marca «DEMOSTRACIÓN», no crear/sobrescribir productos operativos en arranque y no asumir precios reales. El seeding será comando/herramienta explícita, idempotente, con verificación de entorno/base permitida y transacción; no `HasData` ni endpoint público. Base aislada para aceptación, preparación y limpieza documentadas; las pruebas destructivas solo crean/eliminan bases `elcarbonazo_test_<uuid>` de su propia ejecución con permisos existentes. No ejecutar limpieza de base ajena.

## 7. Auditoría y pruebas

Auditar creación de cuenta/comanda y transición KDS con actor y referencias mínimas, en la misma transacción. Aplicar los códigos candidatos de §4 solo tras revisarlos contra `docs/domain.md` y el contrato de auditoría vigente; definir payload permitido y retención antes de implementar. No guardar notas libres ni credenciales. Un fallo de auditoría revierte negocio.

Pruebas por capas: validación de catálogo, notas, cantidades, precios, redondeos/overflow; integridad XOR/índices y migración de datos preexistentes; roles/401/403; cuenta única, grupo y mesa, transición y agregación; FIFO con empate; carreras abrir/abrir, abrir/liberar, enviar/cerrar grupo, enviar/transicionar, dos cocinas, fallos y rollback de auditoría; precio/disponibilidad concurrentes; timeout antes/después de commit y respuesta tardía; conservación de historial y no reintentos. Desktop: red, sesión, lectura obsoleta, estado incierto, reconocimiento y foco. Pruebas visuales/táctiles manuales con entorno aislado; ninguna compilación o captura aislada demuestra aceptación integral.

## 8. Puertas y evidencias

1. El usuario revisa y aprueba/corrige en `spec.md §10` las propuestas técnicas de `envioId`, límites, ProblemDetails, rutas/paginación, concurrencia y polling. Las decisiones comerciales no confirmadas (p.ej. máximos de cantidad) no se dan por aprobadas.
2. Revisión de orden de locks contra servicios 001, SQL/migración aditiva y tratamiento de legados; pruebas destructivas solo en bases temporales. El diseño idempotente requiere pruebas de carrera, commit incierto y reinicio.
3. Contratos y servicios con pruebas API, luego Desktop y pruebas con fakes; mantener 001 funcionando.
4. Secuencia final de formato → build → pruebas API → pruebas Desktop → auditoría de paquetes, más AcceptanceHost si se modifica o utiliza. Registrar ambiente y limitaciones; aceptación explícita del usuario.
5. Para operación real quedan pendientes recuperación durable, inventario y caja reales, seguridad de anulación, validación fiscal, LAN física y demás condiciones de despliegue; nunca afirmar que el MVP las cumple.
