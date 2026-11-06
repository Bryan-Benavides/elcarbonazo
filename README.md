# El Carbonazo — POS Restaurant (MVP)

## Stack
- .NET 10
- Backend: ASP.NET Core Web API + EF Core + Npgsql (PostgreSQL)
- Frontend: Avalonia UI 12.1.4 + SukiUI 7.0.1 + CommunityToolkit.Mvvm 8.4.0

## Ejecutar una demostración local

Requiere .NET 10 SDK y PostgreSQL 15 o superior. El prototipo es para presentación universitaria; no está listo para operar el restaurante. Preparar una base de desarrollo aislada y respaldar cualquier base existente antes de aplicar migraciones. No publicar contraseñas, tokens ni cadenas de conexión.

1. Configurar la conexión PostgreSQL de **Development** fuera de Git, por ejemplo mediante `ConnectionStrings__DefaultConnection` en el entorno de la API. Confirmar que la base elegida es la de demostración, no una base con datos que se desee conservar.
2. Desde la raíz, ejecutar `dotnet tool restore` y `dotnet build ElCarbonazo.sln`. Después, desde `src/ElCarbonazo.Api`, ejecutar `dotnet ef database update` contra la base confirmada. La API no aplica migraciones automáticamente.
3. Desktop necesita `%ProgramData%\ElCarbonazo\desktop.json` con `ApiBaseUrl` e `idInstalacion` UUID válido. En una instalación nueva de demostración, crear ese archivo a partir de `deploy/desktop.example.json`, generar un UUID y poner como URL `https://localhost:7191/`. **No sobrescribir una configuración existente**: revisarla primero. El UUID sigue siendo necesario aun usando un override de Development.
4. En una terminal desde la raíz, iniciar `dotnet run --project src/ElCarbonazo.Api --launch-profile https`. En otra, ejecutar `dotnet run --project src/ElCarbonazo.Desktop`. El perfil `http` usa `http://localhost:5261`; si Desktop apunta a `https://localhost:7191`, usar el perfil `https`. El certificado de desarrollo debe ser confiable; nunca desactivar la validación TLS. Consultar `dotnet dev-certs https --help` antes de modificar el almacén de certificados.
5. En Development, el seeder local crea o reutiliza la cuenta de demostración documentada en `docs/installation.md` solo bajo sus condiciones de seguridad. No usarla en producción ni alterar usuarios existentes manualmente.

### Recorrido sugerido

Iniciar sesión, consultar las 15 mesas, seleccionar una y probar reserva/ocupación/liberación con confirmación. Agrupar dos mesas libres, consultar su detalle y cerrar el grupo. Probar desconexión y recuperación y cerrar sesión. Usar solo datos de demostración; no inventar pedidos o importes. Para Cocina y cuentas pendientes existe un anfitrión temporal aislado descrito en `docs/validacion-t5.md`.

### Si no conecta

- Confirmar que PostgreSQL y la API iniciaron correctamente, y que la URL del Desktop coincide con la dirección escuchada por la API.
- Un error de configuración exige revisar la ruta y el UUID; un error TLS requiere corregir confianza o hostname, no utilizar bypass.
- Si una escritura da resultado incierto, no repetirla automáticamente: revisar la advertencia. Un GET no demuestra que la escritura anterior haya terminado.

Para pruebas y configuración de instalación ver `AGENTS.md`, `docs/installation.md` y `docs/validacion-t5.md`.

## Arquitectura
- `src/ElCarbonazo.Api` — API REST + EF Core (solo BD)
- `src/ElCarbonazo.Desktop` — Cliente Avalonia (solo HTTP a API)
- `docs/` — Dominio, Constitución
- `specs/` — Especificaciones por módulo (SDD)

El módulo [`003-pedidos-kds`](specs/003-pedidos-kds/spec.md) está en preparación documental: pedidos, catálogo y cocina todavía no están implementados. Su plan y tareas requieren revisión y aprobación de las decisiones pendientes antes de programar. La propuesta `002-operaciones-seguras` permanece separada y pospuesta.
