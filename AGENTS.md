# AGENTS.md — El Carbonazo

## Project Overview
.NET 10 solution for a bar/restaurant POS system (Managua, Nicaragua). 15 tables, ~90 products, 8 workers. LAN-only operation.

## Architecture (Strict)
- **Backend**: `src/ElCarbonazo.Api` — ASP.NET Core Web API + EF Core + Npgsql (PostgreSQL)
- **Frontend**: `src/ElCarbonazo.Desktop` — Avalonia UI 12.1.4 + SukiUI 7.0.1 + CommunityToolkit.Mvvm 8.4.0
- **Client Isolation**: Desktop **must not** reference `Microsoft.EntityFrameworkCore`, `Npgsql`, or connect to DB directly. All access via HTTP/REST to API.
- **MVVM**: Every `Views/*.axaml` has a `ViewModels/*ViewModel.cs`. Code-behind `.axaml.cs` only contains `InitializeComponent()`.

## Key Constraints
- **SDD**: No code without approved spec in `specs/`.
- **Dependency Stability**: No untested/incompatible NuGet updates. Exact versions pinned in `tasks.md` (see below).
- **Offline First**: All logic runs on LAN, no internet dependency.
- **Dual Currency**: NIO/USD at fixed rate. IVA 15%, tip 10% voluntary.
- **15 Tables Fixed**: Cannot release a table with pending bill.
- **Roles**: Jefa, Encargado de Caja, Mesero, Personal de Cocina, Supervisor.
- **Auth**: JWT with role claims. Passwords: Hash with Salt. Session timeout: 15 min inactivity.

## Pinned NuGet Versions (from tasks.md)
| Package | Version |
|---------|---------|
| `Microsoft.EntityFrameworkCore` | 10.0.0 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.0 |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.0 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.0 |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.0 |
| `Avalonia` | 12.1.4 |
| `Avalonia.Desktop` | 12.1.4 |
| `Avalonia.Fonts.Inter` | 12.1.4 |
| `SukiUI` | 7.0.1 |
| `CommunityToolkit.Mvvm` | 8.4.0 |

## Design System (SukiUI)
Colors defined in `App.axaml` resources:
- `ColorVino` `#8C2331` (primary, brand, headers)
- `ColorCarbón` `#2A2421` (input bg, dense text, borders)
- `ColorHueso` `#F8F5F0` (app background)
- `ColorBeige` `#E8E0D5` (secondary panels)
- Table states: `ColorMesaLibre` `#7CB342`, `ColorMesaOcupada` `#8C2331`, `ColorMesaReservada` `#E67E22`, `ColorMesaAgrupada` `#2E86AB`

UI specs (Module 001):
- Login: SukiUI styling with ColorVino/ColorBeige
- MesasView: WrapPanel centered, 15 cards 160×160px, circular buttons 100×100px, 4 decorative chairs in corners
- `EstadoMesaColorConverter.cs` maps estado → color

## Developer Commands
```bash
# Build solution (sln at repo root)
dotnet build ElCarbonazo.sln

# Run API (from src/ElCarbonazo.Api)
dotnet run

# Run Desktop (from src/ElCarbonazo.Desktop)
dotnet run

# EF Core migrations (from src/ElCarbonazo.Api)
dotnet ef migrations add <name>
dotnet ef database update
```

## Spec-Driven Workflow
1. Read spec in `specs/<nnn>-<name>/spec.md`
2. Check `plan.md` for technical design (API contracts, DB schema, components)
3. Execute tasks from `tasks.md` in order (T1→T5)
4. Update task checkboxes as completed

## Current Module: 001-auth-mesas-mvp
**Auth (JWT login) + 15-table interactive grid with ≤2s polling.**

Endpoints:
- `POST /api/auth/login` — Body: `{ "nombre", "contrasena" }` → 200: `{ "token", "idUsuario", "nombre", "rol" }`
- `GET /api/mesas` — Auth: Bearer → 200: `[{ "idMesa", "numeroMesa", "capacidad", "estado" }]`
- `PUT /api/mesas/{id}/estado` — Auth: Bearer, Body: `{ "estado" }` → 200: `{ "idMesa", "estado" }`

Desktop components:
- `Views/LoginView.axaml` ↔ `ViewModels/LoginViewModel.cs`
- `Views/MesasView.axaml` ↔ `ViewModels/MesasViewModel.cs`
- `Services/ApiClientService.cs` — HTTPClient + token management
- `Services/MesasApiService.cs` — mesa endpoints only
- `Converters/EstadoMesaColorConverter.cs` — estado → color mapping

## Database Schema (PostgreSQL 3NF)
Key tables: `usuario`, `mesa`, `producto`, `ingrediente`, `receta`, `detalle_receta`, `pedido`, `detalle_pedido`, `factura`

Module 001 tables (per `docs/domain.md`):
- `usuario`: `id_usuario` (PK, SERIAL), `nombre`, `contrasena_hash`, `rol`, `email`, `activo`
- `mesa`: `id_mesa` (PK, SERIAL), `id_grupo` (FK, NULL), `numero` (unique), `capacidad`, `estado` ('Libre'|'Ocupada'|'Reservada')

**Note**: Column names differ from earlier docs — use `domain.md` as source of truth (`numero` not `numero_mesa`; `usuario` has `email`).

Pedido states: 'Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado'

Seed data: 15 mesas + 1 admin user (`admin` / default password hash)

## References
- `docs/domain.md` — Domain dictionary & full DB schema (authoritative)
- `docs/constitution.md` — Non-negotiable principles, stack, architecture rules
- `specs/001-auth-mesas-mvp/` — Current module spec, plan, tasks