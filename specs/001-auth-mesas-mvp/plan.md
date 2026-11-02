# Plan de Diseño Técnico — Módulo 001 (Alineado con docs/domain.md)

## 1. Esquema de Base de Datos (PostgreSQL - 3FN)
> Fuente de verdad: `docs/domain.md`. Columnas y tipos exactos.

### Tabla: `usuario`
- `id_usuario` INT (PK, SERIAL)
- `nombre` VARCHAR(100) (NOT NULL)
- `contrasena_hash` VARCHAR(255) (NOT NULL)
- `rol` VARCHAR(50) (NOT NULL) -- Valores: 'Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor'
- `email` VARCHAR(255) (NOT NULL)
- `activo` BOOLEAN (NOT NULL, DEFAULT true)

### Tabla: `mesa`
- `id_mesa` INT (PK, SERIAL)
- `id_grupo` INT (FK, NULL) -- Para mesas agrupadas (estado 'Agrupada')
- `numero` INT (UNIQUE, NOT NULL) -- 1..15
- `capacidad` INT (NOT NULL)
- `estado` VARCHAR(20) (NOT NULL) -- Valores estrictos: 'Libre', 'Ocupada', 'Reservada', 'Agrupada'

## 2. Contrato de API (ASP.NET Core Web API)

### Convención de serialización JSON
**Todas las respuestas y requests usan camelCase** (configurar `JsonNamingPolicy.CamelCase` en `Program.cs`).
EF Core mapea a snake_case en BD; la serialización expone camelCase.

---

- `POST /api/auth/login`
  - Body: `{ "nombre": "string", "contrasena": "string" }`
  - Response 200: `{ "token": "string", "idUsuario": int, "nombre": "string", "rol": "string" }`

- `GET /api/mesas`
  - Header: `Authorization: Bearer <token>`
  - Response 200: `[ { "idMesa": int, "idGrupo": int?, "numero": int, "capacidad": int, "estado": "string" } ]`
  - `estado` ∈ { "Libre", "Ocupada", "Reservada", "Agrupada" }

- `PUT /api/mesas/{id}/estado`
  - Header: `Authorization: Bearer <token>`
  - Body: `{ "estado": "string" }` -- Valores: "Libre" | "Ocupada" | "Reservada" | "Agrupada"
  - Response 200: `{ "idMesa": int, "estado": "string" }`

## 3. Reglas de Dominio (Módulo 001)
- **Mesa Agrupada**: Solo se permite `estado = 'Agrupada'` si `id_grupo` no es NULL. Al desagrupar → `estado = 'Liber'` y `id_grupo = NULL`.
- **No liberar mesa con cuenta pendiente**: Validar en `PUT /api/mesas/{id}/estado` que no exista `pedido` con `estado ∈ {'Abierto', 'En Preparación', 'Listo'}` para esa mesa.
- **Roles autorizados para MesasView**: `Jefa`, `Encargado de Caja`, `Mesero`, `Personal de Cocina`, `Supervisor` (nombres exactos como en BD).

## 4. Componentes Desktop (Avalonia UI + SukiUI)
- `Views/LoginView.axaml` ↔ `ViewModels/LoginViewModel.cs`
- `Views/MesasView.axaml` ↔ `ViewModels/MesasViewModel.cs`
- `Services/ApiClientService.cs` (Gestión centralizada de HTTPClient y Bearer Token)
- `Services/MesasApiService.cs` (Consumo exclusivo de endpoints de mesa)
- `Converters/EstadoMesaColorConverter.cs` — Mapea estado → color del Design System:
  - `Libre` → `ColorMesaLibre` `#7CB342`
  - `Ocupada` → `ColorMesaOcupada` `#8C2331`
  - `Reservada` → `ColorMesaReservada` `#E67E22`
  - `Agrupada` → `ColorMesaAgrupada` `#2E86AB`

## 5. Configuración Técnica Requerida
- **Program.cs (API)**: `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);`
- **Polling MesasView**: `DispatcherTimer` (intervalo 2000ms) o `System.Reactive` `Observable.Interval` llamando a `MesasApiService.GetAllAsync()`.
- **Seed data**: 15 mesas (`numero` 1..15, `capacidad` 4, `estado` 'Libre', `id_grupo` NULL) + 1 usuario `admin` (rol 'Jefa', `email` 'admin@elcarbonazo.local', `activo` true, hash bcrypt de contraseña por defecto).