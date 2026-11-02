# Lista de Tareas — Módulo 001: Autenticación y Control de Mesas (MVP)

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
  - Crear `AppDbContext` con la configuración de Fluent API y datos semilla (`HasData`): 15 mesas físicas y 1 usuario administrador inicial (`admin` / hash provisional de contraseña)[cite: 1, 2, 7].
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

- [ ] **T4: Capa Desktop — Formulario de Login (SukiUI)**
  - Crear `Services/ApiClientService.cs` en `ElCarbonazo.Desktop` para administrar peticiones HTTP y token JWT en memoria[cite: 3].
  - Crear `Views/LoginView.axaml` y `ViewModels/LoginViewModel.cs` aplicando los recursos del `Design System` (`ColorVino`, `ColorBeige`, etc.)[cite: 3].
  - *Criterio de Aceptación:* Al autenticar credenciales válidas contra la API, la aplicación guarda el token JWT y navega hacia `MesasView`[cite: 3].

- [ ] **T5: Capa Desktop — Mapa de Mesas (15 Mesas)**
  - Crear `Views/MesasView.axaml` y `ViewModels/MesasViewModel.cs`
  - Renderizar `WrapPanel` centrado: 15 tarjetas 160x160px, botones circulares 100x100px, 4 sillas decorativas en esquinas
  - Crear `Converters/EstadoMesaColorConverter.cs` para **4 estados**: `Libre` `#7CB342`, `Ocupada` `#8C2331`, `Reservada` `#E67E22`, `Agrupada` `#2E86AB`
  - Configurar polling ≤2s: `DispatcherTimer` (Interval = 2000ms) llamando a `MesasApiService.GetAllAsync()` y actualizando `ObservableCollection<MesaDto>`
  - *Criterio de Aceptación:* 15 mesas muestran número, capacidad y color de estado en tiempo real; cambios en BD se reflejan ≤2s.
