# El Carbonazo — Documentación técnica del sistema implementado

> **Perfil declarado:** demostración universitaria local de un POS (punto de venta) para restaurante. No está autorizada para dinero real ni para operar el restaurante (ver `docs/constitution.md` y `README.md`).
>
> **Convenciones de este documento:**
> - **Hecho** = comprobado en código, en `specs/<módulo>/{spec,plan,tasks}.md` o en `docs/validacion-*.md`.
> - **Pendiente** = declarado explícitamente como no validado o no implementado en esas fuentes.
> - Los módulos se citan siempre por carpeta completa en `specs/` porque la numeración tiene duplicados (ver Anexo D).
> - Fecha de referencia: 17 de noviembre de 2026.

---

## 1. Entorno de desarrollo y herramientas utilizadas

### 1.1 Entorno

| Elemento | Valor | Evidencia |
|---|---|---|
| Sistema operativo de desarrollo y ejecución | Windows (el cliente es `WinExe` y los scripts de soporte son PowerShell) | `ElCarbonazo.Desktop.csproj` (`OutputType WinExe`), `scripts/*.ps1` |
| .NET | **.NET 10** (`net10.0` en todos los proyectos) | Todos los `.csproj` |
| Base de datos | **PostgreSQL 15 o superior** (local) | `README.md` ("Requiere .NET 10 SDK y PostgreSQL 15 o superior") |
| Idioma de las interfaces en desarrollo | Español (`Locale="es-ES"` del tema, identificadores y textos en C#) | `App.axaml`, código fuente |
| Modo de ejecución | Offline-first; sin dependencias de Internet en ejecución | `docs/constitution.md` |

### 1.2 Herramientas y CLI

| Herramienta | Uso | Evidencia |
|---|---|---|
| `dotnet` CLI | `format`, `build`, `test`, `run` secuencia aceptada del proyecto (`AGENTS.md`: formato → build → pruebas API → pruebas Desktop → auditoría) | `AGENTS.md` |
| `dotnet-ef` **10.0.14** (versión fijada, `rollForward: false`) | Gestión de migraciones; ejecutado desde `src/ElCarbonazo.Api` | `dotnet-tools.json` |
| `dotnet list ElCarbonazo.sln package --vulnerable --include-transitive` | Auditoría de vulnerabilidades de paquetes (incluidos transitivos) | `AGENTS.md` |
| PowerShell | `scripts/iniciar-api-demo.ps1` (inicia API de la demo preparada), `scripts/instalar-diario-caja.ps1` (diario protegido de caja), `scripts/validar-catalogo-defensa.py` (+ variante de prueba) para el catálogo de defensa | `scripts/` |
| `tests/ElCarbonazo.AcceptanceHost` | Anfitrión de aceptación **fuera de la solución** (requiere build Debug previo de la API); crea y limpia una base PostgreSQL temporal por ejecución | `ElCarbonazo.sln` (no lo incluye), su `.csproj` |
| Análisis estático | `Directory.Build.props` activa `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel latest` y `Nullable enable` en toda la solución | `Directory.Build.props` |

### 1.3 Pruebas

| Suite | Marco y paquetes | Evidencia |
|---|---|---|
| `tests/ElCarbonazo.Api.Tests` | xunit 2.9.3, Test SDK 17.14.1, runner 3.1.5, `Microsoft.AspNetCore.Mvc.Testing` 10.0.0, coverlet 6.0.4 | su `.csproj` |
| `tests/ElCarbonazo.Desktop.Tests` | xunit 2.9.3, Test SDK 17.14.1, runner 3.1.5 | su `.csproj` |
| Aislamiento de datos en pruebas | Bases temporales `elcarbonazo_test_<uuid>` con creación/eliminación propia; conexión por `ELCARBONAZO_TEST_ADMIN_CONNECTION` o el entorno Development | `AGENTS.md` |

Regresión más reciente reportada en las validaciones: **~163–165 pruebas API y ~267–268 pruebas Desktop aprobadas**, con formato, compilación (0 advertencias) y auditoría de paquetes sin hallazgos (`docs/validacion-d6.md`).

---

## 2. Lenguajes de programación, frameworks y tecnologías seleccionadas

### 2.1 Lenguajes

- **C#** (línea .NET 10; lenguaje C# 14 incluido en la plataforma), con `Nullable` habilitado en toda la solución.
- **XAML** (Avalonia) para las vistas, con **bindings compilados por defecto** (`AvaloniaUseCompiledBindingsByDefault=true`): el binding se resuelve en tiempo de compilación en lugar de ejecuto.

### 2.2 API (servidor)

| Tecnología | Versión | Rol |
|---|---|---|
| ASP.NET Core (Web SDK) | 10.0 | API REST; controllers, middleware, autenticación |
| Entity Framework Core | 10.0.0 | ORM sobre PostgreSQL |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | Proveedor ADO/EF para PostgreSQL |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.0 | Autenticación por JWT |
| BCrypt.Net-Next | 4.0.3 | Hash de contraseñas (work factor configurado: 12) |
| System.Security.Cryptography.Pkcs | 10.0.14 | Criptografía para la autorización HMAC del módulo 006-auditoria |

### 2.3 Escritorio (cliente)

| Tecnología | Versión | Rol |
|---|---|---|
| Avalonia / Avalonia.Desktop / Avalonia.Fonts.Inter | 12.1.4 | Framework de UI multiplataforma (en ejecución: Windows), tipografía Inter |
| SukiUI | 7.0.1 | Sistema de componentes y tema (SukiTheme, SukiWindow, GlassCard) compatible con Avalonia 12 |
| CommunityToolkit.Mvvm | 8.4.0 | Base de MVVM (ViewModelBase, ObservableProperty, RelayCommand) |

Las dependencias se declaran **con versión fija directa en cada `.csproj`**; no se actualizan sin verificar compatibilidad (`AGENTS.md`).

### 2.4 Selección

El stack es inquebrantable según `docs/constitution.md` (principios, conjunto de tecnologías y paleta); los módulos no eligen tecnología propia: consumen lo que la constitución fija. La separación cliente/servidor por REST es una decisión de constitución, no de conveniencia.

---

## 3. Arquitectura y estructura del proyecto implementado

### 3.1 Arquitectura general

```
┌─────────────────────────────────────────────┐
│  ElCarbonazo.Desktop (Avalonia 12 + SukiUI) │  MVVM estricto; bindings compilados
│  Views ⇄ ViewModels ⇄ Services (HTTP)       │  Code-behind limitado a InitializeComponent()
└──────────────────────┬──────────────────────┘
                       │  REST JSON (HTTPS; header X-Installation-Id)
                       ▼
┌─────────────────────────────────────────────┐
│  ElCarbonazo.Api (ASP.NET Core 10)          │  Controllers → Services → EF Core
│  Autenticación JWT · Rate limiting · Audit   │  Máquinas de estado en Domain
└──────────────────────┬──────────────────────┘
                       ▼
               PostgreSQL (local)
```

**Reglas de arquitectura (constitución y `AGENTS.md`):**
- La **API es la autoridad** para permisos, transiciones y cuentas pendientes; el Desktop nunca decide, solo representa y serializa.
- **Desktop no referencía EF Core ni Npgsql**: todo acceso a datos por REST (verificado: su `.csproj` solo referencia Avalonia, SukiUI y CommunityToolkit.Mvvm).
- Los **controllers delegan** reglas y persistencia en servicios; `Program.cs` concentra configuración, autenticación, bootstrap y middleware.
- **Offline-first**: sin dependencias de Internet en ejecución.

### 3.2 Solución

`ElCarbonazo.sln` contiene 4 proyectos en dos grupos:

| Proyecto | Tipo | Responsabilidad |
|---|---|---|
| `src/ElCarbonazo.Api` | Web | API REST + EF Core (único que toca la base de datos) |
| `src/ElCarbonazo.Desktop` | WinExe | Cliente Avalonia (solo HTTP hacia la API) |
| `tests/ElCarbonazo.Api.Tests` | Test | Pruebas de API (21 clases) |
| `tests/ElCarbonazo.Desktop.Tests` | Test | Pruebas de Desktop (28 archivos) |

Además, **fuera de la solución**: `tests/ElCarbonazo.AcceptanceHost` (aceptación end-to-end sobre base temporal; ver §7.5).

### 3.3 Estructura de `ElCarbonazo.Api`

| Carpeta | Contenido (verificado) |
|---|---|
| `Program.cs` | Punto único de arranque: servicios, autenticación, rate limiting, middleware, modos administrativos CLI |
| `Configuration/` | `JwtSettings`, `PasswordHashingOptions` (opciones validadas en arranque) |
| `Controllers/` | 14 controllers (catálogo completo en §7.2) |
| `Domain/` | Reglas y máquinas de estado puras: `MesaStates`/`MesaStateTransitions`, `ComandaStates`, `PedidoStates`, `CajaStates`, `CajaElegibilidad`, `CajaOperacionCanonica`, `UserRoles`, `SessionSecurityPolicy`, `LoginSecurityPolicy`, `BootstrapPasswordPolicy`, `ProductoCategories`, `UserIdentity` |
| `Entities/` | 19 clases de entidad (ver §4.3) |
| `Data/` | `AppDbContext` + configuraciones de modelo por módulo (`Inventario`, `Reservaciones`, `Cancelaciones`) |
| `Migrations/` | 14 migraciones (ver §4.2) |
| `Security/` | JWT, hash, bootstrap, auditoría, HMAC (ver §8) |
| `Services/` | ~40 servicios por dominio (auth, mesas, grupos, pedidos/envíos, cocina, caja ×9, inventario/recetas, reservaciones, reporte, anulaciones ×4) |
| `DTOs/` | Contratos de entrada/salida (camelCase) |
| `Http/` | Respuestas problem+json normalizadas |
| `Logging/` | Registrador de archivos JSON diario |
| `appsettings*.json` | Configuración por entorno |

### 3.4 Estructura de `ElCarbonazo.Desktop`

| Carpeta | Contenido (verificado) |
|---|---|
| `App.axaml` / `App.axaml.cs` | Tema SukiUI, paleta de diseño, plantillas de datos; ensamblaje de transporte/sesión/navegación/shell |
| `MainWindow.axaml` | Ventana principal `SukiWindow` con barra de título `GlassCard` |
| `Views/` | 10 vistas (ver §6.3) |
| `ViewModels/` | 15 clases (una por vista + `ShellViewModel`, `ViewModelBase` + parciales de Inventario y Mesas) |
| `Services/` | Transporte HTTP, sesión, coordinadores de escrituras/incertidumbre, diarios de recuperación, navegación (ver §7.3) |
| `Models/` | Contratos por dominio reflejando la API (`AuthContracts`, `MesaContracts`, `ReservacionContracts`, `SessionSnapshot`, etc.) |
| `Converters/`, `Controls/`, `Design/`, `Assets/` | Recursos de presentación |
| `app.manifest` | Manifiesto de application Windows |

---

## 4. Implementación y conexión de la base de datos

### 4.1 Motor, proveedor y cadena de conexión

- **PostgreSQL 15 o superior**, consumido por **EF Core 10** con el proveedor **Npgsql** (`options.UseNpgsql(...)` en `Program.cs`).
- Cadena de conexión bajo `ConnectionStrings:DefaultConnection`. En **Development** la conexión se define por instalación (la guía `README.md` indica mantenerla **fuera de Git**, por ejemplo mediante variable de entorno `ConnectionStrings__DefaultConnection`); el repositorio incluye una plantilla en `appsettings.Development.json`.
- **La API no aplica migraciones automáticamente.** La aplicación de esquema es explícita: `dotnet ef database update` ejecutado desde `src/ElCarbonazo.Api` contra la base confirmada, con respaldo previo de cualquier base existente.

### 4.2 Histórico de migraciones (14, verificado en `src/ElCarbonazo.Api/Migrations/`)

| # | Migración | Alcance temático |
|---|---|---|
| 1 | `20261102161454_InitialCreate` | Esquema inicial |
| 2 | `20261102231543_AddGroupsAndOrders` | Grupos de mesa y pedidos |
| 3 | `20261102232435_HardenUserIdentities` | Endurecimiento de identidades |
| 4 | `20261103040917_SecureAdminBootstrap` | Bootstrap seguro de administrador |
| 5 | `20261103043022_AddUserSessions` | Sesiones de usuario y refresh |
| 6 | `20261103043853_EnforceDomainInvariants` | Invariantes de dominio a nivel de base |
| 7 | `20261103044652_MapMesaXminConcurrency` | Concurrency por `xmin` en mesa |
| 8 | `20261103045920_AddAuditEvents` | Auditoría |
| 9 | `20261103050000_EnforceAuditAppendOnly` | Auditoría append-only |
| 10 | `20261106142048_AddPedidosKdsPersistence` | Persistencia de pedidos/KDS (003) |
| 11 | `20261109224603_AddCajaPersistence` | Persistencia de caja (004) |
| 12 | `20261114185615_AddInventarioEscandallo` | Inventario y escandallo (005) |
| 13 | `20261115164448_AddAuditoriaCancelacionesSeguridad` | Anulaciones con seguridad (006-auditoria, módulo **pausado**) |
| 14 | `20261117012501_AddReservaciones` | Reservaciones (006-reservaciones) |

Las migraciones 13 y 14 fueron aplicadas el 15 y 17 de noviembre de 2026, respectivamente, a la base local `elcarbonazo` **con autorización explícita** registrada en `docs/bitacora-errores-y-lecciones.md` y en los `tasks.md` de sus módulos.

### 4.3 Modelo de datos

`AppDbContext` expone 19 entidades (verificadas en `Entities/`), organizadas por módulo:

| Módulo | Entidades |
|---|---|
| Identidad y sesión (001) | `Usuario`, `SesionUsuario`, `TokenRefreshSesion`, `EventoAuditoria` |
| Mesas y grupos (001) | `Mesa`, `GrupoMesa`, `GrupoMesaMiembro` |
| Pedidos y cocina (003) | `Pedido`, `DetallePedido`, `EnvioPedido`, `Comanda`, `Producto` |
| Caja (004) | `TurnoCaja`, `OperacionCaja`, `Cobro`, `ComprobanteCobroLinea` |
| Inventario (005) | `Inventario` (ingredientes, movimientos y recetas) |
| Reservaciones (006-reservaciones) | `Reservacion` |
| Anulaciones (006-auditoria, pausado) | `Cancelaciones` |

Reglas del modelo fijadas por `docs/domain.md` (diccionario autoritativo; p. ej. `mesa.numero`, no `numero_mesa`):
- Estados de **mesa**: `Libre`, `Ocupada`, `Reservada`, `Agrupada`; solo mesas libres sin grupo se agrupan (`MesaStates`, `GruposMesaService`).
- Estados de **comanda**: `Abierta → En Preparación → Lista` (lista ≠ retirada ≠ pago) (`ComandaStates`).
- Estados de **pedido**: `Abierto`, `En Preparación`, `Listo`, `Pagado`, `Cancelado` (`PedidoStates`).
- **Caja**: turno `Abierto`/`Cerrado`; operación `Preparada → Completada | Rechazada | Cancelada`; métodos `Efectivo`, `Tarjeta`, `Transferencia` (`CajaStates`).
- Concurrency: `Mesa` usa `xmin` (migración 7) y control por versión; no se infiere secuencia temporal comparando `version`/`xmin`.

### 4.4 Estrategias de datos implementadas

- **Auditoría append-only** (`EventoAuditoria` + `AuditRecorder`): todo hecho relevante (login, rate limit activado, operaciones de caja, anulaciones, inventario) se registra como evento inmutable; la migración 9 lo impone a nivel de base.
- **Siembra de Development**: al arrancar en Development, `IAdminBootstrapService.SeedDevelopmentAsync` crea o reutiliza la cuenta de demostración (`admin/admin123`, documentada en `docs/installation.md`); **sin** condiciones de seguridad equivalentes en producción.
- **Bootstrap de producción**: `--bootstrap-admin` interactiva con política de contraseña (`BootstrapPasswordPolicy`).
- **Cargas específicas bajo triple condición** (Development + confirmación exacta de base + directorio/datos explícitos): `--seed-demo-catalog`, `--seed-catalogo-defensa` (ver `Program.cs`); el seeder de defensa exige además exactamente **una Jefa activa** como actor.
- **Pruebas**: cada suite crea `elcarbonazo_test_<uuid>` aislado y lo elimina al terminar; nunca se dirige a una base persistente.

### 4.5 Estado actual de la base local `elcarbonazo`

Esquema al día (14 migraciones). Contiene el **catálogo de defensa** de la Fase 1 de `specs/entrega-defensa-local`: 90 productos, 116 ingredientes, 90 recetas activas, 452 líneas de receta, stock inicial por `EntradaCompra`, 1 pedido abierto, más usuarios, mesas y pedidos históricos. **Regla:** ninguna migración ni carga a base persistente se documenta ni se realiza sin autorización explícita y registro en bitácora.

---

## 5. Desarrollo de los módulos y funcionalidades principales

El desarrollo siguió el proceso SDD del proyecto: cada módulo existe como carpeta en `specs/<módulo>/` con `spec.md`, `plan.md` y `tasks.md`; **nada se implementó sin especificación** y el estado efectivo se lee de `tasks.md`.

### 5.1 `specs/001-auth-mesas-mvp` — Autenticación y mesas (base del sistema)

**Implementado y aceptado** para el perfil universitario local (quedan tareas de perfil de producción pendientes en su `tasks.md`: T3.10, T7, T8, T9).

Funcionalidades realizadas:
- **Autenticación**: login con hash BCrypt (work factor 12), emisión de access token JWT (5 min) + refresh token rotatorio, renovación de sesión por actividad (`POST /activity`, registro al menos cada minuto) y logout con revocación.
- **Sesión**: doble vida (inactividad 15 min / tope absoluto 8 h), revocación verificada en cada petición (ver §8.2), 5 roles: `Jefa`, `Encargado de Caja`, `Mesero`, `Personal de Cocina`, `Supervisor` (`UserRoles`).
- **Mapa de mesas**: listado paginado con estado textual en tarjetas; cambio de estado `PUT {id}/estado` validado contra la máquina de estado `MesaStateTransitions` y la concurrencia por versión/`xmin`.
- **Grupos de mesa**: agrupación **solo de mesas libres sin grupo** y desagrupación; las cuentas de un grupo se heredan de solo lectura (`GruposMesaService`).
- **Autorización por rol** en cada endpoint (ver §8.2); la API decide, el Desktop solo representa.

### 5.2 `specs/002-operaciones-seguras` — Pospuesto, no implementado

Propuesta de operaciones seguras adicionales. **No contiene código implementado**; permanece pospuesta y separada del alcance del perfil universitario. Se documenta como no implementada.

### 5.3 `specs/003-pedidos-kds` — Pedidos de salón y cocina (KDS)

**Implementado y aceptado** (evidencia `docs/validacion-d4.md`, `d5.md`, `d6.md`).

Implementado paso a paso:
1. **Catálogo**: `ProductosController` + `ProductosService` (página de catálogo consumida por el pedido).
2. **Borrador y envío**: el mesero arma el pedido (ítems, cantidades, notas) y lo envía con `POST pedidos/envios` — **idempotente** por clave de envío (`EnviosService`, `EnvioPedido`). La validación de stock/receta está acoplada al envío (ver 5.5).
3. **Cuenta y comandas**: `GET pedidos/activo` devuelve la cuenta (individual o de grupo) con sus comandas; una cuenta de otro turno es **heredada y de solo lectura** (`CuentasService`, `CocinaService`).
4. **Cola de cocina**: `GET cocina/cola` (FIFO, hasta 100 visibles con aviso) y transición `PUT cocina/comandas/{id}/estado` `Abierta → En Preparación → Lista`; **lista no equivale a retirada ni a pago**.
5. **Reglas de datos**: ante conflicto, la decisión se repite tras actualizar; ante respuesta incierta, **sin reintento** (bloquear y reconciliar).
6. **Aceptación**: D3 (API) → D4 (salón: conexión/catálogo, borrador, envío, cuenta heredada, botón deshabilitado, interacción) → D5 (cocina: cola, transiciones, teclado/táctil, foco, FIFO, reconexión, concurrencia) → D6 (integración de dos terminales). Cerrar dejó pendiente únicamente la confirmación visual del botón Cocina con su nuevo estilo (ver Anexo C).

### 5.4 `specs/004-caja-cobros` — Caja y cobros

**Implementado y aceptado** (evidencia `docs/validacion-004.md`); la tarea **C6** permanece pendiente.

Implementado paso a paso:
1. **Turnos**: apertura y cierre de turno por `Encargado de Caja`/`Jefa` (`TurnoCaja`, `CajaTurnosService`, `CajaAperturaExecutor`/`CajaCierreExecutor`); consulta del turno actual y resumen por turno.
2. **Operaciones de cobro**: Efectivo, Tarjeta y Transferencia sobre la cuenta de una mesa (`OperacionCaja`, `CajaCobroExecutor`, `CajaOperacionesService`). El sistema **registra** el cobro; **no procesa pagos ni verifica bancos**; el comprobante es interno, no fiscal.
3. **`SendCajaOnceAsync` y diario**: cada operación registra su UUID, propietario y tipo en el diario local **antes** de enviar; se ejecuta **solo tras preparación recién observada** y no se rearma durante recuperación. El diario no lleva payload, referencias ni secretos, y **nunca se crea vacío** para reparar incertidumbre.
4. **Comprobantes**: generación y consulta de comprobantes con líneas (`CajaComprobantesService`).
5. **Recuperación**: `CajaRecoveryCoordinator` (Desktop) + `CajaRecoveryView`; el resultado terminal se conserva hasta aplicar la fotografía de negocio y conciliar; cambio de sesión o resultado incierto conserva el bloqueo.
6. **Límites declarados**: tarjeta/transferencia requieren **verificación humana externa**; coincidencia de referencia → rechazo durable, reconciliación y nueva confirmación explícita; la recuperación no es durable entre terminales ni tras reinicio (requiere revisión humana).

### 5.5 `specs/005-inventario-escandallo` — Inventario y escandallo

**Implementado y aceptado** (evidencia `docs/validacion-005.md`); adenda del catálogo de defensa cargada.

Implementado paso a paso:
1. **Ingredientes**: alta, edición y listado (`InventarioService`, `InventarioConsultasService`).
2. **Movimientos**: entradas (p. ej. `EntradaCompra`) y consumo; historial consultable.
3. **Recetas**: receta activa por producto con líneas (ingrediente, cantidad, unidad) (`RecetasService`).
4. **Escandallo**: al enviar una comanda, consume el stock según la receta **atómicamente con el envío** (según `specs/005-inventario-escandallo/plan.md`); **caja no vuelve a descontar**. Sin receta activa o sin stock: el envío se rechaza.
5. **Alertas**: avisos por stock (`GET inventario/alertas`).
6. **Límite declarado**: la migración **no prepara** recetas ni stock de demostración; no se asume que una base persistente esté preparada (por eso existe el seeder `--seed-catalogo-defensa` para la defensa).
7. **Incertidumbre**: `InventarioEscrituraCoordinator` comparte el bloqueo de escrituras administrativas inciertas durante la sesión, incluso al reabrir la vista; **un GET no lo desbloquea**.

### 5.6 `specs/006-auditoria-cancelaciones-seguridad` — Anulaciones con auditoría (PAUSADO)

**Estado: PAUSADO por decisión explícita de la usuaria, con avance parcial conservado localmente.** No es funcionalidad aceptada y así se debe presentar en toda documentación.

Avance parcial (existe en código y la migración 13 está aplicada):
- Autorización de anulaciones con **HMAC** (`AnulacionAutorizacionService`, `HmacAutorizacionProvisioner`/`Recovery`/`Guard`/`Lock`, modos CLI `--provisionar-hmac-autorizacion` / `--recuperar-hmac-autorizacion`).
- Solicitud y **ejecución de anulación** (`AnulacionEjecucionService`, `EjecutarAnulacionRequest`), **recuperación** (`AnulacionRecuperacionService`) y consultas (`AnulacionesConsultasService`, eventos).
- **Alertas KDS** de cancelación (`GET cocina/alertas-cancelacion`, acuse desde el KDS) y snapshots en envío.

### 5.7 `specs/006-reservaciones-mesas` — Reservaciones de mesa

**Aprobado e implementado** (migración 14 aplicada el 17/11/2026 con autorización). Falta la revisión visual final (Anexo C).

Implementado: disponibilidad por franja/fecha (`GET reservaciones/disponibilidad`), listado paginado, creación y cancelación de reservas (`ReservacionesService`), con el estado `Reservada` agregado al ciclo de la mesa.

### 5.8 `specs/007-reporte-ventas` — Reporte de ventas

**Aprobado e implementado**; falta validación visual (Anexo C).

Implementado: consulta de reporte de ventas del turno/día (`ReporteVentasController` + `ReporteVentasService`), agrupado por lo requerido en su `spec.md`.

### 5.9 `specs/entrega-defensa-local` — Cierre y entrega para defensa (en curso)

- **Fase 1 (hecho)**: catálogo de defensa validado y cargado en `elcarbonazo` vía `--seed-catalogo-defensa`: 90 productos, 116 ingredientes, 90 recetas activas, 452 líneas; stock inicial por `EntradaCompra`; 1 pedido abierto. El seeder exige Development, confirmación exacta de la base y una única Jefa activa.
- **Fase 2 (pendiente)**: publicación/entrega (ver `tasks.md` del módulo).

---

## 6. Implementación de interfaces y formularios

### 6.1 Patrón y reglas

- **MVVM estricto** sobre Avalonia 12: `View` (XAML) ⇄ `ViewModel` (C#); dependencias inyectadas en el ensamblaje de `App.axaml.cs`; **bindings compilados por defecto** (error de compilación si el binding es inválido, no fallo en ejecuto).
- **Code-behind limitado a `InitializeComponent()`** (regla de constitución; verificado en `Views/*.axaml.cs`).
- MVVM con **CommunityToolkit.Mvvm 8.4.0** (`ObservableObject`, comando); cada vista tiene su `ViewModel` y los casos de escritura complejos se extraen a parciales (p. ej. `InventarioLecturaViewModel.Escritura.cs`, `MesasViewModel.Grupos.cs`).
- Los **ViewModels no tocan datos**: llaman a servicios de capa `Services` del Desktop que hablan REST (§7.3).

### 6.2 Tema y diseño

- **SukiUI 7.0.1** aplicado globalmente: `<suki:SukiTheme ThemeColor="Red" Locale="es-ES" />` en `App.axaml`, con `RequestedThemeVariant="Light"`; ventana con `SukiWindow` y barra de título `GlassCard` (`MainWindow.axaml`).
- **Paleta del proyecto** definida en `App.axaml` (propuesta aprobada en `docs/diseno-visual-propuesta.md`):
  - Identidad ("Carbón & Fuego"): fondo principal `#1E1E1E`, panel `#2D2D30`, borde `#3F3F46`, texto `#F3F4F6`/`#9CA3AF`, acento fuego `#FF5722`.
  - Sistema del mapa: vino `#8C2331`, carbón `#2A2421`, hueso `#F8F5F0`, beige `#E8E0D5`.
  - **Estado de mesa con color + texto**: Libre `#7CB342`, Ocupada `#8C2331`, Reservada `#E67E22`, Agrupada `#2E86AB`.
- **UI aprobada**: mesas en tarjetas rectangulares oscuras con **estado textual** (el color nunca sustituye al texto); validación de contraste y estados reales de plantillas SukiUI, no solo compilación (`AGENTS.md`).

### 6.3 Vistas implementadas (10 + shell, verificado en `Views/`)

| Vista | Funcionalidad | Rol(s) principal |
|---|---|---|
| `LoginView` | Credenciales, estados de error (bloqueo, sesión), credencial Development documentada | Todos |
| `MesasView` | Mapa de mesas en tarjetas con estado textual; agrupar/desagrupar; apertura de pedidos | Mesero, Supervisor, Jefa, Cocina (consulta no mutante) |
| `PedidosSalonView` | Catálogo, borrador, confirmar/envío (idempotente), cuenta individual/grupo heredada de solo lectura, botón deshabilitado sin permiso/datos | Mesero, Supervisor, Jefa |
| `CocinaView` | KDS: cola FIFO (máx. 100, aviso si hay más), detalle con notas, transiciones `Abierta → En Preparación → Lista`, alertas de cancelación (006-auditoria, pausado), horas en UTC | Personal de Cocina, Supervisor, Jefa |
| `CajaRecoveryView` | Recuperación del diario de caja: operaciones pendientes, conciliación, resultado terminal | Encargado de Caja, Jefa |
| `InventarioLecturaView` | Ingredientes, movimientos, alertas de escandallo, recetas (escrituras coordinadas) | Jefa, Supervisor (según permisos) |
| `ReservacionesView` | Disponibilidad, listado, crear y cancelar reserva | Mesero/Supervisor/Jefa (según permisos) |
| `ReporteVentasView` | Reporte de ventas del turno/día | Jefa, Supervisor |
| `ConfigurationErrorView` | `desktop.json` ausente/inválido (ruta, UUID), indicación de remedio | — |
| `SecurityErrorView` | Error de token/sesión con distinción de causa (autenticación, ruta, servidor, datos) | — |

La **navegación lateral** (`NavigationService`) solo expone destinos implementados y visibles para el rol del usuario autenticado; la API mantiene la autorización como autoridad final.

### 6.4 Formularios y estados

- Formularios con validación **en cliente** (estado de control, botones deshabilitados con causa) y **en servidor** (revalidación completa: 400 problem+json con errores por campo; ver §8.4).
- Estados explícitos implementados en las vistas: vacío, cargando, error (con diagnóstico sin filtrar excepciones/credenciales, corregido en D4 según `docs/validacion-d4.md`), incierto (bloqueo de acciones hasta reconciliación).

---

## 7. Integración de los diferentes componentes del sistema

### 7.1 Contrato REST

- JSON en **camelCase** (policy global de serialización en `Program.cs` y `ConfigureHttpJsonOptions`).
- **Errores normalizados** en `application/problem+json` con `code` y `traceId` (`Http/ApiProblemDetails.cs`):
  - `400 VALIDATION_ERROR` (campos inválidos), `404 HTTP_NO_ENCONTRADO`, `405 HTTP_METODO_NO_PERMITIDO`;
  - `401 AUTH_SESION_EXPIRADA` / `AUTH_ACCESS_TOKEN_EXPIRADO` / `AUTH_NO_AUTENTICADO`;
  - `403 AUTH_PROHIBIDO` (rol insuficiente);
  - `429 AUTH_DEMASIADOS_INTENTOS` (rate limit, con `Retry-After`).
- **Header `X-Installation-Id`**: si se envía, debe ser un único UUID válido; de lo contrario 400 (middleware en `Program.cs`). El Desktop lo envía en cada petición.

### 7.2 Catálogo de endpoints (verificado por lectura de los 14 controllers)

| Controller | Endpoints |
|---|---|
| `AuthController` | `POST login`, `POST refresh`, `POST activity`, `POST logout` |
| `MesasController` | `GET /` (página), `PUT {id}/estado` |
| `GruposMesaController` | `POST /` (agrupar), `DELETE {id}` (desagrupar) |
| `PedidosController` | `POST envios` (idempotente), `GET activo` |
| `CocinaController` | `GET cola`, `PUT comandas/{id}/estado`, `GET alertas-cancelacion`, `POST anulaciones/{id}/acuse` |
| `CajaCuentasController` | `GET /` (cuentas del turno) |
| `CajaTurnosController` | `GET turno/actual`, `GET turnos/{id}/resumen` |
| `CajaOperacionesController` | `PUT {id}`, `POST {id}/ejecucion`, `GET {id}`, `POST {id}/cancelacion`, `GET {id}/evidencia-conciliacion` |
| `CajaComprobantesController` | `GET /`, `GET {id}` |
| `InventarioController` | `GET/POST ingredientes`, `PUT ingredientes/{id}`, `GET/POST movimientos`, `GET alertas`, `GET productos`, `GET/PUT producto/{id}` |
| `ProductosController` | `GET /` (catálogo) |
| `ReservacionesController` | `GET disponibilidad`, `GET /`, `POST /`, `POST {id}/cancelar` |
| `ReporteVentasController` | `GET /` |
| `AnulacionesSeguridadController` | `POST autorizar-anulacion`, `POST /`, `GET {id}`, `POST {id}/cancelacion`, `GET anulaciones`, `GET eventos` |

### 7.3 Integración en el cliente (Avalonia)

Desde `App.axaml.cs` (ensamblaje verificado) y `Services/`:

1. **`DesktopConfigurationService`** lee `%ProgramData%\ElCarbonazo\desktop.json` (`ApiBaseUrl` + `idInstalacion` UUID; plantilla en `deploy/desktop.example.json`) y **valida el UUID incluso con override**; falla → `ConfigurationErrorView`. `ELCARBONAZO_API_BASE_URL` solo se acepta con `ELCARBONAZO_ENVIRONMENT=Development`; HTTPS verificado en producción, TLS jamás omitido.
2. **`ApiClientService`**: transporte HTTP único — JSON, `X-Installation-Id`, retranqueo de token con `SessionManager`, normalización de errores (`ApiExceptions`).
3. **`SessionManager`** (890 líneas): posee access/refresh token, ciclo de sesión (inicio, actividad ≥1/min, inactividad 15 min, tope 8 h, revocación), refresh rotatorio y reconciliación ante errores de seguridad; `ServerClock` evita decisiones basadas en reloj local.
4. **Servicios por dominio**: `MesasApiService`, `GruposMesaApiService`, `PedidosSalon` (`EnvioPedidoApiService`/`CuentaPedidoApiService`/`ProductosPedidoApiService`), `CocinaApiService`, `CajaCuentasReader`, `InventarioLecturaService`/`InventarioEscrituraCoordinator`, `ReservacionesApiService`, `ReporteVentasApiService`.
5. **Coordinación de lecturas y escrituras** (núcleo de la integración segura):
   - `MesasRequestCoordinator` + `MesasPollingPolicy`: serializa polling y mutaciones; **descarta lecturas obsoletas**.
   - `CajaRecoveryCoordinator` + `CajaRecoveryStore`/`CajaRecoveryJournal` + provisioners Windows (`WindowsCajaJournalProvisioner`, `WindowsCajaRecoverySecurityPolicy`): diario protegido en Windows; recuperación al iniciar.
   - `InventarioEscrituraCoordinator`: bloqueo de escrituras administrativas inciertas durante la sesión, incluso al reabrir la vista.
   - **Regla transversal**: ante timeout/red/5xx en una escritura **no hay reintento automático**; se informa la incertidumbre, se reconcilia y se exige revisión humana. **Un GET no demuestra que la escritura anterior terminó.**

### 7.4 Integración en el servidor

- `Program.cs` (punto único de arranque): configuración de EF/JWT/rate limiting/middleware, modos administrativos CLI (uno a la vez, validado), siembra de Development y `UseHttpsRedirection` fuera de Development.
- Flujo de petición: `UseExceptionHandler` → routing → validación `X-Installation-Id` → `UseStatusCodePages` → `UseRateLimiter` → `UseAuthentication` (validación JWT + sesión + rol en `OnTokenValidated`) → `UseAuthorization` → controller → service → `AppDbContext`.
- Logs: JSON en consola + **archivo JSON diario** (`DailyJsonFileLoggerProvider` hacia `%ProgramData%\ElCarbonazo\api-logs` en producción o content root en Development), nivel mínimo Information, Microsoft/Npgsql/EF en Warning.
- Servicios hospedados: `PasswordHasherWarmupService` (calienta BCrypt) y `RefreshTokenCleanupService` (retención 30 días).

### 7.5 Integración end-to-end verificada (AcceptanceHost)

`tests/ElCarbonazo.AcceptanceHost` (fuera de la solución; referencia a Api y Desktop) crea una base PostgreSQL temporal, aplica migraciones solo ahí, carga el catálogo ficticio y ejecuta **los servicios REST reales del Desktop** contra la API:

| Smoke | Recorrido |
|---|---|
| `--caja-smoke` | Pedidos + caja integrados |
| `--pedidos-smoke` | Salón + cocina: cuenta individual y de grupo, envío/repetición idempotente, importes, transición hasta `Lista`, lectura de cuenta |
| `--inventario-smoke` | Inventario con recetas y escandallo |
| `--catalogo-defensa-smoke` | Catálogo de defensa |

**Límite declarado:** usa sesión adaptada; no abre ventana, no valida render ni LAN, y una interrupción forzada puede dejar recursos temporales a identificar antes de limpiar. `--pedidos` (sin `-smoke`) mantiene un entorno ficticio abierto para revisión visual manual.

### 7.6 Recorrido de operación integrado (validado)

Con la base preparada: iniciar sesión → mapa de mesas → abrir/agrupar mesa → armar y **enviar** pedido (consume escandallo atómicamente si hay receta) → KDS avanza comanda a `Lista` → caja: abrir turno → operación de cobro (diario primero, ejecución única) → comprobante interno → cierre de turno → reporte de ventas. Evidencia: `docs/validacion-d4/d5/d6.md`, `validacion-004.md`, `validacion-005.md` y `README.md` (recorrido universitario).

En producción de demostración, la API escucha en el perfil `https` (`https://localhost:7191/`); el perfil `http` (`http://localhost:5261`) es solo para entorno de desarrollo (`AGENTS.md`).

---

## 8. Implementación de validaciones y mecanismos básicos de seguridad, según el alcance del proyecto

> Alcance declarado (constitución/README): **autenticación y autorización de acceso, integridad de datos de negocio, auditabilidad y protección de la instalación local**. NO incluye: verificación bancaria, validez fiscal, cifrado en tránsito con certificado CA pública, operación LAN/producción.

### 8.1 Autenticación

| Mecanismo | Detalle verificado | Fuente |
|---|---|---|
| Hash de contraseña | BCrypt (BCrypt.Net-Next 4.0.3), work factor **12** validado en 10–15 en arranque | `Program.cs`, `BCryptPasswordHasher` |
| Access token | JWT **5 min** — la opción `Jwt:ExpirationMinutes` se **valida que sea exactamente 5** en arranque; issuer `ElCarbonazo.Api`, audience `ElCarbonazo.Desktop`, reloj skew 30 s, rol en claim `rol` | `Program.cs`, `appsettings.json`, `JwtTokenGenerator` |
| Clave JWT | Base64 ≥ 32 bytes validada en arranque; en Development existe clave de desarrollo por defecto si no se define; fuera de Development la falta de clave **falla el arranque** | `Program.cs` (validación de opciones) |
| Refresh token | **Rotatorio** (`TokenRefreshSesion`, `RefreshTokenCodec`), historial con retención de 30 días y limpieza por servicio hospedado | `RefreshTokenCleanupService`, `SessionSecurityPolicy` |
| Revocación | Cada petición valida en `OnTokenValidated`: sesión no revocada, usuario activo con rol vigente, inactividad < 15 min y tope absoluto < 8 h; el logout revoca | `Program.cs`, `AuthService` |
| Bootstrap admin | Producción: `--bootstrap-admin` interactiva con `BootstrapPasswordPolicy`; Development: siembra `admin/admin123` solo bajo Development | `AdminBootstrapService`, `README.md` |

### 8.2 Autorización

- Roles: `Jefa`, `Encargado de Caja`, `Mesero`, `Personal de Cocina`, `Supervisor` (`UserRoles`).
- Autorización por **rol en cada endpoint** (atributos `[Authorize]`/roles en los 14 controllers; ejemplos: cobro y turnos solo rol de caja/Jefa; estado de comanda solo cocina/Supervisor/Jefa; inventario según rol).
- **La API es autoridad final**: la navegación de Desktop solo es una conveniencia; una petición fuera de rol termina en `403 AUTH_PROHIBIDO`.
- Consulta de cocina **sin mutar** desde roles de solo consulta (regla de `docs/domain.md`).

### 8.3 Protecciones de cuenta y acceso

- **Bloqueo por intentos de login**: 5 fallos → bloqueo de 5 minutos (`LoginSecurityPolicy`).
- **Rate limiting** de login: 10 peticiones/minuto por IP (ventana fija, política `"login"`); rechazo con 429 + `Retry-After`; el **primer 429 de la ventana por IP se audita** en evento append-only `LoginRateLimitActivado` (`LoginRateLimitAuditGate`).
- **Identidad de instalación**: header `X-Installation-Id` (UUID) validado en middleware; `desktop.json` exige `idInstalacion` UUID válido **incluso con override de Desarrollo** (doble requisito).

### 8.4 Validación de entrada y datos

- **En servidor**: Modelo binding con validación → 400 problem+json `VALIDATION_ERROR` con errores por campo (camelCase) y `traceId`; 404/405 normalizados. `Directory.Build.props` trata advertencias como errores y `AnalysisLevel latest` activa análisis estático en toda la solución.
- **En cliente**: validación y estados en ViewModel (botones deshabilitados con causa, diagnósticos sin exposición de excepciones, credenciales o stack internals — corregido durante D4, ver `docs/validacion-d4.md` y ERR correspondientes en bitácora).
- **Invariantes de negocio a nivel de base**: migración `EnforceDomainInvariants` (restricciones de esquema), máquinas de estado en `Domain/` (`MesaStateTransiciones`, `ComandaStates`...); la concurrencia por `xmin`/versión detecta escrituras concurrentes sin reintentos ciegos.
- **Validaciones de configuración en arranque**: opciones JWT y hash validadas con `ValidateOnStart`; modos administrativos CLI mutuamente excluyentes; `--seed-catalogo-defensa` exige Development + confirmación exacta de base + directorio + exactamente una Jefa activa.

### 8.5 Integridad y auditabilidad

- **Auditoría append-only** (`EventoAuditoria`, migración `EnforceAuditAppendOnly`): login, activación de rate limit, operaciones de caja, anulaciones, inventario; consultable por eventos/module.
- **Diarios locales sin payload ni secretos** (caja e inventario): nunca se crea un diario vacío para "reparar" incertidumbre; el resultado terminal se conserva hasta conciliar.
- **Idempotencia y una sola ejecución**: envíos de pedido idempotentes; `SendCajaOnceAsync` (preparación recién observada + ejecución única); UUID/propietario/tipo registrados antes de enviar.
- **Regla de incertidumbre** (transeversal a caja, cocina e inventario): sin reintento automático ante timeout/red/5xx; un GET no demuestra que una escritura terminó; se bloquea, se reconcilia y se exige revisión humana. Recuperación durable **no garantizada** entre terminales ni tras reinicio (pendiente declarado, no defecto oculto).

### 8.6 Seguridad del canal y de la instalación

- **HTTPS**: `UseHttpsRedirection` fuera de Development; el Desktop **nunca** omite validación TLS; en Development se permite HTTP plano y el override de base URL **solo** con `ELCARBONAZO_ENVIRONMENT=Development`.
- **Diario de caja protegido en Windows** (`WindowsCajaJournalProvisioner` + política de seguridad `WindowsCajaRecoverySecurityPolicy`, `scripts/instalar-diario-caja.ps1`).
- **HMAC para anulaciones** (módulo pausado, sección 5.6): clave puesta vía `AutorizacionSuperior:ClaveHmac`, provision/recovery por CLI, guard + lock para una sola autorización activa.
- **Secretos**: no se versionan (instrucción de `AGENTS.md`/README: "No publicar contraseñas, tokens ni cadenas de conexión"); logs sin credenciales; `api-logs` bajo `%ProgramData%` en producción.

### 8.7 Límites declarados de este alcance

- Caja **registra** cobros (efectivo/tarjeta/transferencia) pero **no procesa pagos ni verifica bancos**; coincidencia de referencia ⇒ rechazo durable y nueva confirmación explícita.
- Comprobante de caja **interno, no fiscal**.
- Sin cifrado de almacenamiento en reposo de la base (fuera del alcance universitario).
- LAN física, TLS con CA pública, instalador y requisitos de producción quedan en objetivos futuros (`docs/evolucion-futura-por-modulos.md`).

---

## Anexo A. Estado real de módulos (por carpeta)

| Módulo (carpeta) | Estado efectivo | Evidencia / notas |
|---|---|---|
| `specs/001-auth-mesas-mvp` | **Aceptado** (perfil universitario) | Quedan tasks de perfil producción (T3.10/T7/T8/T9) |
| `specs/002-operaciones-seguras` | **Pospuesto, no implementado** | Solo spec/plan |
| `specs/003-pedidos-kds` | **Aceptado** | `validacion-d4/d5/d6.md` |
| `specs/004-caja-cobros` | **Aceptado**; C6 pendiente | `validacion-004.md` |
| `specs/005-inventario-escandallo` | **Aceptado**; adenda de catálogo de defensa cargada | `validacion-005.md` |
| `specs/006-auditoria-cancelaciones-seguridad` | **PAUSADO** con avance parcial conservado; **no aceptado** | Migración 13 aplicada; código HMAC/anulación/alertas KDS local |
| `specs/006-reservaciones-mesas` | **Aprobado e implementado**; falta revisión visual | Migración 14 aplicada 17/11/2026 |
| `specs/007-reporte-ventas` | **Aprobado e implementado**; falta validación visual | Spec/plan, controller y vista presentes |
| `specs/entrega-defensa-local` | **Fase 1 en curso** (catálogo cargado) | Fase 2 (publicación) pendiente |

## Anexo B. Validación: automatizada, manual y no validada

**Automatizada (hecho):** ~163–165 pruebas API (21 clases) y ~267–268 Desktop (28 archivos) en verde en la regresión más reciente reportada; formato y build limpios (0 advertencias); auditoría NuGet sin hallazgos; 4 smokes de AcceptanceHost (ver §7.5).

**Manual declarada por el usuario (hecho):** D4/D5/D6 (módulo 003) en ejecución local real; validaciones 004 y 006 en sus documentos; T5/T6 corresponden al **perfil universitario**, no a producción.

**No validado (debe decirse explícitamente en cualquier documento):**
- Revisión **visual** final de reservaciones y reporte de ventas.
- Botón Cocina con su nuevo estilo (cambio posterior a la revisión de D5).
- Prueba física LAN, métricas cronometradas de render, cobertura ≥80 %, aptitud para producción.

## Anexo C. Pendientes documentados (a 17/11/2026)

- [ ] Revisión visual de reservaciones (`specs/006-reservaciones-mesas`).
- [ ] Validación visual de reporte de ventas (`specs/007-reporte-ventas`).
- [ ] Confirmar en pantalla el botón Cocina con estilo actualizado (residual de D5/D6).
- [ ] C6 de `specs/004-caja-cobros`.
- [ ] Tareas de perfil de producción en `specs/001-auth-mesas-mvp` (T3.10/T7/T8/T9).
- [ ] Fase 2 (publicación) de `specs/entrega-defensa-local`.
- [ ] Decisión sobre `specs/006-auditoria-cancelaciones-seguridad` (pausado por decisión de la usuaria; avance conservado).
- [ ] `specs/002-operaciones-seguras` (pospuesto).

**Objetivos futuros (inventario, no especificación)** — `docs/evolucion-futura-por-modulos.md`: LAN física, render cronometrado, cobertura ≥80 %, instalador, requisitos de producción.

## Anexo D. Trampas y convenciones documentales

1. **Numeración duplicada**: dos carpetas `006-*` y "Tareas 007" dentro de `specs/entrega-defensa-local`; nombrar siempre por carpeta completa.
2. **AGENTS.md vs. estado efectivo**: declara 006-auditoria como "aprobado, aún no implementado"; los specs indican **PAUSADO con avance parcial**. Presentar siempre segundo.
3. `docs/evolucion-futura-por-modulos.md` es **inventario**, nunca citación como especificación aprobada.
4. Estilo en español, formato bitácora: hecho / causa / corrección / evolución / evidencia.
5. Nada se documenta como implementado/aceptado sin cruzar `tasks.md` (estado) → `docs/validacion-*.md` (evidencia) → código.
6. Lo no medido se declara no medido (render, latencia, cobertura, LAN).
7. La base local `elcarbonazo` contiene datos de la Fase 1: todo documento que mencione bases debe distinguir desarrollo (`elcarbonazo`), prueba temporal (`elcarbonazo_test_<uuid>`) y demo (`elcarbonazo_demo_local`), y exigir aprobación de destino antes de migrar.