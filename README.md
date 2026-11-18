# El Carbonazo — POS Restaurant (MVP)

## Stack
- .NET 10
- Backend: ASP.NET Core Web API + EF Core + Npgsql (PostgreSQL)
- Frontend: Avalonia UI 12.1.4 + SukiUI 7.0.1 + CommunityToolkit.Mvvm 8.4.0

## Iniciar la demostración preparada

Si ya se preparó la base local `elcarbonazo_demo_local` y el diario de caja en este equipo, ejecutar desde la raíz en PowerShell:

```powershell
.\scripts\iniciar-api-demo.ps1
```

Dejar esa terminal abierta y, en otra, ejecutar `dotnet run --project src/ElCarbonazo.Desktop`. La API escucha en `https://localhost:7191/`. El iniciador no crea bases ni diarios y no cambia la configuración instalada. Si informa de una operación pendiente, no borrar el diario: confirmar primero que pertenece a esta base; para abrir la API y recuperarla usar `-RecuperarDiarioDeEstaDemo`. Esa opción no ejecuta ni concilia la operación automáticamente.

## Preparación inicial general

Requiere .NET 10 SDK y PostgreSQL 15 o superior. El prototipo es para presentación universitaria; no está listo para operar el restaurante. Preparar una base de desarrollo aislada y respaldar cualquier base existente antes de aplicar migraciones. No publicar contraseñas, tokens ni cadenas de conexión.

1. Configurar la conexión PostgreSQL de **Development** fuera de Git, por ejemplo mediante `ConnectionStrings__DefaultConnection` en el entorno de la API. Confirmar que la base elegida es la de demostración, no una base con datos que se desee conservar.
2. Desde la raíz, ejecutar `dotnet tool restore` y `dotnet build ElCarbonazo.sln`. Después, desde `src/ElCarbonazo.Api`, ejecutar `dotnet ef database update` contra la base confirmada. La API no aplica migraciones automáticamente.
3. Desktop necesita `%ProgramData%\ElCarbonazo\desktop.json` con `ApiBaseUrl` e `idInstalacion` UUID válido. En una instalación nueva de demostración, crear ese archivo a partir de `deploy/desktop.example.json`, generar un UUID y poner como URL `https://localhost:7191/`. **No sobrescribir una configuración existente**: revisarla primero. El UUID sigue siendo necesario aun usando un override de Development.
4. Iniciar la API con el perfil HTTPS y la conexión de la base confirmada, y en otra terminal ejecutar Desktop. Para la base preparada `elcarbonazo_demo_local`, usar el iniciador anterior; el comando genérico `dotnet run --project src/ElCarbonazo.Api --launch-profile https` usa la conexión configurada, que puede ser otra base. El certificado debe ser confiable; nunca desactivar la validación TLS.
5. En Development, el seeder local crea o reutiliza la cuenta de demostración documentada en `docs/installation.md` solo bajo sus condiciones de seguridad. No usarla en producción ni alterar usuarios existentes manualmente.

### Recorrido universitario

Con datos ficticios: iniciar sesión, consultar mesas, registrar pedidos, avanzar las comandas en Cocina y revisar la cuenta en Caja. Caja registra efectivo, tarjeta o transferencia; no procesa pagos ni verifica bancos. El comprobante interno es de demostración, no una factura fiscal. La instalación de caja requiere el diario protegido descrito en `docs/installation.md`.

Para una comprobación automática aislada, ejecutar `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --caja-smoke` después del build. El anfitrión crea y limpia una base temporal; no abre una ventana ni sustituye la revisión visual. Ver `docs/validacion-004.md` para el alcance y las limitaciones.

### Si no conecta

- Confirmar que PostgreSQL y la API iniciaron correctamente, y que la URL del Desktop coincide con la dirección escuchada por la API.
- Un error de configuración exige revisar la ruta y el UUID; un error TLS requiere corregir confianza o hostname, no utilizar bypass.
- Si la API registra que falta `cobro` u otra tabla, confirmar a qué base apunta y si tiene las migraciones correspondientes; no modificar una base persistente sin autorización y respaldo.
- Si una escritura da resultado incierto, no repetirla automáticamente: revisar la advertencia. Un GET no demuestra que la escritura anterior haya terminado.

Para pruebas y configuración de instalación ver `AGENTS.md`, `docs/installation.md` y `docs/validacion-t5.md`.

## Arquitectura
- `src/ElCarbonazo.Api` — API REST + EF Core (solo BD)
- `src/ElCarbonazo.Desktop` — Cliente Avalonia (solo HTTP a API)
- `docs/` — Dominio, Constitución
- `specs/` — Especificaciones por módulo (SDD)

Pedidos y Cocina (`003`), Caja (`004`) y Auditoría/anulaciones (`006`) están aceptados para la demostración universitaria local. Consulta sus respectivos `tasks.md` para conocer el alcance y las tareas pendientes para uso real. La propuesta `002-operaciones-seguras` permanece separada y pospuesta.

La anulación de líneas requiere la primera provisión HMAC descrita en `docs/installation.md` y la clave `AutorizacionSuperior__ClaveHmac` configurada fuera de Git; sin ellas el resto del sistema funciona, pero la anulación responde `AUTORIZACION_SUPERIOR_NO_DISPONIBLE`.

Para comprobar que la base local elegida está preparada antes de presentar, ejecutar `.\scripts\validar-base-defensa.ps1 -BaseEsperada elcarbonazo` (solo lectura). Para el recorrido automático completo en bases temporales, `.\scripts\ejecutar-smoke-integral.ps1`.

Inventario y escandallo (`005`) tiene persistencia, API e interfaz implementadas y aceptación del usuario para el perfil universitario local; permanecen los límites documentados de validación. La API actual exige recetas y stock al enviar: no se ha preparado automáticamente la base persistente de demostración. Consultar `specs/005-inventario-escandallo/tasks.md`.
