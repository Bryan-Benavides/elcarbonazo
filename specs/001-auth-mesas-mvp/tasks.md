# Lista de Tareas — Módulo 001: Autenticación y Control de Mesas (MVP)

> Fuera de alcance: altas, roles, desactivación y restablecimiento de empleados se definirán en una especificación posterior; T1–T5 no deben agregar administración ad hoc ni SQL manual.

## Paquetes de pruebas fijados (T3.11)

Versiones estables consultadas en NuGet y verificadas mediante restore/build para .NET 10:

| Paquete | Versión |
|---------|---------|
| `Microsoft.NET.Test.Sdk` | `17.14.1` |
| `xunit` | `2.9.3` |
| `xunit.runner.visualstudio` | `3.1.5` |
| `Microsoft.AspNetCore.Mvc.Testing` | `10.0.0` |
| `coverlet.collector` | `6.0.4` |

- [x] **T1: Inicialización de la Solución y Proyectos (.NET 10)** ✅ COMPLETADA
  - Crear el proyecto `src/ElCarbonazo.Api` (ASP.NET Core Web API con `net10.0`).
  - Crear el proyecto `src/ElCarbonazo.Desktop` (Avalonia UI App con `net10.0`).
  - **Agregar ambos proyectos al solution file `ElCarbonazo.sln` en la raíz** (`dotnet sln add src/ElCarbonazo.Api/ElCarbonazo.Api.csproj src/ElCarbonazo.Desktop/ElCarbonazo.Desktop.csproj`).
  - Configurar directamente en `ElCarbonazo.Api.csproj` los paquetes con versión `10.0.0`:
    - `Microsoft.EntityFrameworkCore` (10.0.0)
    - `Microsoft.EntityFrameworkCore.Design` (10.0.0)
    - `Microsoft.EntityFrameworkCore.Tools` (10.0.0)
    - `Npgsql.EntityFrameworkCore.PostgreSQL` (10.0.0)
    - `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.0)
  - Configurar directamente en `ElCarbonazo.Desktop.csproj`:
    - `Avalonia` (12.1.4)
    - `Avalonia.Desktop` (12.1.4)
    - `Avalonia.Fonts.Inter` (12.1.4)
    - `SukiUI` (7.0.1)
    - `CommunityToolkit.Mvvm` (8.4.0)
  - *Criterio de Aceptación:* `dotnet build ElCarbonazo.sln` en la raíz finaliza con 0 errores y 0 advertencias.

- [x] **T2: Modelo de Dominio y Contexto de Datos (EF Core)** ✅ COMPLETADA
  - En `ElCarbonazo.Api`, crear las entidades `Usuario` y `Mesa` mapeadas exactamente según `docs/domain.md` (`id_usuario`, `nombre`, `contrasena_hash`, `rol`, `email`, `activo`; `id_mesa`, `id_grupo`, `numero`, `capacidad`, `estado`)[cite: 7].
  - Crear `AppDbContext` con la configuración de Fluent API y datos semilla (`HasData`) para las 15 mesas físicas[cite: 1, 2, 7].
  - Generar la migración inicial (`InitialCreate`)[cite: 1].
  - *Criterio de Aceptación:* La migración se aplica correctamente en PostgreSQL y las tablas `usuario` y `mesa` quedan creadas sin errores de nombres o tipos[cite: 1, 7].

- [x] **T3: Endpoints de API (Autenticación y Mesas)** ✅ COMPLETADA
  - Configurar serialización JSON **camelCase** en `Program.cs`: `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);`
  - Implementar `AuthController` con `POST /api/auth/login` (emisión JWT con claim `rol` usando nombres completos: 'Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor')
  - Implementar `MesasController` con:
    - `GET /api/mesas` → devuelve `idMesa`, `idGrupo`, `numero`, `capacidad`, `estado`
    - `PUT /api/mesas/{id}/estado` → valida transición, incluye regla "no liberar mesa con pedido pendiente" (verificar que no exista `pedido` con `estado ∈ {'Abierto', 'En Preparación', 'Listo'}` para esa mesa)
  - Proteger endpoints de mesas con `[Authorize]` + JWT Bearer
  - *Criterio de Aceptación:* Peticiones en Swagger/HTTP Client responden HTTP 200 con JSON camelCase idéntico al contrato en `plan.md`.

- [x] **T3.0: Refactor a Capas Internas de API** ✅ COMPLETADA
  - Dejar Controllers delgados y mover EF/reglas a servicios scoped con interfaces; mover criptografía a `Security/` y reglas puras a `Domain/`.
  - Mantener DTOs separados de entidades, opciones tipadas validadas al inicio y `AppDbContext` como unidad de trabajo sin repositorio genérico.
  - Inyectar `TimeProvider` y estandarizar reglas/contratos en UTC, sin depender de `DateTime.Now` ni de la zona local del servidor.
  - *Criterio de Aceptación:* Ningún Controller consulta EF ni contiene criptografía o reglas de dominio; los contratos existentes siguen funcionando.

- [x] **T3.1: Base de Datos de Grupos y Pedidos** ✅ COMPLETADA
  - Crear `GrupoMesa`, `GrupoMesaMiembro` y `Pedido` completos; conservar usuarios de creación/cierre, historial inmutable de miembros, `mesa.id_grupo` activo, destino XOR, FK `ON DELETE RESTRICT`, `NUMERIC(12,2)` e índices únicos parciales.
  - Agregar ambos DbSet y reemplazar la consulta SQL condicional de mesas por una consulta EF tipada.
  - Generar una migración posterior a `InitialCreate` que cree `pedido` en base vacía o adopte la tabla residual mediante preflight transaccional; ante incompatibilidad debe abortar con diagnóstico sin tocar datos ni reescribir migraciones aplicadas.
  - *Criterio de Aceptación:* El modelo impide pedidos ambiguos/activos duplicados y la regla de cuenta pendiente funciona igual en toda instalación.

- [x] **T3.2: Unicidad y Normalización de Usuarios** ✅ COMPLETADA
  - Agregar índices únicos y CHECK de Unicode NFC/minúsculas/Trim para nombre y email; agregar `intentos_fallidos`/`bloqueo_hasta`, normalizar en orden NFC→Trim→minúsculas antes de guardar/buscar y detectar colisiones antes de migrar.
  - Limitar email a 254 caracteres y validar sintaxis localmente sin DNS ni servicios externos.
  - Limitar nombre a 3–50 caracteres y validar letras/números Unicode con separadores internos simples ` `, `.`, `-` o `_`; abortar migración si datos existentes no cumplen.
  - Ajustar login para no distinguir mayúsculas y mantener errores genéricos.
  - Centralizar BCrypt con factor configurable, 12 por defecto, y rechazar al inicio valores fuera de 10–15.
  - Recalcular después de un login válido cualquier hash cuyo costo sea inferior al configurado.
  - Persistir bloqueo de login durante 5 minutos tras 5 fallos consecutivos; al vencer, reiniciar antes del siguiente intento, reiniciar también tras éxito y mantener respuesta externa genérica.
  - Generar al arranque un hash BCrypt señuelo con el costo vigente y usarlo para usuarios inexistentes, inactivos o bloqueados, sin persistirlo ni registrarlo.
  - Limitar login a 10 solicitudes por minuto/IP sin cola y rechazar excedentes antes de BCrypt.
  - *Criterio de Aceptación:* PostgreSQL y API impiden identidades duplicadas; `Admin`, `ADMIN` y `admin` representan al mismo usuario.

- [x] **T3.3: Bootstrap Seguro del Administrador** ✅ COMPLETADA
  - Eliminar credenciales conocidas de `HasData`; implementar `--bootstrap-admin` solo para tabla vacía o única fila heredada neutralizada, reutilizando esa fila/ID, y un seeder `admin/admin123` exclusivo de Development con la misma regla.
  - Mediante migración condicional, desactivar y neutralizar el hash solo de la fila que aún coincide exactamente con el seed conocido de `InitialCreate`; conservar su ID y no tocar cuentas modificadas.
  - Exigir entre 10 caracteres y 64 bytes UTF-8 para la contraseña de bootstrap, sin reglas de composición ni normalización; la credencial corta de Development es una excepción local explícita.
  - Rechazar contraseñas presentes en una lista local mínima y versionada de valores comunes, sin dependencias de Internet.
  - Preservar usuarios existentes, no registrar secretos y documentar instalación.
  - *Criterio de Aceptación:* Producción nueva no contiene credenciales conocidas y Development conserva acceso reproducible.

- [x] **T3.4: Contratos HTTP y ProblemDetails** ✅ COMPLETADA
  - Mantener `numero`, devolver `MesaDto` completo y configurar `ProblemDetails` global con `code` y `traceId`, incluido challenge JWT.
  - Configurar 400 `ValidationProblemDetails` con `VALIDATION_ERROR` y `errors` por campo camelCase, sin detallar fallos de credenciales.
  - Separar 401 (`AUTH_NO_AUTENTICADO`, `AUTH_ACCESS_TOKEN_EXPIRADO`, `AUTH_SESION_EXPIRADA`) de 403 (`AUTH_PROHIBIDO`); Desktop nunca renueva por falta de rol.
  - Responder 429 de login como ProblemDetails `AUTH_DEMASIADOS_INTENTOS` con `Retry-After`.
  - Actualizar HTTP Client y pruebas para camelCase y `application/problem+json`.
  - *Criterio de Aceptación:* Desktop interpreta cualquier error por `status` y `code`, sin analizar texto libre.

- [x] **T3.5: Sesiones Deslizantes y Revocación JWT** ✅ COMPLETADA
  - Crear `SesionUsuario` y `TokenRefreshSesion`, una sesión activa por usuario, historial de hashes SHA-256, JWT de 5 minutos con `sid`, inactividad de 15 minutos y máximo de 8 horas.
  - Implementar login ampliado, `POST /api/auth/refresh`, `POST /api/auth/logout` y validación inmediata de sesión en cada petición.
  - Rotar en una transacción marcando el hash actual como consumido e insertando el siguiente; revocar toda la sesión ante reuso comprobado. Desktop serializa refresh como operación single-flight.
  - Limpiar diariamente hashes de sesiones terminadas hace más de 30 días, sin afectar sesiones activas ni auditoría.
  - Firmar JWT con HS256; validar al inicio una clave Base64 de al menos 32 bytes, issuer y audience obligatorios.
  - Configurar y probar `ClockSkew` de 30 segundos.
  - Generar refresh tokens con 32 bytes de CSPRNG y Base64URL sin padding; persistir únicamente SHA-256.
  - Implementar `POST /api/auth/activity` sin rotación, limitado por contrato a interacción humana y excluido de auditoría; polling no renueva actividad.
  - Hacer activity idempotente: actualización condicional máxima de una por minuto/sesión y 200 sin escritura para llamadas adicionales.
  - Devolver `inactivityExpiresAt`, `absoluteSessionExpiresAt` y `serverTime` en login/refresh/activity, siempre en UTC.
  - Agregar migración y pruebas de expiración, actividad, rotación, reutilización y revocación.
  - *Criterio de Aceptación:* La sesión respeta inactividad/límite absoluto y cualquier revocación se aplica inmediatamente.

- [x] **T3.6: Invariantes y Transiciones de Dominio** ✅ COMPLETADA
  - Agregar CHECK para roles, estados, rangos, coherencia grupo/mesa, ciclo de grupo, importes y `total = subtotal + impuestos`.
  - Centralizar la matriz Libre→Reservada/Ocupada; Reservada→Libre/Ocupada; Ocupada→Libre sin pendientes; Agrupada solo por grupo.
  - Autorizar cambios de estado solo a Jefa, Encargado de Caja, Mesero y Supervisor; Personal de Cocina conserva acceso de solo lectura.
  - Tratar mismo estado con versión vigente como 200 sin escritura/auditoría; una versión obsoleta siempre responde 409 antes del no-op.
  - Documentar/probar que abrir pedido ocupa atómicamente y que finalizarlo solo habilita liberación explícita; el endpoint de apertura se implementará en su módulo funcional.
  - *Criterio de Aceptación:* API y PostgreSQL rechazan estados, roles, importes y transiciones incoherentes dentro del alcance implementado.

- [x] **T3.7: Concurrencia Optimista de Mesas** ✅ COMPLETADA
  - Mapear `xmin` como `uint Version`; incluirlo en DTO/request y responder 409 ante versiones obsoletas.
  - Exigir versiones de todas las mesas al agrupar y revertir la operación completa ante cualquier conflicto.
  - *Criterio de Aceptación:* Ningún cambio de mesa se sobrescribe silenciosamente.

- [x] **T3.8: Endpoints Transaccionales de Agrupación** ✅ COMPLETADA
  - Implementar POST/DELETE de grupos con 2–15 mesas libres, versiones válidas y transacciones; el cierre se bloquea por pedidos pendientes asociados a `id_grupo`.
  - Cerrar con locks `FOR UPDATE` en orden grupo→mesas por ID y revalidar dentro de la transacción; documentar el mismo orden para la futura apertura de pedidos.
  - Hacer DELETE idempotente: grupo existente ya cerrado responde 204 sin escritura/auditoría; ID inexistente responde 404.
  - Restringir modificación a Jefa, Encargado de Caja, Mesero y Supervisor; Cocina conserva solo lectura.
  - Impedir asignar `Agrupada` desde PUT de mesa o modificar individualmente una mesa agrupada.
  - Insertar miembros históricos al crear y conservarlos al cerrar; limpiar únicamente `mesa.id_grupo` como pertenencia activa.
  - Tomar `id_usuario` del claim autenticado para creador/cierre y persistirlo en la misma transacción.
  - Devolver 201 con datos del grupo y `MesaDto` completas, incluidas versiones nuevas.
  - *Criterio de Aceptación:* Creación/cierre son atómicos, preservan historial y responden 409 ante conflictos o mesas no elegibles.

- [x] **T3.9: Auditoría Persistente y Logs Estructurados** ✅ COMPLETADA
  - Crear `evento_auditoria` con columnas estables, FKs restrictivas, índices y `detalles` JSONB construido desde modelos permitidos; generar su migración.
  - Implementar auditoría PostgreSQL para eventos funcionales y de seguridad críticos, separada de los logs técnicos estructurados.
  - Auditar resultados de auth, bloqueos, revocaciones, bootstrap, cambios de mesa y creación/cierre de grupos; excluir GET, polling y reportes de actividad.
  - Auditar únicamente el primer 429 de login por IP/ventana como `LoginRateLimitActivado`; evitar una fila por cada rechazo excedente.
  - En login fallido sin usuario, guardar solo el nombre intentado normalizado NFC→Trim→minúsculas; nunca el request completo.
  - Capturar IP remota e `X-Installation-Id` UUID como metadatos, sin utilizarlos para autenticar o autorizar.
  - Guardar cada mutación y su auditoría en la misma transacción; revertir por completo si el evento no puede persistirse.
  - Tratar registros como append-only, sin endpoints de update/delete, y limitar el rol PostgreSQL de ejecución a `INSERT`/`SELECT` sobre auditoría.
  - Definir retención mínima de 5 años y documentar archivo/purga externa con credenciales administrativas separadas; no implementar purga en la API.
  - No exponer consulta de auditoría por API/UI en el módulo 001; verificar sus registros únicamente en pruebas de integración.
  - Impedir que ambos mecanismos registren contraseñas, tokens, claves, cadenas de conexión o secretos de certificados.
  - Configurar logs JSON rotativos más consola con paquetes estables, compatibles con .NET 10 y versiones exactas fijadas antes de instalar.
  - Aplicar rotación diaria, límite de 100 MB por archivo y retención de 30 días, manteniendo consola activa al alcanzar el límite.
  - Usar `Information` para aplicación y `Warning` para Microsoft/EF/Npgsql; excluir logs por cada GET/polling exitoso y conservar `traceId` en rechazos/errores.
  - *Criterio de Aceptación:* Los eventos auditables sobreviven a la rotación de logs y el diagnóstico técnico no expone secretos.

- [ ] **T3.10: Configuración Segura para Operación LAN**
  - Configurar hostname HTTPS, certificado confiable, `ASPNETCORE_URLS`, firewall LAN, plantillas sin secretos y `ApiBaseUrl` Desktop configurable.
  - Documentar migración explícita con rol administrativo y ejecutar la API con un rol runtime sin DDL; producción no aplica migraciones al arrancar.
  - Documentar `pg_dump` diario cifrado hacia disco/NAS separado, retención de 30 días y restauración mensual en una base aislada, con secretos fuera de Git.
  - Definir `%ProgramData%\ElCarbonazo\desktop.json`, generación de UUID por instalador y ACL de solo lectura para usuarios normales; permitir override por entorno solo para Development/pruebas.
  - Prohibir bypass TLS y probar desde un segundo equipo sin Internet.
  - *Criterio de Aceptación:* Desktop accede por hostname HTTPS sin secretos en Git ni URLs compiladas.

- [x] **T3.11: Pruebas Automatizadas de la API** ✅ COMPLETADA
  - Crear proyecto xUnit con versiones estables exactas verificadas para .NET 10, unitarias e integración sobre `elcarbonazo_test_<id>` temporal.
  - Cubrir migraciones, constraints, auth/sesiones, mesas, grupos, pedidos, concurrencia y ProblemDetails; exigir ≥80% en lógica crítica.
  - Ejecutar formato → build → unitarias → integración → auditoría de vulnerabilidades; nunca tocar la base de desarrollo.
  - *Criterio de Aceptación:* Toda la secuencia finaliza limpia, aislada y con cobertura crítica ≥80%.

- [x] **T4: Capa Desktop — Formulario de Login (SukiUI)** ✅ COMPLETADA
  - Crear `Services/ApiClientService.cs` en `ElCarbonazo.Desktop` para mantener access/refresh tokens solo en memoria, renovar por interacción humana y detener polling al expirar la sesión[cite: 3].
  - Aplicar timeouts por request: 5 s para lecturas y 10 s para login/refresh/mutaciones; nunca reintentar automáticamente una escritura con resultado desconocido.
  - Detectar interacción humana y reportarla mediante `POST /api/auth/activity` como máximo una vez por minuto, con envío diferido si ocurre durante el intervalo; polling no cuenta como actividad.
  - Implementar refresh single-flight solo por interacción humana; si polling detecta access token vencido, entrar en estado de sesión pausada sin renovar automáticamente.
  - Refrescar anticipadamente con margen de 60 s para acciones humanas; solicitudes concurrentes esperan la misma tarea de refresh.
  - Reintentar una sola vez acciones humanas rechazadas específicamente por `AUTH_ACCESS_TOKEN_EXPIRADO` después de refresh; nunca otros errores/timeouts.
  - Al cerrar normalmente, intentar logout con tiempo acotado sin bloquear la salida y limpiar siempre access/refresh tokens de memoria.
  - Mostrar aviso con cuenta regresiva un minuto antes de inactividad; solo Continuar renueva actividad y el vencimiento limpia sesión/navega a login.
  - Mostrar banner no extensible cinco minutos antes del límite absoluto y exigir nuevo login al vencer.
  - Leer `ApiBaseUrl` e UUID desde ProgramData, validar sin regenerar silenciosamente en producción y enviar `X-Installation-Id` en cada petición.
  - Crear `ConfigurationErrorView`/ViewModel no editable para configuración faltante o inválida; bloquear login y no usar fallback localhost en producción.
  - Crear `SecurityErrorView`/ViewModel para fallos TLS; detener tareas, limpiar tokens y prohibir bypass o tratamiento como offline recuperable.
  - Configurar logs Desktop JSON en ProgramData con 20 MB/día y 7 días, paquetes estables fijados, sin polling exitoso ni datos sensibles; separar ACL de logs y configuración.
  - Convertir instantes UTC solo al presentar usando `America/Managua` y la equivalencia de zona disponible en la plataforma.
  - Estimar desfase con `serverTime` y el punto medio de cada request auth para programar avisos sin confiar en el reloj local.
  - Crear `Views/LoginView.axaml` y `ViewModels/LoginViewModel.cs` aplicando los recursos del `Design System` (`ColorVino`, `ColorBeige`, etc.)[cite: 3].
  - *Criterio de Aceptación:* Al autenticar credenciales válidas contra la API, la aplicación guarda el token JWT y navega hacia `MesasView`[cite: 3].

- [ ] **T5: Capa Desktop — Mapa de Mesas (15 Mesas)**
  - **Contratos REST y aislamiento:** implementar `MesasApiService` para `GET /api/mesas` y `PUT /api/mesas/{id}/estado`, y `GruposMesaApiService` para `POST/DELETE /api/grupos-mesa`; DTOs Desktop propios con JSON camelCase, `numero` (no `numeroMesa`), `idGrupo` nullable y `version` sin dependencia de proyectos, EF Core, Npgsql ni entidades API. Usar `ISessionManager`/transporte existentes; no duplicar tokens, refresh, TLS ni políticas de timeout.
  - **Robustez de transporte previa a su consumo:** asegurar que el timeout de 5 s cubre envío, cabeceras y lectura completa/deserialización del body, no solo `ResponseHeadersRead`; clasificar cancelación/timeout/red luego de iniciar una mutación como resultado potencialmente incierto. Corregir el resultado de `SendHumanAsync` cuando expira el access token y el refresh no tiene éxito, para que nunca devuelva un `HttpResponseMessage` ya dispuesto; errores de sesión/TLS deben conservar su semántica terminal y no clasificarse como offline.
  - **Estado y coordinación:** un único coordinador cancelable/single-flight de polling, sin requests solapados y con respuesta satisfactoria aplicada solo si sigue vigente. Serializar las mutaciones; al comenzar una mutación invalidar/cancelar lecturas anteriores y descartar cualquier respuesta tardía que pudiera sobrescribir datos posteriores. Aplicar de inmediato los DTOs devueltos por PUT/POST; después de DELETE 204 conservar acciones bloqueadas hasta un GET exitoso. Nunca comparar `version` como reloj ni inferir orden entre versiones.
  - **Cadencia y observabilidad:** intervalo nominal de 2 s entre inicio de consultas saludables cuando no hay una consulta en curso; si una consulta excede el intervalo, iniciar la siguiente tan pronto termine la anterior, sin solaparla. Backoff de 2→5→10→30 s ante fallos recuperables, indicador de API desconectada, conservación de últimos datos y vuelta a 2 s después de un GET exitoso. Medir y documentar frescura desde el cambio confirmado por API hasta su representación en UI; la latencia observable incluye espera de polling, duración de GET y despacho/render (nominalmente 2 s más la latencia medida). No prometer un límite absoluto de 2 s ni solapar requests para simularlo.
  - **Offline e incertidumbre:** desconexión recuperable conserva última vista solo lectura; no encolar ni reintentar mutaciones. Tras un PUT/POST/DELETE cuyo envío pudo llegar al servidor pero no obtuvo respuesta concluyente (timeout, cancelación posterior al envío, fallo de red o respuesta 5xx), mostrar resultado incierto, bloquear nuevas mutaciones y reconciliar con GET; desbloquear solo tras respuesta satisfactoria. Cancelación anterior al envío no crea incertidumbre. Sesión `Paused` es distinta de offline y reanuda polling/mutaciones solo después de refresh por interacción humana exitoso; sesión expirada o TLS bloqueado navega según el estado global de sesión.
  - **Errores y concurrencia:** distinguir códigos HTTP `ProblemDetails`; `MESA_CONFLICTO_CONCURRENCIA` y `GRUPO_CONFLICTO_CONCURRENCIA` requieren GET inmediato, mensaje y nueva confirmación, sin reintento de la escritura. `MESA_CON_PEDIDO_PENDIENTE` y `GRUPO_CON_PEDIDOS_PENDIENTES` informan bloqueo por cuenta pendiente. `MESA_PERTENECE_A_GRUPO`, `GRUPO_MESAS_NO_ELEGIBLES` y demás errores de elegibilidad actualizan/reconcilian la vista y explican el rechazo; 401/403 no se traducen a offline. No asumir que `MesaDto` informa pedidos pendientes: la API es autoridad.
  - **Mapa e identidad:** crear `Views/MesasView.axaml` y `ViewModels/MesasViewModel.cs`; mostrar nombre/rol y cierre de sesión con confirmación, detención de tareas, logout best-effort, limpieza y retorno a LoginView. Renderizar `WrapPanel` centrado con 15 tarjetas de 160×160 px, botones circulares de 100×100 px y cuatro sillas decorativas. Crear `Converters/EstadoMesaColorConverter.cs`: Libre `#7CB342`, Ocupada `#8C2331`, Reservada `#E67E22`, Agrupada `#2E86AB`.
  - **Acciones y roles:** los cinco roles consultan; solo Jefa, Encargado de Caja, Mesero y Supervisor pueden cambiar estados/agrupar/cerrar grupos; Cocina es solo lectura. Acciones individuales: Libre→Reservada/Ocupada; Reservada→Libre/Ocupada; Ocupada→Libre con validación de pedidos pendientes en API. `Agrupada` solo se modifica mediante el flujo de grupo. Ocultar/deshabilitar mutaciones offline, con sesión pausada, reconciliación o resultado incierto.
  - **Agrupación:** botón explícito inicia selección de 2–15 mesas Libre; guardar IDs y resolver las versiones vigentes del último GET al confirmar `{ idMesa, version }`. Mantener polling durante selección y retirar con aviso IDs que dejen de ser elegibles. Mostrar contador y Confirmar/Cancelar. Al crear, aplicar inmediatamente todas las `MesaDto` de la respuesta. Al pulsar Agrupada, derivar ID y miembros activos de los datos actuales (la API no expone GET de grupo); roles autorizados pueden solicitar cierre con confirmación. Tras DELETE 204, bloquear acciones hasta GET satisfactorio.
  - **Criterios de aceptación T5:** las 15 mesas presentan número, capacidad, estado/color y datos de grupo disponibles; permisos y transiciones coinciden con contratos; no hay escrituras offline, requests solapados ni respuestas antiguas aplicadas; creación/cierre/conflictos/resultado incierto se reconcilian según lo indicado. Se verifica polling nominal de 2 s en LAN/API saludables y se mide/documenta la latencia de convergencia incluyendo GET y render; latencias fuera del presupuesto no se ocultan ni rompen single-flight.
  - Las pruebas automatizadas específicas de Desktop, cobertura ≥80% y checklist visual integral pertenecen a T6; T5 debe dejar los componentes testeables y los casos de aceptación documentados.

- [ ] **T6: Pruebas Automatizadas y Aceptación Desktop**
  - Crear `tests/ElCarbonazo.Desktop.Tests` con xUnit, referencia solo a Desktop y versiones estables exactas compatibles con .NET 10.
  - Probar servicios/ViewModels con HTTP, reloj y navegación falsos: login, refresh single-flight, actividad, avisos, logout, timeouts, polling/backoff, offline, TLS/configuración, agrupación y conflictos.
  - Verificar que el proyecto Desktop/tests no referencia EF Core, Npgsql ni la API.
  - Exigir cobertura ≥80% en servicios/ViewModels críticos, excluyendo XAML, code-behind mínimo, generado y composición.
  - Ejecutar checklist manual de XAML, dimensiones 160×160/100×100, sillas, colores, diálogos, selección, sesión pausada y errores bloqueantes.
  - Ejecutar formato → build → pruebas API → pruebas Desktop → auditoría de paquetes.
  - *Criterio de Aceptación:* Toda lógica Desktop crítica pasa pruebas unitarias y el checklist visual cumple el Design System sin acceso directo a PostgreSQL.
