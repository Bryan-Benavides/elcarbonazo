# Plan de Diseño Técnico — Módulo 001 (Alineado con docs/domain.md)

## 0. Arquitectura Interna de la API
- `Controllers/`: traduce HTTP a DTOs, invoca servicios y convierte resultados en respuestas; no contiene consultas EF ni reglas de negocio.
- `Services/`: interfaces e implementaciones para autenticación, sesiones, mesas, grupos y bootstrap. Los servicios coordinan reglas y transacciones usando `AppDbContext`.
- `Entities/`: entidades persistentes y navegación del dominio; no dependen de ASP.NET Core ni de DTOs.
- `Data/`: `AppDbContext`, configuraciones Fluent API y migraciones.
- `DTOs/`: contratos HTTP; nunca se exponen entidades EF directamente.
- `Security/`: emisión/validación JWT, generación/hash de refresh tokens y BCrypt.
- `Domain/`: constantes de roles/estados y reglas puras como la matriz de transiciones.
- `Configuration/`: opciones tipadas y validación de configuración al inicio.
- El backend inyecta `TimeProvider` y usa UTC para expiraciones y marcas de tiempo; queda prohibido basar reglas en `DateTime.Now` o en la zona local del servidor.
- No se implementa repositorio genérico sobre EF Core; `DbContext` ya cumple unidad de trabajo y los servicios son la frontera testeable.
- Servicios de aplicación y `AppDbContext` usan lifetime scoped; componentes puros sin estado pueden ser singleton.
- Fuera de alcance: CRUD/desactivación/reset de usuarios. Solo se implementa bootstrap de la primera Jefa; la gestión cotidiana requiere una especificación posterior.
- `Auditing/`: registra eventos funcionales y de seguridad en PostgreSQL; los logs estructurados quedan reservados para diagnóstico técnico.

## 1. Esquema de Base de Datos (PostgreSQL - 3FN)
> Fuente de verdad: `docs/domain.md`. Columnas y tipos exactos.

### Tabla: `usuario`
- `id_usuario` INT (PK, SERIAL)
- `nombre` VARCHAR(50) (NOT NULL)
- `contrasena_hash` VARCHAR(255) (NOT NULL)
- `rol` VARCHAR(50) (NOT NULL) -- Valores: 'Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor'
- `email` VARCHAR(254) (NOT NULL)
- `activo` BOOLEAN (NOT NULL, DEFAULT true)
- `intentos_fallidos` INT (NOT NULL, DEFAULT 0)
- `bloqueo_hasta` TIMESTAMPTZ (NULL)
- Índice único para `nombre` e índice único para `email`.
- CHECK de normalización: ambos valores están en Unicode NFC y cumplen `valor = lower(btrim(valor))`.
- CHECK para `rol` ∈ { `Jefa`, `Encargado de Caja`, `Mesero`, `Personal de Cocina`, `Supervisor` }.
- CHECK `intentos_fallidos BETWEEN 0 AND 5`.
- La API aplica `Normalize(NormalizationForm.FormC)`, `Trim()` y minúsculas invariantes, en ese orden, antes de guardar o buscar nombre/email.
- La API valida `nombre` con el patrón lógico `letras/números Unicode` separados opcionalmente por un único espacio, punto, guion o guion bajo; PostgreSQL refuerza longitud 3–50 y normalización.
- La API valida sintaxis de email y longitud máxima 254 sin DNS; PostgreSQL refuerza longitud, no vacío y normalización.

### Tabla: `mesa`
- `id_mesa` INT (PK, SERIAL)
- `id_grupo` INT (FK a `grupo_mesa.id_grupo`, NULL)
- `numero` INT (UNIQUE, NOT NULL) -- 1..15
- `capacidad` INT (NOT NULL)
- `estado` VARCHAR(20) (NOT NULL) -- Valores estrictos: 'Libre', 'Ocupada', 'Reservada', 'Agrupada'
- CHECK `numero BETWEEN 1 AND 15` y CHECK `capacidad BETWEEN 1 AND 20`.
- CHECK del conjunto de estados permitido.
- CHECK de coherencia: `estado = 'Agrupada'` si y solo si `id_grupo IS NOT NULL`.
- La entidad EF mapea PostgreSQL `xmin` como token de concurrencia `uint Version`; no se crea una columna adicional.

### Tabla: `grupo_mesa`
- `id_grupo` INT (PK, SERIAL)
- `id_usuario_creacion` INT (FK a `usuario.id_usuario`, NOT NULL, ON DELETE RESTRICT)
- `id_usuario_cierre` INT (FK a `usuario.id_usuario`, NULL, ON DELETE RESTRICT)
- `fecha_creacion` TIMESTAMPTZ (NOT NULL, DEFAULT CURRENT_TIMESTAMP)
- `fecha_cierre` TIMESTAMPTZ (NULL)
- `activo` BOOLEAN (NOT NULL, DEFAULT true)
- La FK `mesa.id_grupo` usa `ON DELETE RESTRICT`; los grupos cerrados se conservan como historial.
- CHECK temporal: un grupo activo tiene `fecha_cierre`/`id_usuario_cierre` nulos; uno inactivo tiene ambos informados.

### Tabla: `grupo_mesa_miembro`
- `id_grupo` INT (PK compuesta, FK a `grupo_mesa.id_grupo`, NOT NULL, ON DELETE RESTRICT)
- `id_mesa` INT (PK compuesta, FK a `mesa.id_mesa`, NOT NULL, ON DELETE RESTRICT)
- `fecha_asignacion` TIMESTAMPTZ (NOT NULL)
- La relación es append-only y conserva los integrantes después del cierre; `mesa.id_grupo` representa únicamente pertenencia activa.
- El rol PostgreSQL runtime recibe `INSERT`/`SELECT`, no `UPDATE`/`DELETE`, sobre esta tabla.

### Tabla: `sesion_usuario`
- `id_sesion` UUID (PK)
- `id_usuario` INT (FK a `usuario.id_usuario`, NOT NULL, ON DELETE RESTRICT)
- `fecha_creacion` TIMESTAMPTZ (NOT NULL)
- `ultima_actividad` TIMESTAMPTZ (NOT NULL)
- `expiracion_absoluta` TIMESTAMPTZ (NOT NULL)
- `fecha_revocacion` TIMESTAMPTZ (NULL)
- Índice único parcial por `id_usuario` donde `fecha_revocacion IS NULL` para permitir una sola sesión activa.

### Tabla: `token_refresh_sesion`
- `id_token` BIGINT (PK, BIGSERIAL)
- `id_sesion` UUID (FK a `sesion_usuario.id_sesion`, NOT NULL, ON DELETE RESTRICT)
- `token_hash` CHAR(64) (UNIQUE, NOT NULL) -- SHA-256 hexadecimal; nunca se guarda el token en claro.
- `fecha_emision` TIMESTAMPTZ (NOT NULL)
- `fecha_consumo` TIMESTAMPTZ (NULL)
- Índice único parcial por `id_sesion` donde `fecha_consumo IS NULL`, garantizando un solo refresh vigente por sesión.
- Cada refresh marca el token actual como consumido e inserta el siguiente dentro de la misma transacción.
- Un hash encontrado con `fecha_consumo` informada prueba reuso y provoca la revocación completa de la sesión.
- El valor entregado al cliente se genera con 32 bytes de `RandomNumberGenerator` y Base64URL sin padding; `token_hash` es SHA-256 del valor transportado.
- Una limpieza diaria elimina hashes únicamente cuando la sesión terminó por revocación o vencimiento efectivo hace más de 30 días; nunca elimina eventos de auditoría.

### Tabla: `pedido` (soporte de reglas de mesa y grupo)
- `id_pedido` INT (PK, SERIAL)
- `id_mesa` INT (FK a `mesa.id_mesa`, NULL)
- `id_grupo` INT (FK a `grupo_mesa.id_grupo`, NULL)
- `id_usuario` INT (FK a `usuario.id_usuario`, NOT NULL)
- `fecha_hora` TIMESTAMPTZ (NOT NULL)
- `subtotal` NUMERIC(12,2) (NOT NULL, moneda NIO)
- `impuestos` NUMERIC(12,2) (NOT NULL, moneda NIO)
- `total` NUMERIC(12,2) (NOT NULL, moneda NIO)
- `estado` VARCHAR(20) (NOT NULL) -- 'Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado'
- CHECK XOR: exactamente uno de `id_mesa` e `id_grupo` debe ser no nulo.
- Índice único parcial por `id_mesa` para estados activos (`Abierto`, `En Preparación`, `Listo`).
- Índice único parcial por `id_grupo` para los mismos estados activos.
- Todas las FK usan `ON DELETE RESTRICT` para conservar historial.
- CHECK del conjunto de estados permitido.
- CHECK de importes no negativos y CHECK `total = subtotal + impuestos`.
- Migración de adopción: si `public.pedido` ya existe, un bloque de preflight verifica columnas, tipos convertibles, PK/secuencia, FKs, estados, importes, destino de filas existentes y ausencia de pedidos activos duplicados. Solo entonces agrega o adapta lo faltante.
- El preflight y las alteraciones se ejecutan en la misma transacción. Una incompatibilidad lanza una excepción con diagnóstico accionable y revierte todo; nunca elimina, renombra ni trunca la tabla residual.

### Tabla: `evento_auditoria`
- `id_evento` BIGINT (PK, BIGSERIAL)
- `fecha_hora` TIMESTAMPTZ (NOT NULL)
- `tipo_evento` VARCHAR(50) (NOT NULL)
- `resultado` VARCHAR(20) (NOT NULL) -- conjunto controlado: `Exitoso`, `Rechazado`, `Error`.
- `id_usuario` INT (FK a `usuario.id_usuario`, NULL, ON DELETE RESTRICT)
- `id_sesion` UUID (FK a `sesion_usuario.id_sesion`, NULL, ON DELETE RESTRICT)
- `direccion_ip` INET (NULL) -- IP remota observada por la API.
- `id_instalacion` UUID (NULL) -- Identificador estable enviado por Desktop; no es una credencial.
- `tipo_entidad` VARCHAR(30) (NULL)
- `id_entidad` VARCHAR(64) (NULL) -- texto para admitir IDs enteros o UUID sin columnas alternativas.
- `trace_id` VARCHAR(64) (NOT NULL)
- `detalles` JSONB (NOT NULL, DEFAULT `{}`)
- Índices por `fecha_hora`, `(tipo_evento, fecha_hora)`, `(id_usuario, fecha_hora)` y `(tipo_entidad, id_entidad, fecha_hora)`.
- `detalles` se construye desde modelos permitidos por tipo de evento; queda prohibido serializar requests, entidades o excepciones completas.
- Para login fallido sin usuario identificado, el modelo permitido contiene solo `nombreIntentado` ya normalizado con NFC → Trim → minúsculas.
- Desktop genera una vez `idInstalacion`, lo guarda en configuración local y lo envía en `X-Installation-Id`. La API valida formato UUID y lo usa únicamente como metadato; su ausencia o valor no participa en autorización.
- La entidad se configura append-only; no existen operaciones de actualización/borrado en el contexto de ejecución.

## 2. Contrato de API (ASP.NET Core Web API)

### Convención de serialización JSON
**Todas las respuestas y requests usan camelCase** (configurar `JsonNamingPolicy.CamelCase` en `Program.cs`).
EF Core mapea a snake_case en BD; la serialización expone camelCase.

> **Perfil universitario:** los endpoints de mutación de mesas y grupos descritos abajo son los contratos actuales y se usan para T5. `specs/002-operaciones-seguras/` queda como propuesta futura; no se implementa ni deshabilita estos endpoints en este perfil.

---

- `POST /api/auth/login`
  - Body: `{ "nombre": "string", "contrasena": "string" }`
  - Normaliza `nombre` con Trim + minúsculas; el login no distingue mayúsculas.
- Después de 5 fallos consecutivos, rechaza nuevos intentos durante 5 minutos. Al vencer, reinicia contador/bloqueo antes de evaluar; un éxito también los reinicia.
  - Credenciales inválidas, cuenta inactiva y bloqueo usan la misma respuesta externa.
  - Rate limit por IP: 10 solicitudes por ventana de un minuto, `QueueLimit = 0`; excedentes no ejecutan BCrypt y responden 429 con `Retry-After`.
  - Revoca cualquier sesión activa anterior del usuario y crea una nueva sesión.
  - Response 200: `{ "accessToken": "string", "refreshToken": "string", "accessTokenExpiresAt": "date-time", "inactivityExpiresAt": "date-time", "absoluteSessionExpiresAt": "date-time", "serverTime": "date-time", "idUsuario": int, "nombre": "string", "rol": "string" }`

- `POST /api/auth/refresh`
  - Body: `{ "refreshToken": "string" }`
  - Solo Desktop lo invoca como consecuencia de interacción humana; el polling no lo usa.
  - Rechaza sesiones revocadas, con más de 15 minutos de inactividad o que superaron 8 horas.
  - Rota el refresh token, actualiza `ultima_actividad` y emite un JWT de 5 minutos.
  - Si detecta reuso de un token ya consumido, revoca toda la sesión y exige autenticación completa.
  - Response 200: mismo contrato de tokens y expiraciones del login.

- `POST /api/auth/activity`
  - Auth: Bearer; obtiene la sesión desde `sid`.
  - Desktop lo envía como máximo una vez por minuto y únicamente si hubo interacción humana desde el último reporte.
  - Actualiza `ultima_actividad` sin rotar tokens.
  - Response 200: `{ "inactivityExpiresAt": "date-time", "absoluteSessionExpiresAt": "date-time", "serverTime": "date-time" }`.
  - La API aplica una actualización condicional como máximo una vez por minuto y sesión; llamadas adicionales devuelven 200 con valores vigentes sin escritura.
  - Polling, timers sin interacción y tareas automáticas no pueden invocarlo.

- `POST /api/auth/logout`
  - Auth: Bearer; obtiene la sesión desde el claim `sid`.
  - Revoca inmediatamente la sesión y responde 204 sin contenido.
  - Desktop lo invoca como mejor esfuerzo al cerrar normalmente; el cierre no queda bloqueado si la API no responde y los tokens locales se limpian siempre.

### Validación JWT y sesión
- Cada JWT incluye `sub`, `rol` y `sid`; duración fija de 5 minutos.
- En cada petición protegida se valida firma, emisor, audiencia, expiración y que `sid` corresponda a una sesión no revocada y no vencida.
- `/api/auth/activity` y un refresh válido iniciado por interacción actualizan `ultima_actividad`; peticiones normales y polling no extienden la sesión.
- Inactividad máxima: 15 minutos. Duración absoluta máxima: 8 horas.
- Login, refresh y activity devuelven ambas expiraciones y `serverTime` en UTC; no se usa el campo ambiguo `sessionExpiresAt`.
- Desktop estima el desfase como `serverTime - puntoMedio(requestSentAt, responseReceivedAt)` y usa ese valor para cuentas regresivas; la autoridad de expiración sigue siendo la API.
- Desktop programa un aviso a `inactivityExpiresAt - 1 min` con cuenta regresiva. `Continuar` provoca refresh/activity según corresponda; `Cerrar sesión` o llegar a cero cancela tareas, limpia tokens y navega a LoginView.
- Desktop muestra desde `absoluteSessionExpiresAt - 5 min` un banner persistente sin opción de extensión; al vencer cancela tareas, limpia tokens y vuelve a LoginView.
- Si polling recibe `AUTH_ACCESS_TOKEN_EXPIRADO`, Desktop entra en estado `Sesión en pausa`, detiene polling y no confunde el caso con pérdida de red. La siguiente interacción intenta un único refresh; éxito reanuda y sesión vencida navega a login.
- Para requests provocados por interacción, `ApiClientService` refresca primero cuando `accessTokenExpiresAt - ahora <= 60 s`; un lock/tarea compartida garantiza single-flight y las demás solicitudes esperan el mismo resultado.
- Si una acción humana recibe 401 `AUTH_ACCESS_TOKEN_EXPIRADO`, el handler hace refresh single-flight y repite una sola vez. No repite 401 de sesión, 403, 409, 429, 5xx, cancelaciones ni timeouts.

- `GET /api/mesas`
  - Header: `Authorization: Bearer <token>`
  - Response 200: `[ { "idMesa": int, "idGrupo": int?, "numero": int, "capacidad": int, "estado": "string", "version": uint } ]`
  - `estado` ∈ { "Libre", "Ocupada", "Reservada", "Agrupada" }

- `PUT /api/mesas/{id}/estado`
  - Auth: Bearer; roles: `Jefa`, `Encargado de Caja`, `Mesero`, `Supervisor`. `Personal de Cocina` no puede modificar.
  - Body: `{ "estado": "string", "version": uint }` -- Valores: "Libre" | "Ocupada" | "Reservada"
  - `Agrupada` solo se asigna mediante `POST /api/grupos-mesa`.
  - Si la mesa pertenece a un grupo activo, rechaza el cambio con HTTP 409.
  - Si `version` no coincide con `xmin`, responde 409 `MESA_CONFLICTO_CONCURRENCIA`.
  - Después de validar versión, si `estado` ya coincide, responde 200 con el DTO actual sin `SaveChanges`, nueva versión ni evento de auditoría.
  - Response 200: `{ "idMesa": int, "idGrupo": int?, "numero": int, "capacidad": int, "estado": "string", "version": uint }`

- `POST /api/grupos-mesa`
  - Auth: Bearer; roles: `Jefa`, `Encargado de Caja`, `Mesero`, `Supervisor`.
  - Body: `{ "mesas": [{ "idMesa": int, "version": uint }, ...] }` -- Entre 2 y 15 IDs distintos.
  - Crea el grupo y actualiza todas las mesas en una sola transacción.
  - Solo acepta mesas `Libre`, sin grupo y sin pedidos pendientes.
  - Valida la versión de todas las mesas; cualquier conflicto responde 409 y revierte toda la operación.
  - Response 201: `{ "idGrupo": int, "idUsuarioCreacion": int, "fechaCreacion": "date-time", "activo": true, "mesas": [{ "idMesa": int, "idGrupo": int, "numero": int, "capacidad": int, "estado": "Agrupada", "version": uint }] }`

- `DELETE /api/grupos-mesa/{id}`
  - Auth: Bearer; roles: `Jefa`, `Encargado de Caja`, `Mesero`, `Supervisor`.
  - Dentro de la transacción adquiere `FOR UPDATE` sobre el grupo y luego sobre sus mesas en orden ascendente de `idMesa`.
  - Si el grupo existe pero ya está inactivo, responde 204 sin escritura ni auditoría; si el ID no existe, responde 404 `GRUPO_NO_ENCONTRADO`.
  - Bloquea con HTTP 409 si existe un pedido pendiente cuyo `idGrupo` corresponde al grupo; la cuenta compartida no referencia individualmente a sus mesas.
  - En una transacción deja las mesas `Libre`, limpia `idGrupo` y cierra el grupo sin eliminarlo.
  - Response 204 sin contenido.
  - Desktop ejecuta GET de mesas inmediatamente después del 204 y mantiene mutaciones bloqueadas hasta reconciliar versiones.

### Contrato de errores
- Toda respuesta 4xx/5xx usa `application/problem+json` con el formato estándar `ProblemDetails`.
- Además de `type`, `title`, `status`, `detail`, `instance` y `traceId`, incluye la extensión `code` con un identificador estable para Desktop.
- Validación de modelo usa 400 `ValidationProblemDetails`, extensión `code = VALIDATION_ERROR` y diccionario `errors` con nombres de campo camelCase; nunca se usa para revelar cuál parte de las credenciales fue incorrecta.
- 401 distingue `AUTH_NO_AUTENTICADO` (token ausente/inválido), `AUTH_ACCESS_TOKEN_EXPIRADO` (JWT vencido) y `AUTH_SESION_EXPIRADA` (sesión revocada, inactiva o fuera del límite absoluto).
- 403 usa `AUTH_PROHIBIDO` cuando el usuario está autenticado pero su rol no permite la operación. Desktop no intenta refresh ante 403.
- Códigos adicionales mínimos del módulo: `AUTH_CREDENCIALES_INVALIDAS`, `AUTH_REFRESH_INVALIDO`, `MESA_NO_ENCONTRADA`, `MESA_ESTADO_INVALIDO`, `MESA_CON_PEDIDO_PENDIENTE`, `MESA_PERTENECE_A_GRUPO`, `MESA_CONFLICTO_CONCURRENCIA`, `GRUPO_NO_ENCONTRADO`, `GRUPO_MESAS_NO_ELEGIBLES`, `GRUPO_CONFLICTO_CONCURRENCIA` y `GRUPO_CON_PEDIDOS_PENDIENTES`.
- 429 de login usa `AUTH_DEMASIADOS_INTENTOS` e incluye `Retry-After`.
- El campo JSON oficial para el número físico es `numero`; no se expone el alias `numeroMesa`.

## 3. Reglas de Dominio (Módulo 001)
- **Mesa Agrupada**: `Agrupada` exige `id_grupo` no nulo; los demás estados exigen `id_grupo` nulo.
- **Creación de grupo**: Requiere de 2 a 15 mesas distintas, todas `Libre`, sin grupo y sin pedidos pendientes.
- **Cierre de grupo**: Bloquea si el grupo tiene un pedido pendiente, conserva el registro como historial (`activo = false`, `fecha_cierre` informada) y deja todas sus mesas `Libre` y sin grupo.
- **Miembros históricos**: Crear grupo inserta `grupo_mesa_miembro` en la misma transacción. El cierre limpia solo la pertenencia activa en `mesa`; nunca elimina las filas históricas.
- **Orden de locks**: Para un grupo existente se bloquea `grupo_mesa` y después sus mesas por `id_mesa`; las operaciones futuras de pedido de grupo usan el mismo orden. La creación bloquea mesas por `id_mesa` antes de validar versiones.
- **No liberar mesa con cuenta pendiente**: Validar en `PUT /api/mesas/{id}/estado` que no exista `pedido` con `estado ∈ {'Abierto', 'En Preparación', 'Listo'}` para esa mesa.
- **Destino de pedido**: Un pedido apunta exclusivamente a `id_mesa` o `id_grupo`, nunca a ambos ni a ninguno.
- **Cuenta compartida**: Las mesas agrupadas usan un único pedido asociado a `id_grupo`.
- **Un pedido activo**: Cada mesa o grupo admite como máximo un pedido en estado `Abierto`, `En Preparación` o `Listo`.
- **Montos**: Todos los importes del pedido se almacenan en NIO como `NUMERIC(12,2)`; USD se calcula al facturar/pagar.
- **Historial**: Las relaciones de pedido con mesa, grupo y usuario usan borrado restringido.
- **Transiciones de mesa**: `Libre` → `Reservada`/`Ocupada`; `Reservada` → `Libre`/`Ocupada`; `Ocupada` → `Libre` solo sin pendientes. `Agrupada` se gestiona exclusivamente desde los endpoints de grupo.
- **Abrir pedido**: Cambia una mesa individual `Libre`/`Reservada` a `Ocupada` dentro de la misma transacción; un grupo permanece `Agrupada`.
- **Finalizar pedido**: `Pagado` o `Cancelado` habilita la liberación explícita, pero no modifica automáticamente la mesa ni el grupo.
- **Roles autorizados para MesasView**: `Jefa`, `Encargado de Caja`, `Mesero`, `Personal de Cocina`, `Supervisor` (nombres exactos como en BD).
- **Roles que modifican estados de mesa**: `Jefa`, `Encargado de Caja`, `Mesero`, `Supervisor`; `Personal de Cocina` solo consulta.
- **Roles que modifican agrupaciones**: `Jefa`, `Encargado de Caja`, `Mesero`, `Supervisor`; `Personal de Cocina` solo consulta.

## 4. Componentes Desktop (Avalonia UI + SukiUI)
- Mejora de presentación aprobada: alinear login y mapa con Carbón & Fuego; añadir leyenda, razones contextuales no especulativas, instrucciones de selección y confirmación central para cierre de grupo. Mantener botones deshabilitados para acciones no disponibles y API como autoridad; no inferir pedidos pendientes. Verificar teclado/foco y tamaños visualmente, sin introducir paquetes o code-behind de vistas.
- Extensión visual aprobada: imagen local `Assets/logo_cabonazo.jfif` como fondo atenuado del login; control MVVM de mostrar/ocultar contraseña usando `TextBox.RevealPassword`, reiniciado tras cada intento. Ajustar barra de `SukiWindow` con propiedades nativas de la versión instalada y acentos luminosos locales; probar minimizar/maximizar/restaurar y contraste en ejecución sin afirmar resultado solo por compilar.
- Ante el fallo reportado del botón de maximizar de SukiUI 7.0.1 con clic real en Windows, sustituir solo ese botón en la plantilla aplicada, preservando el estado y los demás controles. El reemplazo no ofrece el menú Snap Layout al pasar el puntero; confirmar la corrección con clic real y teclado en la configuración del usuario.
- T9: navegación en columna propia con transición breve y mando accesible; el mapa se redistribuye al abrirla. Tarjetas compactas y detalle lateral; cabecera con mensajes visibles, estado de consulta distinguible y totales derivados de la colección visible. Agrupación explícita con controles compactos adaptables. Usar variantes luminosas de la paleta en indicadores, sin inferir conectividad ni alterar permisos, polling o reglas. Verificar diseño real, estados, ratón y teclado en tamaño mínimo y maximizado.
- `Views/LoginView.axaml` ↔ `ViewModels/LoginViewModel.cs`
- `Views/ConfigurationErrorView.axaml` ↔ `ViewModels/ConfigurationErrorViewModel.cs`
- `Views/SecurityErrorView.axaml` ↔ `ViewModels/SecurityErrorViewModel.cs`
- `Views/MesasView.axaml` ↔ `ViewModels/MesasViewModel.cs`
- `Services/ApiClientService.cs` (transporte HTTP/TLS, `X-Installation-Id` y timeouts por operación; no conserva tokens ni renueva sesiones)
- `Services/SessionManager.cs` / `ISessionManager` (tokens en memoria, autenticación, refresh single-flight, actividad, logout y ciclo explícito de sesión)
- `Services/ServerClock.cs` / `IServerClock` (desfase UTC del servidor y conversión a Managua únicamente para presentación)
- `Services/MesasApiService.cs` (Consumo exclusivo de endpoints de mesa)
- `Services/GruposMesaApiService.cs` (Creación y cierre de grupos)
- `MesasView` mantiene `Agrupar mesas` habilitado solo para roles de modificación en condiciones válidas. El botón activa modo de selección, resalta únicamente mesas `Libre`, muestra `n seleccionadas`, habilita Confirmar desde 2 y ofrece Cancelar.
- Fuera del modo de selección, clic en una mesa individual abre detalle con acciones contextuales Reservar/Ocupar/Liberar según la matriz y el rol. Liberar requiere confirmación; para Cocina, modo offline y sesión pausada las acciones permanecen deshabilitadas con explicación contextual.
- Clic en una mesa `Agrupada` abre un detalle derivado de `idGrupo` y la colección actual. `Cerrar grupo` requiere rol autorizado y confirmación; Cocina puede verlo deshabilitado con explicación de solo lectura.
- El encabezado de MesasView muestra nombre, rol y `Cerrar sesión`. La acción confirmada cancela polling/actividad, intenta logout con tiempo acotado, limpia estado sensible y navega a LoginView incluso ante fallo de red.
- El modo de selección guarda `idMesa`, no copias congeladas del DTO. Polling continúa; al confirmar resuelve versiones actuales y elimina con aviso IDs cuyo estado/grupo ya no sea elegible.
- Los DTOs de Desktop son contratos cliente propios y no referencian tipos del proyecto API. El JSON usa camelCase; `numero` es el campo físico. `MesaDto.Version` se conserva como `uint` opaco para concurrencia y no se interpreta como timestamp.
- **Propiedad de requests:** `MesasViewModel` o un servicio coordinador de mesas mantiene como máximo un GET activo y un flujo de mutación serializado. Cada resultado de GET lleva una generación local; al iniciar una mutación se invalida la generación previa. Una respuesta de polling de generación obsoleta se descarta. Las respuestas de escritura no se sustituyen por polling anterior; las respuestas `PUT`/`POST` satisfactorias se aplican inmediatamente.
- **Delete y reconciliación:** un `DELETE` de grupo satisfactorio (204) no contiene DTOs; se mantienen bloqueadas las mutaciones hasta un `GET /api/mesas` satisfactorio posterior. Un conflicto también invalida los datos previos y solicita GET antes de permitir confirmar otra mutación.
- **Timeout e incertidumbre:** timeout, cancelación después del envío, fallo de transporte o 5xx no demuestran que la API no aplicó el cambio. Mostrar resultado incierto y bloquear mutaciones; consultar mesas y exigir revisión y reconocimiento explícitos antes de permitir nuevas acciones. No se reintenta automáticamente. Un GET no demuestra que la escritura haya terminado: esta limitación debe mostrarse y documentarse; el perfil no ofrece garantía de recuperación definitiva ni es apto para operación real bajo este fallo.
- **Problemas API:** conflicto de versión (`MESA_CONFLICTO_CONCURRENCIA`, `GRUPO_CONFLICTO_CONCURRENCIA`) implica GET inmediato y nueva confirmación. Los códigos de pedido pendiente muestran rechazo de dominio; los errores de elegibilidad reconcilian el estado y notifican. 401/403, sesión pausada, sesión expirada, TLS y fallos de red mantienen semánticas distintas. La API sigue siendo autoridad sobre pedidos pendientes, ya que `MesaDto` no incluye esa información.
- **Cadencia medible:** polling nominal cada 2 s entre inicios de solicitudes saludables, sin solapamiento; una consulta que sigue activa impide iniciar otra y la siguiente empieza al completar la anterior. Backoff 2→5→10→30 s ante errores recuperables. La latencia de convergencia es intervalo de espera + GET + despacho/render y se mide/documenta de extremo a extremo. El objetivo nominal es 2 s más la latencia medida de GET y presentación; no se afirma un SLA absoluto ≤2 s ni se solapan requests para simularlo.
- **Prerrequisitos de integración de sesión:** `ISessionManager.SendHumanAsync` no debe retornar una respuesta ya dispuesta cuando refresh falla; timeout de `ApiClientService` debe cubrir también lectura del contenido de respuesta. Mantener `SecurityBlocked`/`Expired` terminales y no clasificar sus errores como desconexión recuperable. Estos comportamientos deben quedar cubiertos por pruebas de integración Desktop planificadas en T6.
- `Converters/EstadoMesaColorConverter.cs` — Mapea estado → color del Design System:
  - `Libre` → `ColorMesaLibre` `#10B981`
  - `Ocupada` → `ColorMesaOcupada` `#EF4444`
  - `Reservada` → `ColorMesaReservada` `#F59E0B`
  - `Agrupada` → `ColorMesaAgrupada` `#06B6D4`

## 5. Configuración Técnica Requerida
- **Program.cs (API)**: `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);`
- **Polling MesasView**: bucle cancelable single-flight, sin solapar solicitudes. Mantiene cadencia nominal de 2 s entre inicios cuando la consulta anterior ya terminó; si excede el intervalo, inicia la siguiente al completar. Aplica backoff 2→5→10→30 s tras fallos; conserva los últimos datos, muestra estado sin conexión y vuelve a 2 s al recuperarse. La convergencia observable incluye espera, duración GET y despacho/render; objetivo nominal: 2 s más la latencia medida, sin prometer ≤2 s absoluto.
- **Modo sin conexión**: la colección visible permanece como último estado conocido, pero comandos de cambio/agrupación quedan deshabilitados hasta un GET exitoso. No existe cola de mutaciones offline.
- **Conflictos 409**: `MESA_CONFLICTO_CONCURRENCIA` y `GRUPO_CONFLICTO_CONCURRENCIA` fuerzan GET inmediato, mensaje al usuario y nueva confirmación; nunca se reintenta la mutación obsoleta.
- **Sesión en pausa**: es distinta de “sin conexión”. JWT vencido durante polling detiene consultas y mutaciones sin refresh automático; interacción humana dispara refresh single-flight y, si funciona, reanuda polling.
- **Timeouts Desktop**: 5 s para GET/polling y 10 s para login, refresh y mutaciones mediante cancelación por request. Una mutación que vence no se reintenta automáticamente; se informa la incertidumbre, se consulta mesas y se pide revisión humana sin afirmar que GET confirma el resultado definitivo.
- **Seed estructural**: 15 mesas (`numero` 1..15, `capacidad` 4, `estado` 'Libre', `id_grupo` NULL). Las migraciones no incluyen contraseñas conocidas.
- **Bootstrap de producción**: `--bootstrap-admin` crea la primera `Jefa` cuando `usuario` está vacío o contiene únicamente la fila heredada neutralizada. En ese caso actualiza y reactiva esa fila conservando su ID. Cualquier otro usuario bloquea el comando.
- **Contraseñas**: toda contraseña nueva o restablecida fuera de `Development` requiere al menos 10 caracteres y admite como máximo 64 bytes UTF-8; no se imponen categorías de composición. Se usa literalmente, sin Trim ni normalización. `--bootstrap-admin` valida estos límites antes de BCrypt para evitar el truncamiento de 72 bytes.
- **BCrypt**: opción tipada `PasswordHashing:WorkFactor`, valor predeterminado 12 y rango válido 10–15; la API falla al iniciar si queda fuera del rango. Cada hash contiene su propia sal y costo.
- **Rehash oportunista**: después de verificar credenciales válidas, si el hash existente usa un costo inferior, se reemplaza por uno generado con el costo configurado antes de crear la sesión.
- **Lista de rechazo offline**: un recurso local, pequeño y versionado contiene contraseñas comunes u obvias para el negocio. Altas, restablecimientos y bootstrap comparan contra ella sin modificar la contraseña literal enviada a BCrypt ni consultar servicios externos.
- **Protección de login**: `intentos_fallidos` y `bloqueo_hasta` se persisten en `usuario`. El quinto fallo fija un bloqueo de 5 minutos; al vencer se limpian ambos antes de contar el siguiente intento, y un login válido también los reinicia. La respuesta nunca confirma si el usuario existe o está bloqueado.
- **Mitigación temporal**: al iniciar se genera una vez un hash BCrypt señuelo con el costo configurado. Usuario inexistente, inactivo o bloqueado ejecuta `Verify` contra ese hash antes de devolver el mismo error genérico; el hash señuelo no se persiste ni registra.
- **Datos de Development**: un inicializador exclusivo de `Development` puede crear `admin/admin123` si no existe ningún usuario o reutilizar la única fila heredada neutralizada; nunca se ejecuta en otros entornos.
- **Seed heredado**: una migración condicional actúa solo si `id_usuario`, nombre, email y hash coinciden exactamente con el registro conocido de `InitialCreate`; conserva la fila, fija `activo = false` y reemplaza el hash por un marcador que el flujo de login nunca verifica. Si cualquier valor fue modificado, no toca la cuenta.
- **Publicación LAN**: Desarrollo conserva localhost; cada instalación define `ASPNETCORE_URLS` para el hostname/puerto LAN sin fijar una IP en código.
- **HTTPS offline**: Kestrel usa un certificado cuyo SAN contiene `elcarbonazo-api.local`; el certificado raíz/intermedio se instala en el almacén confiable de cada equipo Desktop.
- **Secretos del certificado**: Ruta y contraseña se suministran mediante `Kestrel__Certificates__Default__Path` y `Kestrel__Certificates__Default__Password` (o almacén equivalente), nunca en Git.
- **Desktop Windows**: el instalador crea `%ProgramData%\ElCarbonazo\desktop.json` con `ApiBaseUrl` e `idInstalacion`; ACL permite lectura a usuarios normales y escritura solo a instalador/administrador. `ApiBaseUrl` admite override por entorno para Development/pruebas.
- **Configuración inválida**: en producción se navega a `ConfigurationErrorView`, que muestra la ruta esperada y orientación administrativa sin campos editables; no existe fallback a localhost.
- **Identificador Desktop**: el instalador genera una vez el UUID no secreto por terminal, enviado como `X-Installation-Id`; la aplicación no lo regenera silenciosamente si falta o es inválido en producción.
- **Zona horaria**: contratos y `TIMESTAMPTZ` representan UTC. Desktop convierte al presentar con el identificador IANA `America/Managua` y su equivalencia de plataforma; nunca modifica el instante recibido.
- **Validación TLS**: Desktop usa la validación normal del sistema operativo; queda prohibido aceptar cualquier certificado o desactivar errores TLS.
- **Fallo TLS**: errores de confianza, vigencia u hostname navegan a `SecurityErrorView`, cancelan polling/actividad, limpian tokens y no usan backoff de conectividad ni ofrecen bypass.
- **Plantillas**: Versionar archivos `.example` sin secretos para conexión PostgreSQL, JWT, certificado y URL de API; los archivos locales reales permanecen ignorados.
- **JWT**: HS256 con `Jwt:Key` codificada en Base64 y al menos 32 bytes aleatorios al decodificar; `Jwt:Issuer` y `Jwt:Audience` son obligatorios. Las opciones se validan al inicio y producción falla cerrada ante valores ausentes o inválidos.
- **Expiración JWT**: `TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30)`; no conservar el valor predeterminado de cinco minutos.
- **Migraciones de producción**: un paso explícito de instalación ejecuta `dotnet ef database update` (o el bundle equivalente) con un rol administrativo. La API no llama `Database.Migrate()` en producción.
- **Rol PostgreSQL runtime**: sin DDL; recibe solo los permisos DML y de secuencias necesarios. En `evento_auditoria` queda limitado a `INSERT`/`SELECT`. Las credenciales de migración no se configuran en el servicio operativo.
- **Respaldos**: una tarea externa ejecuta `pg_dump` diariamente con credenciales dedicadas de solo respaldo, cifra el archivo y lo escribe en otro disco o NAS local. Conserva 30 días; una restauración mensual se prueba en una base aislada que nunca reemplaza producción.
- **Secretos de respaldo**: contraseña PostgreSQL y clave de cifrado se suministran fuera de Git y no se escriben en logs ni junto al archivo respaldado.

## 6. Auditoría y Observabilidad
- La auditoría persistente y los logs técnicos estructurados son mecanismos separados.
- La tabla de auditoría es la fuente de verdad para eventos funcionales y de seguridad críticos.
- Cada evento usa columnas estables para fecha, tipo, resultado, actor, sesión, entidad y `traceId`; un `JSONB` limitado contiene solo cambios permitidos por ese tipo.
- Eventos mínimos: login exitoso/fallido, bloqueo, refresh exitoso/rechazado, logout, revocación por nuevo login, bootstrap, cambio de estado de mesa y creación/cierre de grupo.
- Rate limit: se inserta `LoginRateLimitActivado` solo para el primer 429 de cada par IP/ventana de un minuto; rechazos posteriores no generan auditoría y pueden muestrearse en logs.
- `GET`, polling, reportes de actividad y demás lecturas quedan excluidos de auditoría; sus fallos técnicos pueden aparecer en logs sin cuerpos ni tokens.
- Cada cambio funcional, incluido el cambio de sesión o contador de intentos, comparte transacción con su evento. Un fallo de auditoría revierte el cambio y produce un `ProblemDetails` interno sin detalles sensibles.
- La entidad de auditoría es inmutable y no tiene endpoints de edición/borrado. El rol PostgreSQL de ejecución recibe únicamente `INSERT` y `SELECT` sobre esa tabla; correcciones y anulaciones se agregan como eventos nuevos.
- Retención mínima en la base operativa: 5 años. No hay purga automática en la API; un procedimiento administrativo separado archiva y elimina registros vencidos con un rol distinto al de ejecución.
- El módulo 001 no expone endpoints ni UI para consultar auditoría. Un módulo posterior definirá paginación, filtros y autorización para Jefa/Supervisor; esta fase solo captura y verifica eventos.
- Los logs cubren arranque, configuración inválida, excepciones, latencia y diagnóstico, sin incluir secretos ni cuerpos sensibles.
- En producción, el proveedor escribe eventos estructurados JSON en archivos rotativos y en consola. Las versiones exactas del proveedor y sink se verifican para .NET 10 y se fijan antes de instalar; no se aceptan prereleases.
- Rotación: un archivo por día, límite de 100 MB por archivo y retención de 30 archivos diarios. Al alcanzar el límite se detiene la escritura de ese archivo hasta la siguiente rotación, mientras consola permanece activa.
- Niveles de producción: código de la aplicación en `Information`; categorías Microsoft, EF Core y Npgsql en `Warning`. Se excluye logging de requests GET exitosos y polling; mutaciones lentas, rechazos y errores conservan `traceId`.
- Desktop escribe JSON en `%ProgramData%\ElCarbonazo\logs`, 20 MB máximos por archivo diario y 7 días. La ACL permite escribir solo en `logs`, no modificar `desktop.json`; registra arranque, configuración/TLS, transición online↔offline, timeouts y errores con `traceId`, nunca polling exitoso ni datos sensibles.
- Las dependencias de logging Desktop también usan versiones estables exactas verificadas antes de instalar.

## 7. Estrategia de Pruebas
- Proyecto `tests/ElCarbonazo.Api.Tests` con xUnit y referencias directas únicamente a la API.
- Proyecto `tests/ElCarbonazo.Desktop.Tests` con xUnit y referencia únicamente a Desktop; no referencia EF Core, Npgsql ni la API.
- Fijar en `tasks.md` las versiones exactas de xUnit, runner, `Microsoft.AspNetCore.Mvc.Testing` y cobertura después de verificar compatibilidad con .NET 10; no usar prerelease.
- Pruebas unitarias para normalización, transiciones, cálculo de expiraciones, rotación/revocación y reglas de mesas, grupos y pedidos.
- Pruebas de integración para migraciones, restricciones PostgreSQL, endpoints HTTP, JWT/sesiones, concurrencia y `ProblemDetails`.
- Pruebas Desktop con `HttpMessageHandler`, `TimeProvider` y navegación falsos para login, refresh single-flight, actividad, avisos, logout, timeouts, polling/backoff, modo offline, TLS/configuración, agrupación y conflictos.
- XAML, distribución de tarjetas rectangulares, colores, estados visuales y diálogos se verifican manualmente; no se agrega automatización UI al MVP.
- Cada ejecución crea una base `elcarbonazo_test_<identificador>`, aplica todas las migraciones y la elimina al finalizar; queda prohibido apuntar a `elcarbonazo` o reutilizar la base de desarrollo.
- La conexión administrativa de pruebas se suministra por variable de entorno no versionada. El fixture rechaza cualquier nombre que no comience con `elcarbonazo_test_`.
- Para el perfil universitario se ejecutan pruebas existentes y pruebas focalizadas de servicios/ViewModels y flujos críticos. La cobertura mínima de 80% para API y Desktop es objetivo previo a producción, no condición de aceptación del prototipo.
- Verificación requerida antes de T4: formato → build → pruebas unitarias → pruebas de integración → auditoría de paquetes vulnerables.
- Verificación final tras T5: formato → build → pruebas API → pruebas Desktop → checklist visual → auditoría de paquetes.
