# Plan técnico 005 — Inventario y escandallo

**Estado: línea base D0 aprobada y precisiones iniciales cerradas para comenzar C1.** Las verificaciones de SQL, restricciones y carreras son tareas de implementación, no resultados ya demostrados; cualquier cambio funcional requiere nueva aprobación. No aplicar migraciones a bases persistentes sin autorización y destino confirmado.

## 1. Modelo físico propuesto

PostgreSQL snake_case, entidades PascalCase y Fluent API explícita. UTC `timestamptz` con `TimeProvider`; presentar en `America/Managua`.

| Tabla | Campos e invariantes |
|---|---|
| `ingrediente` | `id_ingrediente int` identity PK, `nombre varchar(120)` obligatorio, clave normalizada única, `unidad_medida varchar(20)`, `stock_actual` y `stock_minimo numeric(12,4)` no negativos default 0, `activo` default true, creación/actualización UTC, versión opaca. |
| `receta` | `id_receta int` identity PK, `id_producto` FK RESTRICT único, `activa`, `notas_preparacion varchar(500)` nullable, actualización UTC, versión opaca. |
| `detalle_receta` | `id_detalle_receta int` identity PK, `id_receta` e `id_ingrediente` FK RESTRICT, `cantidad_requerida numeric(12,4)>0`, UNIQUE de receta/ingrediente. |
| `movimiento_inventario` | `id_movimiento bigint` identity PK, ingrediente FK RESTRICT, tipo, delta y saldos `numeric(12,4)`, pedido y comanda FK RESTRICT opcionales, usuario FK RESTRICT, motivo hasta 250 y fecha UTC. |

CHECK explícitos de valores no nulos, tipo/signo, `stock_posterior = stock_anterior + cantidad_delta`, saldos no negativos, motivo obligatorio en ajustes y origen pedido/comanda obligatorio solo en consumo. Añadir UNIQUE parcial `(id_comanda,id_ingrediente)` para consumo y comprobar coherencia de pedido/comanda mediante validación transaccional bajo el pedido bloqueado. El kardex no admite UPDATE/DELETE por runtime. Los CHECK deben excluir explícitamente NULL cuando corresponda: PostgreSQL acepta `UNKNOWN`.

**Normalización:** API valida Unicode bien formado y controles, aplica NFC → `Trim()` para `nombre`, exige 1–120 escalares, y calcula `nombre_clave` mediante `nombre.ToLowerInvariant().Normalize(FormC)`. Guardar clave en columna separada con comparación binaria determinista `COLLATE \"C\"` y UNIQUE; API es autoridad de su cálculo y actualiza nombre/clave juntos, con prueba de carreras y equivalencias Unicode (incluidos acentos y mayúsculas). La restricción de unicidad protege la clave almacenada, **no demuestra** por sí sola que otro escritor SQL calculó correctamente la clave; runtime solo modifica por la API y no se afirma equivalencia universal con `lower()` PostgreSQL. Rechazar si la clave normalizada supera el límite físico de 240 caracteres; conservar representación del nombre. No eliminar acentos ni prometer case folding Unicode completo. Revalidar cambios de versión .NET/Unicode antes de actualizar el entorno.

**Versiones:** `ingrediente.version` y `receta.version` son `bigint` positivos inicializados a 1, enviados como cadena decimal para evitar pérdida de precisión JSON; se comparan como tokens de igualdad, nunca como relojes. Bajo lock, toda modificación de ingrediente, incluido stock por movimiento, incrementa versión una vez; todo PUT efectivo de receta incrementa la versión padre aun cuando cambien solo líneas. Rechazar desbordamiento sin escribir. Para crear receta se exige `versionEsperada: null` explícito; para reemplazar se exige cadena positiva igual a la actual. Ausencia de campo es 400, existencia inesperada o versión distinta es 409 `VERSION_CONFLICT`. GET de producto existente sin receta devuelve `receta: null` explícito; producto inexistente, 404. La creación del ingrediente no acepta versión ni stock inicial. No confiar en claves/versiones enviadas como estado autoritativo.

RESTRICT en referencias históricas; reemplazar líneas actuales de receta requiere permisos y diseño separados de evidencia histórica: snapshot de consumo en movimientos, no borrar movimientos. No almacenar precio unitario en detalle de receta.

**Límites y precisión:** 1–50 líneas por receta activa, 0–50 si inactiva; hasta 50 productos/líneas por envío según 003, hasta 2.500 asociaciones antes de agregación. Notas hasta 500 escalares NFC; motivo hasta 250 escalares NFC y trim; búsqueda hasta 120 escalares NFC y trim; rechazar controles y Unicode inválido. `decimal` exacto, escala máxima cuatro y máximo `99999999.9999`; no redondear silenciosamente. Calcular suma/productos con `decimal` checked, y si un requerimiento agregado o saldo excede el tipo persistible rechazar 400 sin mutación. Los DTO reciben/emiten cantidades como números JSON decimales, no `double`; validación debe detectar más de cuatro decimales antes de persistir. `stock_minimo` se valida igual que stock. Precisar comportamiento de visualización cultural sin modificar valores.

## 2. Transacciones y orden

Inspección inicial: `EnviosService` toma advisory lock de `envioId`, bloquea destino y pedido, y consulta productos por ID ascendente `FOR SHARE`; Caja usa locks de destino/cuenta/comandas y no bloquea producto para cobrar. 003 especifica el orden completo con comandas. C1/C2 deben contrastar SQL y locks reales, incluidos FK, índices y triggers; detener ante incompatibilidad.

Orden de envío previsto: `envioId → grupo/mesas o mesa → pedido existente → producto por ID ASC → ingrediente por ID ASC → inserciones de pedido/comanda, movimientos y auditoría`. El servicio actual consulta comandas y operaciones de caja, sin tomar explícitamente locks de todas las comandas; conserva la serialización del pedido. La receta y sus líneas se leen después del lock de producto, protegidas por la barrera común de producto; no se necesita lock independiente de receta en el envío. Mantener locks hasta commit; probar interacciones reales de inserciones/FK sin invertir locks preexistentes. No tomar ingredientes antes de destino/productos. Caja conserva su barrera y orden propios, no toma ingredientes ni descuenta al cobrar.

Para resolver la ausencia de receta y serializar inserciones: envío adquiere producto `FOR SHARE` antes de leer receta/líneas; guardar receta adquiere **producto `FOR UPDATE`** primero, luego receta y los ingredientes de la nueva ficha por ID ASC. Toda ruta que cambie ficha debe respetar esa barrera; tras esperar, releer y revalidar bajo READ COMMITTED. No adquirir destino después de producto/ingrediente en rutas administrativas. La inactivación o cambio de unidad bloquea solo ingrediente y consulta referencias activas sin bloquear productos/recetas; el guardado de receta bloquea ingredientes y revalida activos, evitando inversión. Verificar esta propuesta con intercalaciones reales y pruebas, incluidas FK y creación de receta inexistente.

Movimientos manuales bloquean ingrediente por ID ascendente, validan saldo y escriben movimiento/auditoría en la misma transacción. Sin interacción humana durante locks. Mantener controles transaccionales y restricciones aunque existan validaciones UI. Orden reduce riesgo pero no garantiza ausencia absoluta de deadlocks: tratar cancelación/error con rollback y resultado incierto si corresponde; no reintentar escrituras automáticamente.

## 3. Idempotencia y recuperación

Resolver primeramente la clave `envioId` de 003: si ya hay resultado autorizado para mismo actor/hash, devolverlo sin consultar receta ni consumir; si no hay resultado, efectuar validaciones y consumo en la misma transacción que registro de envío. Rol revocado no reproduce. La caída tras commit exige conservar intención existente para repetición humana explícita según 003; un GET no acredita terminación. El cliente 003 no garantiza recuperar intención tras reinicio.

Para escrituras nuevas de inventario/recetas no se promete idempotencia ni recuperación durable: timeout/red/5xx o respuesta inválida después de enviar implica incertidumbre y bloqueo de acciones conflictivas durante la sesión, sin reintentos automáticos. Revisión humana no prueba que una solicitud tardía haya terminado; no ofrecer una nueva escritura como segura por consultar GET. Tras reinicio o entre terminales no se garantiza bloqueo; documentar la limitación universitaria y dejar recuperación segura para diseño futuro. No emplear diario Caja.

## 4. API y Desktop

Aplicar matriz/rutas de `spec.md`, control estricto de body, normalización y tipos decimales, DTOs, validación de cursores y ausencia de secretos en problemas y auditoría. Resumen y página de alertas deben indicar que no son snapshot si se consultan por separado. `GET /api/recetas/productos?cursor=&limite=&busqueda=` es exclusivo de Jefa, incluye productos disponibles y no disponibles con `idProducto`, nombre, categoría, disponibilidad y `estadoReceta` (`SinReceta`, `Activa`, `Inactiva`), sin líneas; consultar detalle por ruta de receta. Reutilizar catálogo 003 sin alterar su contrato ni exponer recetas a salón.

Cursores opacos base64url JSON versionados, validados y ligados a filtros normalizados: ingredientes y productos por ID ascendente; movimientos por `id_movimiento` descendente; límite 25 por defecto, 1–100. Cursores no son credenciales y no garantizan snapshot; inválidos o incompatibles responden `400 VALIDATION_ERROR`. Alertas admite `cursor`/`limite`, orden por ingrediente ASC y retorna conteos de activos más página de bajos/agotados; no afirmar fotografía global estable. Filtro `estadoAlerta` opcional, `busqueda` opcional por nombre normalizado; ingredientes inactivos visibles a roles autorizados sin sumarlos a alertas. Body máximo 16 KiB en ingrediente/movimiento, 32 KiB en receta; envío 003 conserva 64 KiB.

Desktop integra clientes REST tipados, generación de sesión y descarte de lecturas antiguas, coordinador para evitar lectura/mutación solapada y bloqueo conservador de escrituras inciertas. Mantener `SessionManager`, TLS y navegación únicamente hacia destinos implementados y autorizados. Validar números exactos sin usar `double`; distinguir ausencia, null y respuestas inválidas. Formato local de fecha solo en presentación.

## 5. Migración y grants

Migración EF aditiva `AddInventarioEscandallo`, sin editar migraciones previas ni migrar al arranque. Su `Down` debe impedir pérdida accidental de movimientos/evidencia, con política de reversión documentada; no prometer rollback destructivo seguro. Validar esquema de origen y abortar transaccionalmente ante incompatibilidad. Probar desde base vacía y 001+003+004 representativa, preservando IDs, comandas, comprobantes, cuentas y grants; no crear consumos históricos. PostgreSQL temporal `elcarbonazo_test_<uuid>` exclusivamente para pruebas destructivas. Aplicar a base persistente solo con autorización y destino confirmado.

Actualizar `deploy/runtime-grants.sql` estrictamente para SELECT/INSERT/UPDATE y secuencias necesarios; ninguna eliminación/actualización de movimientos o auditoría. El reemplazo de líneas de receta puede requerir DELETE solo sobre `detalle_receta`, no sobre historia; revisar locks de filas PostgreSQL y demostrar privilegios mínimos con rol temporal no propietario, sin CREATE ni superusuario. No elevar permisos para simplificar pruebas.

## 6. Pruebas y aceptación

- PostgreSQL temporal: constraints, Unicode/carreras de nombre, precisiones/overflow, migración y grants; unidades y recetas activas/inactivas.
- Agregación por ingrediente con productos/líneas repetidas; ausencia de receta y stock insuficiente sin efectos; dos envíos compiten por último stock en conexiones independientes.
- Repetición idempotente tras commit/respuesta perdida: una sola comanda y un movimiento por ingrediente; mismo UUID con otro actor/hash rechazado. Auditoría fallida revierte todo.
- Carreras controladas: edición/creación de receta vs envío, inactivación/cambio de unidad vs guardado/envío, ajuste vs consumo, destinos mesa/grupo y compatibilidad con Caja/KDS. No afirmar cobertura exhaustiva.
- Manuales: signos, mínimos, historial, rollback, permisos de cada ruta/rol, DTO malformado, paginación y versión obsoleta. Desktop: decimales, 409, sesión cambiada, lectura antigua e incertidumbre sin repetición.
- Secuencia: `dotnet format ElCarbonazo.sln --verify-no-changes --no-restore`, `dotnet build ElCarbonazo.sln`, tests API y Desktop con `--no-build --no-restore`, `dotnet list ElCarbonazo.sln package --vulnerable --include-transitive`. AcceptanceHost se construye aparte si se usa; documentar sus límites.
- Ejecutar recorrido ficticio y revisión práctica local de interfaz; crear `docs/validacion-005.md` solo con evidencia observada. No afirmar LAN física, verificación de producción, cobertura o mediciones no realizadas.
