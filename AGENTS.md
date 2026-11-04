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
Approved direction: dark **Carbón & Fuego**; see `docs/diseno-visual-propuesta.md`.
- Background `#1E1E1E`, panels `#2D2D30`, borders `#3F3F46`.
- Primary text `#F3F4F6`, secondary text `#9CA3AF`, accent `#FF5722`, alternate `#E65100`.
- Table states: Libre `#10B981`, Ocupada `#EF4444`, Reservada `#F59E0B`, Agrupada `#06B6D4`. Pair colors with text and verify contrast.

UI specs (Module 001):
- MesasView: responsive dark rectangular cards in a WrapPanel, inspired by the approved reference; no decorative chairs or circular table buttons.
- Sidebar exposes implemented destinations only; do not invent orders, amounts, staff or other data not supplied by the API.
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
**University MVP: auth (JWT login) + 15-table interactive grid with nominal 2s single-flight polling.**

Endpoints:
- `POST /api/auth/login` — Body: `{ "nombre", "contrasena" }` → access/refresh tokens, expirations and identity.
- `GET /api/mesas` — Bearer → `[{ "idMesa", "idGrupo", "numero", "capacidad", "estado", "version" }]`.
- `PUT /api/mesas/{id}/estado` — Bearer, body `{ "estado", "version" }` → complete mesa DTO.
- `POST /api/grupos-mesa`, `DELETE /api/grupos-mesa/{id}` — see `plan.md` for contracts and roles.

Desktop components:
- `Views/LoginView.axaml` ↔ `ViewModels/LoginViewModel.cs`
- `Views/MesasView.axaml` ↔ `ViewModels/MesasViewModel.cs`
- `Services/ApiClientService.cs` — HTTP/TLS transport; `SessionManager` owns in-memory tokens and session lifecycle
- `Services/MesasApiService.cs` — mesa endpoints only
- `Converters/EstadoMesaColorConverter.cs` — estado → color mapping

## Database Schema (PostgreSQL 3NF)
Key tables: `usuario`, `mesa`, `producto`, `ingrediente`, `receta`, `detalle_receta`, `pedido`, `detalle_pedido`, `factura`

Module 001 tables (per `docs/domain.md`):
- `usuario`: `id_usuario` (PK, SERIAL), `nombre`, `contrasena_hash`, `rol`, `email`, `activo`
- `mesa`: `id_mesa` (PK, SERIAL), `id_grupo` (FK, NULL), `numero` (unique), `capacidad`, `estado` ('Libre'|'Ocupada'|'Reservada'|'Agrupada')

**Note**: Column names differ from earlier docs — use `domain.md` as source of truth (`numero` not `numero_mesa`; `usuario` has `email`).

Pedido states: 'Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado'

Seed data: 15 mesas; production admin is created explicitly via bootstrap. Development has a separate local-only seeder.

## References
- `docs/domain.md` — Domain dictionary & full DB schema (authoritative)
- `docs/constitution.md` — Non-negotiable principles, stack, architecture rules
- `specs/001-auth-mesas-mvp/` — Current module spec, plan, tasks
