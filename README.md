# El Carbonazo — Sistema de Punto de Venta (POS)

Proyecto académico de punto de venta para restaurante. Incluye gestión de mesas, pedidos, cocina, caja, inventario, reportes y control de anulaciones con auditoría.

## Stack
- .NET 10
- Backend: ASP.NET Core Web API + EF Core + Npgsql (PostgreSQL)
- Frontend: Avalonia UI 12 + SukiUI 7 + CommunityToolkit.Mvvm

## Requisitos previos
- .NET 10 SDK instalado
- PostgreSQL 15 o superior instalado y en ejecución
- Una base de datos PostgreSQL creada (por ejemplo, llamada `elcarbonazo`)

## Configuración inicial

### 1. Base de datos
Configure primero la conexión del paso 2 en la misma terminal y confirme el destino. La base debe existir; respáldela si contiene datos que desee conservar. La migración no carga menú ni inventario. Restaure la herramienta local y aplique las migraciones desde `src/ElCarbonazo.Api`:

```powershell
dotnet tool restore
cd src/ElCarbonazo.Api
dotnet ef database update
cd ../..
```

> La API no aplica migraciones automáticamente. Este paso es obligatorio antes de iniciar el sistema.

### 2. Variables de entorno
Cree un archivo `entorno.ps1` en la raíz del proyecto (no subirlo a ningún repositorio) con los valores de su instalación. Ejemplo:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=elcarbonazo;Username=postgres;Password=SU_CONTRASEÑA'
$env:Jwt__Key = 'REEMPLAZAR_POR_CLAVE_BASE64_VALIDA'
$env:AutorizacionSuperior__ClaveHmac = 'REEMPLAZAR_POR_CLAVE_BASE64_INDEPENDIENTE'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
```

Ejecutelo antes de iniciar la API:

```powershell
. .\entorno.ps1
```

Los marcadores no son claves válidas. Para anulaciones, la clave HMAC debe ser aleatoria, protegida y estable, y requiere primera provisión explícita; consulte `docs/installation.md`.

### 3. Iniciar la API
Desde una terminal:

```powershell
dotnet run --project src/ElCarbonazo.Api --launch-profile https
```

La API escucha en `https://localhost:7191/`.

### 4. Iniciar el escritorio
Desde otra terminal:

En una instalación nueva configure primero `%ProgramData%\ElCarbonazo\desktop.json` y provisione los diarios de caja y anulaciones según `docs/installation.md`. No reemplace diarios existentes.

```powershell
dotnet run --project src/ElCarbonazo.Desktop
```

## Credenciales de prueba (entorno Development)
- En una base nueva, si se cumplen las condiciones del seeder Development: usuario `admin`, contraseña `admin123`, rol Jefa. Una base existente puede tener otra contraseña.

> Estas credenciales son solo para el entorno de desarrollo y pruebas académicas. No usarlas en un entorno real.

## Estructura del proyecto
- `src/ElCarbonazo.Api` — API REST y base de datos
- `src/ElCarbonazo.Desktop` — Interfaz de escritorio
- `tests/` — Pruebas automatizadas
- `docs/` — Documentación técnica
- `specs/` — Especificaciones funcionales por módulo

## Alcance
Este sistema cubre: mesas, pedidos, cocina, caja, inventario, recetas, reportes, reservas y anulaciones con registro de auditoría. Es un proyecto académico para presentación universitaria; no está diseñado para operación comercial ni para uso con dinero real.

## Soporte
Cualquier problema de instalación, consulte `docs/installation.md` y `AGENTS.md`.

**Entrega pendiente:** el ZIP generado anteriormente contiene configuración local y registros, y no incluye una base restaurable verificada. No lo comparta; hay que preparar y revisar un paquete nuevo.
