# Plan técnico — 004 Caja, cobros y comprobante interno

**Estado:** línea base técnica D0 aprobada por el usuario para iniciar implementación del perfil universitario local aislado. C1 puede comenzar; cada migración y cambio de código sigue sujeto a sus puertas y pruebas. Esta aprobación no autoriza migrar una base persistente ni usar dinero real.

## 1. Arquitectura y restricciones

- Reutilizar solución/.NET 10 y paquetes existentes: API ASP.NET Core + EF Core/Npgsql; Desktop Avalonia 12/SukiUI/MVVM con REST; PostgreSQL. Controllers delgados, servicios coordinan dominio/transacción, entidades no dependen de HTTP, DTOs nunca exponen EF.
- Desktop/pruebas no referencian API, EF Core ni Npgsql. Code-behind solo inicialización. Reutilizar `SessionManager`; polling no renueva sesión ni es actividad.
- API decide roles, totales, elegibilidad, referencias, estados y resultado. Sin dependencia de Internet.
- No agregar paquetes ni actualizar versiones sin análisis y aprobación de compatibilidad. Todos los tiempos persistidos UTC mediante `TimeProvider`; presentar localmente en `America/Managua`.
- 002 sigue pospuesto y mesa-específico. 004 debe implementar protocolo durable para sus tipos de operación sin afirmar que 002 ya existe ni modificar contratos de 001 de modo implícito. Durante implementación evaluar extracción futura compartida solo si mantiene los contratos separados y recibe aprobación.

## 2. Inventario previo / compatibilidad

Antes de diseñar migración final, verificar modelo EF y migraciones 001/003; no asumir que el diccionario conceptual es esquema físico. Las tablas existentes relevantes son `pedido`, `comanda`, `detalle_pedido`, `mesa`, `grupo_mesa`, miembros, `usuario`, `evento_auditoria` y la idempotencia de envíos. Revisar CHECKs, índices parciales, FKs, permisos runtime y locks de `PedidosService`, `CocinaService`, `MesasService`, `GruposMesaService`.

La migración será aditiva: no editar migraciones aplicadas, no borrar/recrear `pedido`, no fabricar detalles ni modificar datos históricos. Cualquier precondición estructural incompatible aborta transaccionalmente. El diagnóstico no incluirá secretos ni datos personales. Primero revisar SQL generado y probar esquema vacío y representativo en PostgreSQL temporal. No aplicar en base persistente sin destino exacto confirmado y autorización expresa.

## 3. Modelo persistente aprobado

Nombres de tablas y columnas se fijan como snake_case conforme al esquema existente; nombres C# siguen PascalCase. Las FK/columnas se mapean explícitamente en Fluent API.

| Entidad | Datos y reglas principales |
|---|---|
| `turno_caja` | PK `id_turno_caja`; estado `Abierto`/`Cerrado`; UTC apertura; FK restrictiva del abridor; fondo inicial `NUMERIC(12,2)` >= 0; UTC cierre y FK cerrador; efectivo esperado/contado/diferencia; totales efectivo/tarjeta/transferencia; número de cuentas pendientes al cierre; motivo solo si diferencia; FK a operación de apertura/cierre. El registro cerrado es inmutable. |
| `cobro` | PK `id_cobro`; número interno `numero_comprobante` bigint único generado por secuencia (huecos válidos); FK restrictiva a `pedido`, `turno_caja`, actor y operación; método `Efectivo`/`Tarjeta`/`Transferencia`; moneda fija NIO; subtotal/impuestos/total; recibido/vuelto solo efectivo; referencia solo pago externo; fecha UTC; único `id_pedido`. |
| `comprobante_cobro_linea` | PK; FK restrictiva a cobro; ordinal único por cobro; snapshot obligatorio de `id_detalle` de origen como valor informativo sin FK (la FK real histórica es comanda/detalle y ya aplica RESTRICT), nombre, cantidad, precio y subtotal históricos. El snapshot se confirma junto al cobro y nunca se edita. |
| `operacion_caja` | UUID PK; propietario FK obligatorio (para tombstone previo a prepare, el actor que creó la barrera); tipo/versión; argumentos canónicos JSONB y hash NULL únicamente para ese tombstone; estado, UTC creación/terminal; HTTP status, código y resultado tipado reproducible; campos de actor/fecha de cancelación. No caduca automáticamente ni guarda secretos. |
| `evento_auditoria` | Tabla existente: insertar eventos de caja con campos permitidos en la transacción correspondiente; no alterar su política append-only ni incluir PAN, credenciales, notas o body completo. |

### Integridad y restricciones

- Índice único parcial para `turno_caja` abierto; la concurrencia se resuelve además con lock/advisory sobre una clave global estable y transacción.
- `CHECK` de importes no negativos, escala/tipo acordados, `total = subtotal + impuestos`, impuestos cero para alcance vigente; recibido/vuelto coherentes por efectivo; campos método mutuamente compatibles y referencias externas no vacías.
- Índice `UNIQUE(cobro.id_pedido)` para impedir doble cobro incluso con carreras. Bloqueo del pedido y transición de estado complementan el índice.
- `FK RESTRICT` desde turno/cobro/comprobante/operación a actor y pedido. No cascadas que borren evidencia.
- Trigger de PostgreSQL en `turno_caja` permite únicamente transición `Abierto` → `Cerrado`, conserva inmutables los campos de apertura y rechaza cualquier actualización de un turno ya cerrado, además de grants por columna.
- `operacion_caja.id_operacion` UUID PK y registro de barrera de cancelación incluso si aún no hay intención, garantizando que ejecución/preparación tardía del UUID cancelado no ocurra. En un tombstone previo a preparación, propietario es el actor que crea la barrera y tipo/argumentos/hash son NULL; CHECK impide otras combinaciones. La preparación tardía del actor original no puede adoptar ni reemplazar ese UUID.
- Definir grants runtime mínimos: SELECT/INSERT de nuevas evidencias y UPDATE solo de campos necesarios del turno/operación/pedido; runtime nunca DDL. Probar permisos efectivos con rol temporal. No otorgar UPDATE/DELETE a cobros, líneas snapshot, comprobantes o auditoría salvo necesidad revisada.
- Números internos de comprobante: secuencia/identidad durable, huecos permitidos; no prometer numeración fiscal correlativa. Identificador visible no reutilizable.

### Importes y límites aprobados

- NIO `NUMERIC(12,2)` coherente con 003, máximo C$9,999,999,999.99 para total representable. Valores no negativos; total cobrable > 0.
- Fondo, conteo y recibido limitados a escala 2 y rango `NUMERIC(12,2)`; vuelto derivado exacto, sin redondeos binarios.
- Referencia externa normalizada Unicode NFC, trim de extremos, 1–100 valores escalares Unicode; rechazar controles y vacío. Coincidencia exacta, sensible a mayúsculas, después de NFC/trim y dentro del mismo método. Serializar el chequeo con advisory lock transaccional derivado de método + SHA-256 del valor canónico, volver a consultar cobros confirmados y no exponer la referencia previa. Sin unique constraint: coincidencia no prueba duplicado real. Si no se confirmó previamente, guardar rechazo terminal `REFERENCIA_REQUIERE_CONFIRMACION`; tras revisión humana se crea un UUID nuevo con confirmación explícita. La operación nueva vuelve a comprobar bajo el mismo lock antes de persistir el cobro.
- Motivo diferencia: 1–500 valores escalares después de NFC/trim; no aceptar solo espacios ni controles; no incluir en logs técnicos. Mantenerlo en cierre/auditoría con lectura restringida a Caja/Jefa.
- Request JSON máximo 32 KiB para operaciones `prepare` y 16 KiB para otras mutaciones; paginación default 25, max 100, cursores opacos ligados a filtro/orden. Estos son límites fijos de primera entrega.
- Hash semántico SHA-256 de contrato canónico UTF-8 con propiedades en orden fijo, números invariantes y normalización definida; actor asociado aparte; UUID excluido de hash y propiedad de la clave.

## 4. Operación durable: prepare/execute/query/cancel

### Protocolo

Usar principios de carrera de 002 como referencia, no reutilizar la tabla `operacion_mesa` ni asegurar que el módulo está implementado. Contratos aprobados:

| Método y ruta | Propósito |
|---|---|
| `PUT /api/caja/operaciones/{uuid}` | Registrar intención tipada inmutable; no ejecuta negocio. |
| `POST /api/caja/operaciones/{uuid}/ejecucion` | Ejecutar solo argumentos preparados; sin body de negocio alternativo. |
| `GET /api/caja/operaciones/{uuid}` | Consultar estado/resultado permitido del mismo UUID. |
| `POST /api/caja/operaciones/{uuid}/cancelacion` | Establecer/consultar barrera terminal durable; nunca revierte efecto confirmado. |

Tipos iniciales: `AbrirTurno` (fondo), `CobrarCuenta` (pedido, turno esperado, total visto, método y efectivo recibido o referencia + confirmación humana externa y confirmación de coincidencia si aplica), `CerrarTurno` (conteo y motivo si aplica). La semántica externa no convierte la terminal en participante transaccional: el actor declara verificación antes del registro; anomalías después requieren revisión humana.

### Atomicidad y autorización

- Prepare normaliza DTO y persiste propietario, tipo, versión y argumentos. UUID existente idéntico del mismo propietario devuelve el registro; discordancia devuelve 409 `OPERACION_CLAVE_EN_USO`; UUID de otro propietario devuelve 404 genérico. La autorización vuelve a verificarse al ejecutar y consultar.
- Serializar por UUID mediante advisory lock estable/clave primaria con barrera persistible. Preparación, cancelación y ejecución deben tener orden probado. No tomar lock de turno/pedido antes de resolver una clave idempotente ya existente.
- Execute toma la fila de operación `FOR UPDATE`; estado terminal reproduce resultado. Para preparada, aplica servicio de dominio usando misma conexión/transacción, guarda mutación, snapshot, auditoría y resultado terminal y hace un commit único. Si una excepción interna ocurre, rollback sin inventar resultado terminal. Rechazos de dominio deterministas se guardan terminalmente con mecanismo que no deje transacción PostgreSQL abortada; diseñar savepoint explícito y probarlo.
- No guardar `En ejecución` salvo diseño persistente consistente; `Preparada` puede coexistir con ejecución no confirmada y no es evidencia de no ejecución.
- Cancelar UUID inexistente crea tombstone propiedad del actor que cancela y evita preparación/ejecución tardías. Si execute ganó, cancelación devuelve resultado real. Cancelación idempotente no duplica auditoría de éxito. Solo Jefa puede cancelar una intención ajena o crear una barrera supervisada; Encargado de Caja recupera/cancela únicamente su propia intención. Resolver una operación ajena nunca autoriza ejecutarla como su propietario.
- Estado terminal confirma si cobro ocurrió, fue rechazado o cancelado. Inaccesibilidad, timeout o GET ordinario no son estados terminales.
- Retención: conservar todos los resultados terminales completos por al menos cinco años, alineado con auditoría. No purgar operaciones no terminales. Tras cinco años solo una tarea administrativa explícita podrá compactar a tombstone permanente con UUID, propietario, hash, tipo, estado terminal y fecha, sin borrar identidad ni permitir reejecución. No hay purga automática en la primera entrega.

### Recuperación Desktop

- Antes de primer envío guardar duraderamente UUID, propietario, instalación, tipo/fase mínima y formato de diario en `%ProgramData%\ElCarbonazo\recovery\caja-journal.json`; no guardar token/contraseña, referencia externa, importe de tarjeta ni body completo. El instalador debe aprovisionar el archivo/ACL; no asumir diario de 002 disponible. Si no se puede asegurar persistencia/ACL, bloquear nueva mutación recuperable.
- El diario admite una sola operación local no terminal, se escribe mediante reemplazo atómico y conserva estado terminal hasta reconciliación con API. ACL permite acceso solo a identidades operativas autorizadas y administradores. Verificar reemplazo/ACL en Windows; no afirmar protección ante administrador o cuenta con permisos de escritura.
- Para la aceptación universitaria local aislada se verifica con la cuenta Windows actual y ACL temporales, incluyendo rechazo de reglas de acceso amplio y conservación tras reemplazo. Por decisión del usuario no es requisito crear ni iniciar sesión con otra cuenta Windows real. No afirmar que se verificó el acceso efectivo desde otra cuenta; esa comprobación queda como limitación para uso real. La identidad Windows no sustituye la autorización por usuario y sesión de la aplicación.
- Reinicio recupera UUID por REST; consulta/cancelación es explícita, no ejecuta automáticamente una operación preparada. Un usuario distinto mantiene bloqueo local hasta terminalidad/reconciliación; Jefa puede supervisar según política y UI conserva la evidencia hasta resolver.
- Aun con estado terminal, reconciliar turno/cuenta desde API antes de reactivar; las lecturas no reemplazan la terminalidad de operación.
- Timeout, `IOException`, cuerpo inválido, 5xx o cancellation después de iniciar escritura se tratan conservadoramente. No reintentar el pago externo ni crear nuevo UUID para misma intención. No borrar diario si una respuesta o reconciliación no es concluyente.

## 5. Orden de locks y carreras cruzadas

Orden global aprobado como contrato de implementación, sujeto a pruebas deterministas contra los locks reales de 003. La clave idempotente se resuelve primero; luego los recursos de dominio siguen 003 y el turno se bloquea después del pedido, antes de insertar el cobro:

1. Operación: adquirir lock transaccional/advisory de UUID, verificar propietario/tipo/hash/tombstone y bloquear fila de operación. Nunca tomar lock de dominio antes de resolver identidad; la fila de operación ya está bloqueada y no se vuelve a bloquear al guardar el resultado.
2. Destino individual: `mesa → pedido → comandas por id ascendente → turno_caja → cobro/snapshot`.
3. Destino de grupo: `grupo → mesas miembro por id ascendente → pedido → comandas por id ascendente → turno_caja → cobro/snapshot`.
4. KDS y envío conservan orden de 003: `grupo → miembros ordenados → pedido → comanda` para grupo; `mesa → pedido → comanda` para mesa. KDS individual debe tomar mesa antes del pedido para respetar liberación/envío; revisión C1 confirmará rutas y tests existentes.
5. Apertura, cierre y creación de cualquier preparación de caja toman advisory lock transaccional global `caja-compartida`, después de resolver/bloquear su UUID y antes de leer el turno o insertar la preparación. Esto evita también que una apertura consulte ausencia de intenciones mientras otra preparación se confirma. Bajo esa barrera, prepare de cobro comprueba que el turno esperado continúa abierto e inserta la intención en la misma transacción; si está cerrado, no crea una intención ejecutable. Cierre toma fila de turno y, bajo READ COMMITTED, consulta operaciones no terminales con una sentencia MVCC posterior a obtener la barrera y el turno, **sin bloquear sus filas**, excluyendo el UUID del propio cierre. Si encuentra otra intención relevante para ese turno, rechaza el cierre manteniendo turno abierto. Ambos conservan la barrera hasta commit/rollback. No tomar locks de otras filas de `operacion_caja` después de `turno_caja`, para evitar ciclo con ejecución (operación → dominio → turno). La consulta debe identificar el turno esperado en argumentos canónicos validados; una operación relevante cuyo turno no pueda determinarse bloquea el cierre de forma conservadora. Una preparación ya existente no puede convertirse en nueva intención sin esta barrera.
6. La **ejecución** de cobro nunca toma el lock global de apertura/cierre; después de su pedido toma `turno_caja FOR UPDATE`. Si cierre obtuvo el turno primero, cobro espera y luego observa cerrado/rechaza. Si cobro obtuvo primero, cierre espera y calcula totales confirmados después del commit. Si hay una operación preparada observable, cierre se bloquea conservadoramente; terminalizar/cancelar es una acción separada y cierre se intenta de nuevo explícitamente. Prepare sí toma la barrera indicada en el punto 5.
7. Producto no se bloquea para cobro: líneas históricas son autoridad; catálogo actual irrelevante.

La preparación de cobro toma la barrera global y después los locks del destino y pedido/comandas según los puntos 2–3 antes de validar e insertar la intención. Así serializa la creación de la intención con envíos y cambios KDS, que comprueban bajo los mismos locks si existe un cobro preparado para esa cuenta. La ejecución de cobro no toma la barrera global; consulta intenciones competidoras sin bloquear sus filas antes de bloquear turno. Un payload pendiente no interpretable bloquea conservadoramente.

Antes de añadir código, C1 inspecciona el SQL/orden implementado y valida esta matriz con pruebas PostgreSQL concurrentes. Toda incompatibilidad requiere detener y actualizar/aprobar plan antes de alterar orden. No mantener locks durante interacción humana ni validación bancaria externa. La confirmación externa ocurre antes de preparar/ejecutar el registro local.

### Invariantes cruzadas

- Envío nuevo, transición a `Lista`, liberación/cierre y cobro se serializan; cualquier cambio que invalide cuenta produce conflicto/revisión.
- Una cuenta solo pasa a `Pagado` a la vez que cobro y snapshot quedan confirmados; el índice único impide doble pago.
- Cierre calcula totales desde cobros confirmados del turno en la misma transacción y bloquea nuevos cobros en cuanto gana el lock de cierre. Una operación de cobro no terminal impide cerrar incluso si su fila aún no es visible como ejecutada.
- Cierre no puede finalizar con una intención de cobro preparada/ejecutándose que pueda afectar turno. Cancelación debe ganar la barrera, o esperar resultado terminal antes de continuar.
- Grupo pagado permanece activo, sin pedido nuevo; aplicar el gate en 003 y 004 bajo locks que eviten competir con envío.

## 6. Contratos REST aprobados

JSON camelCase, JWT/sesión actual, `ProblemDetails` con `code`, `traceId`, sin entidades EF ni stack. Toda mutación de negocio usa protocolo durable anterior.

| Método/ruta | Finalidad |
|---|---|
| `GET /api/caja/turno/actual` | Turno abierto/estado vacío y resumen; no prueba resultado terminal. |
| `GET /api/caja/cuentas?cursor=&limite=` | Cuentas cobrables y bloqueadas con motivo, total/detalle histórico y destino. |
| `GET /api/caja/turnos/{id}/resumen` | Totales/estado para revisión y cierre, con límites de paginación para pendientes. |
| `GET /api/caja/comprobantes?cursor=&limite=` | Historial interno por autorización. |
| `GET /api/caja/comprobantes/{id}` | Snapshot persistido. |
| cuatro rutas `/api/caja/operaciones/...` | Prepare/execute/query/cancel descritas en §4. |
| `GET /api/caja/operaciones/{uuid}/evidencia-conciliacion` | Evidencia mínima autorizada para rechazos/cancelaciones: UUID, estado, tipo, IDs de cuenta/turno y distinción de barrera sin intención; no devuelve argumentos ni referencias. Por sí sola no sustituye la reconciliación de negocio. |

Errores estables: 400 `VALIDATION_ERROR`; 401 contratos de sesión 001; 403 `AUTH_PROHIBIDO`; 404 `RECURSO_NO_ENCONTRADO` genérico/no enumerativo; 409 `TURNO_YA_ABIERTO`, `TURNO_NO_ABIERTO`, `CUENTA_NO_COBRABLE`, `CUENTA_CAMBIO_REQUIERE_REVISION`, `CUENTA_YA_PAGADA`, `OPERACION_CLAVE_EN_USO`, `OPERACION_NO_TERMINAL`, `REFERENCIA_REQUIERE_CONFIRMACION`, `GRUPO_PAGADO_REQUIERE_CIERRE`, `VERSION_CONFLICT`; 413 `REQUEST_TOO_LARGE`; 500 genérico. Las respuestas de consulta solo muestran recursos autorizados. `traceId` en todos los problemas.

El DTO consulta incluye elegibilidad y causas como códigos estables/localizables; cliente no deduce elegibilidad de color ni estado solo. Totales server-authoritative. Rutas y paginación no alteran contrato 003 salvo cambio RF-06 explícito. Nunca se responde éxito antes de commit. Repetir GET no se considera resultado de operación.

El listado de cuentas usa cursor `caja-cuentas-id-v1` por ID ascendente y devuelve pedidos históricos y activos con estado explícito. Las causas incluyen `DESTINO_INVALIDO`, `CUENTA_YA_PAGADA`, `CUENTA_CANCELADA`, `ESTADO_INVALIDO`, `CUENTA_LEGADA_SIN_COMANDAS`, `COMANDAS_PENDIENTES`, `CUENTA_SIN_DETALLE`, `DETALLE_INCONSISTENTE`, `TOTAL_NO_COBRABLE`, `TOTAL_INCONSISTENTE` y `OPERACION_NO_TERMINAL`. La huella se entrega solo para una cuenta que pasa la evaluación; una intención preparada visible bloquea su presentación como cobrable. La fotografía MVCC no demuestra que no haya una escritura en curso aún invisible.

## 7. Auditoría, privacidad y logs

Eventos: `TurnoCajaAbierto`, `CobroRegistrado`, `TurnoCajaCerrado`, `OperacionCajaCancelada` e `IntervencionOperacionCaja`. Rechazos terminales de dominio se conservan en operación durable; no generan evento de auditoría de éxito. En `CobroRegistrado` incluir actor, turno/pedido/cobro/UUID, método y total; no referencia externa completa ni líneas. En cierre auditar conteo, esperado, diferencia y actor; motivo íntegro solo en registro de cierre con acceso Caja/Jefa. Auditoría según retención de dominio (al menos cinco años).

Evento y cambio de negocio comparten transacción. Si auditoría falla, rollback total. Auditoría append-only, retención base vigente al menos 5 años según domain, sin notas de pedido, referencia completa si se determina sensible, datos de tarjeta, tokens, cuerpos HTTP o secretos. Logs técnicos estructurados no duplican cada GET/polling y no son fuente de verdad.

## 8. Desktop / coordinación

- Módulo de caja con servicios REST/DTO propios, ViewModels y navegación solo para roles implementados. Seguir patrones de sesión y coordinador para serializar lecturas/mutaciones, descartar lecturas obsoletas y pausar con sesión.
- Caja no confía en polling para liberar bloqueo. Solo resultado terminal + reconciliación permiten avanzar.
- Confirmación de cobro muestra destino, líneas, total exacto, método y efectivo recibido/vuelto esperado; confirmación externa expresa “verificado fuera de El Carbonazo”. Para referencia repetida, muestra alcance de coincidencia y exige una segunda confirmación explícita.
- Cierre presenta efectivo esperado, conteo, diferencia, motivo obligatorio y cantidad de cuentas pendientes. No inferir pago de cuenta pendiente ni conciliación bancaria.
- Comprobante persistido permite consulta aunque cierre sesión/turno; diseño demuestra historicidad.
- Sin red, stale/solo lectura, sin cola de mutaciones. Polling no genera actividad de sesión. Timeout de lectura/escritura con políticas específicas; escritura queda incierta y bloquea.
- Evaluar SukiUI 7.0.1 real, contraste, teclado, foco, escalado, pantalla mínima y scroll; revisar visualmente mensajes de incertidumbre y pasos de confirmación.

## 9. Migración y despliegue de demostración

1. Inventario de solo lectura del esquema efectivo y rol de conexión, sin revelar connection string. No tocar la base HTTPS alternativa sin destino confirmado.
2. Añadir entidades, constraints, índices, secuencias y permisos mínimos en migración nueva. No agregar migración automática a startup.
3. Revisar SQL generado y un plan de despliegue/backup antes de aplicar. Aceptación destructiva únicamente sobre bases temporales `elcarbonazo_test_<uuid>` creadas por la suite.
4. Probar desde base vacía y esquema representativo 001+003 (mesa/grupo, cuenta activa con comandas en todos estados, legado sin comanda, pagado/cancelado y turnos/cobros ausentes); preservar IDs y estado; abortar en estructura incompatible.
5. Probar grants con rol runtime temporal; API runtime sin DDL. No cargar datos de catálogo nuevos por cobro; reutilizar datos 003 ficticios existentes.
6. Aplicación local no usa dinero real. Registrar explícitamente que API no aplica migraciones al arrancar. No modificar base persistente sin autorización y destino confirmado.

## 10. Plan de pruebas

### API / PostgreSQL

- Unitarias de elegibilidad, estados, importes exactos, métodos, referencia, diferencia, códigos, autorización.
- Integración real en PostgreSQL temporal para constraints, índices, snapshots, migración y rol runtime.
- Carrera dos aperturas; apertura/cierre; dos cobros misma cuenta; dos cuentas en un turno y cierre; cierre/cobro en ambos órdenes; envío/KDS/cobro; liberar mesa/cerrar grupo con cuenta pagada; nuevo envío a grupo pagado; mismo UUID con distinto payload/actor; repetición concurrente.
- Pérdida de respuesta antes/después de commit, reinicio API, abortos, savepoint y rollback, excepción de auditoría, cancelación/ejecución tardía; verificar una mutación como máximo y respuesta durable congruente.
- Concurrencia de total cambiado (nueva comanda, transición, cambio de estado), cuenta heredada y total cero; referencia duplicada con/ sin confirmación; efectivo insuficiente y vuelto; importes máximos/overflow.
- Cierre con diferencia/motivo, cuentas pendientes, referencias y totales por método; bloquear con operación no terminal y demostrar que GET no desbloquea.
- Comprobante snapshot histórico; no datos cliente/PAN; permisos negativos en todas las rutas; usuario/rol revocado; 401/403/404 no enumerables.
- Migración vacía/representativa/rollback y permisos solo en PostgreSQL temporal. No probar contra Development persistente.

### Desktop

- Fakes HTTP y reloj para flujos, DTO malformado/campo ausente, elegibilidad, stale reads, conflictos, cancelación, reinicio y cambio de sesión/rol.
- Escrituras no reintentan solas; diario local fallido impide submit; recuperación no ejecuta operación preparada; actor distinto bloquea; Jefa supervisora no aparece como autora original.
- Visual manual completa en ratón/teclado, foco, escalado, tamaño mínimo, contraste, navegación, pantallas de turno/cobro/comprobante, desconexión, timeout, permiso, motivo y confirmación externa.
- No declarar cobertura, LAN física ni latencia visual no medidas.

## 11. Criterios de calidad y aceptación

- Formato → build → tests API → tests Desktop → auditoría de paquetes, según AGENTS. AcceptanceHost se compila/prueba aparte si se modifica o utiliza.
- Tests API requieren PostgreSQL temporal con `ELCARBONAZO_TEST_ADMIN_CONNECTION` o configuración de Development; confirmar que la suite usa prefijo `elcarbonazo_test_` y no apunta a una base existente.
- No matar procesos del usuario ni borrar cambios preexistentes para compilar.
- Aceptación final requiere revisión visual del usuario y registro de evidencia; smoke o build no prueban aceptación de interfaz.
- Aceptación es únicamente perfil universitario de demostración local aislada; requisitos de uso real listados en `spec.md §7` continúan bloqueantes.

## 12. Aprobación y puertas de implementación

1. D0 técnico, contratos, modelo, límites, retención y política de coincidencia de referencia: **aprobados** en este documento.
2. Cambio contractual grupo pagado en 003: **aprobado documentalmente** en el adendum de `specs/003-pedidos-kds/spec.md`; debe implementarse y probarse en C3/C5 sin reescribir históricos.
3. C1 comienza con inspección de código/locks y PostgreSQL temporal. Si una prueba demuestra que el orden aprobado puede deadlockear o no preservar invariant, detener y revisar el plan antes de cambiar código de dominio.
4. ACL y `caja-journal.json`: instalación debe aprovisionar diario válido con UUID de instalación y estado explícito `SinPendientes`; no crear diario vacío para reparar ausencia/corrupción. Fallo bloquea mutaciones. Implementación requiere verificación Windows con ACL/reemplazo atómico.
5. La aprobación autoriza empezar C1 en código y pruebas; migración se aplica solamente a base temporal durante desarrollo. Ninguna base persistente puede modificarse sin confirmar destino y autorización expresa separada.
