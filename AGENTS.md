# El Carbonazo — instrucciones para agentes

## Alcance y fuentes
- MVP universitario local de autenticación y 15 mesas; no está aceptado para operación real. Mantener calidad lógica, estructural y visual aunque el endurecimiento de producción esté pospuesto.
- **SDD obligatorio:** antes de programar, leer y cumplir `specs/<módulo>/spec.md`, `plan.md` y `tasks.md`; no implementar funcionalidades sin especificación aprobada. `001-auth-mesas-mvp` es el módulo actual; `002-operaciones-seguras` es una propuesta pospuesta, no implementada.
- `docs/domain.md` define las reglas y nombres de dominio (`mesa.numero`, no `numero_mesa`); `docs/constitution.md` contiene las restricciones. Consultar `docs/bitacora-errores-y-lecciones.md` antes de cambiar áreas relacionadas y registrar lecciones confirmadas sin secretos. `docs/evolucion-futura-por-modulos.md` es inventario, no especificación.
- Mantener el sistema offline-first, sin dependencias de Internet en ejecución. No actualizar paquetes NuGet sin verificar compatibilidad; versiones directas en los `.csproj` y pautas de `tasks.md`.

## Arquitectura y reglas difíciles de inferir
- `src/ElCarbonazo.Api`: ASP.NET Core, EF Core y Npgsql; `Program.cs` configura servicios, autenticación, bootstrap y middleware. Controllers delegan reglas y persistencia a servicios. Desarrollo siembra el usuario local al arrancar, pero **no aplica migraciones automáticamente**; producción usa bootstrap explícito.
- `src/ElCarbonazo.Desktop`: Avalonia 12/SukiUI, bindings compilados y MVVM. Desktop y sus pruebas no deben referenciar API, EF Core ni Npgsql; acceso a datos únicamente por REST. Code-behind de vistas limitado a `InitializeComponent()`. `App.axaml.cs` ensambla transporte, sesión, navegación y shell; `SessionManager` posee tokens y ciclo de sesión.
- Mesa y grupo: la API es autoridad para permisos, transiciones y cuentas pendientes. Solo mesas libres sin grupo se agrupan; Cocina consulta sin mutar. `Agrupada` no prueba que exista un pedido. No inferir secuencia temporal comparando `version`/`xmin`.
- `MesasRequestCoordinator` serializa polling y mutaciones; descartar lecturas obsoletas. Ante timeout/red/5xx en una escritura no reintentar automáticamente: informar incertidumbre, reconciliar y exigir revisión humana; un GET **no** demuestra que la escritura anterior terminó.
- UI aprobada: `docs/diseno-visual-propuesta.md` y `docs/bitacora-errores-y-lecciones.md`. Mesas en tarjetas rectangulares oscuras con estado textual; navegación solo a destinos implementados. Validar contraste y estados reales de plantillas SukiUI, no solo compilación.

## Comandos desde la raíz
```bash
dotnet format ElCarbonazo.sln --verify-no-changes --no-restore
dotnet build ElCarbonazo.sln
dotnet test tests/ElCarbonazo.Api.Tests/ElCarbonazo.Api.Tests.csproj --no-build --no-restore
dotnet test tests/ElCarbonazo.Desktop.Tests/ElCarbonazo.Desktop.Tests.csproj --no-build --no-restore
dotnet test tests/ElCarbonazo.Desktop.Tests/ElCarbonazo.Desktop.Tests.csproj --filter "FullyQualifiedName~MesasRequestCoordinatorTests"
dotnet list ElCarbonazo.sln package --vulnerable --include-transitive
```
- Secuencia final de T6: formato → build → pruebas API → pruebas Desktop → auditoría de paquetes; `Directory.Build.props` trata advertencias como errores. En Windows, cerrar instancias API/Desktop antes de recompilar si bloquean sus binarios; no terminar procesos del usuario sin permiso.
- API local compatible con la configuración HTTPS habitual de Desktop: `dotnet run --project src/ElCarbonazo.Api --launch-profile https`. El perfil predeterminado `http` escucha en `http://localhost:5261`; `https` incluye `https://localhost:7191`. Comprobar la URL efectiva en lugar de asumir que coincide.
- Desktop: `dotnet run --project src/ElCarbonazo.Desktop`. Lee `%ProgramData%/ElCarbonazo/desktop.json` y exige un `idInstalacion` UUID válido incluso con override. `ELCARBONAZO_API_BASE_URL` solo se acepta si `ELCARBONAZO_ENVIRONMENT=Development`; HTTP se permite solo en ese entorno. Nunca omitir validación TLS de producción.
- EF CLI local fijado en `dotnet-tools.json` (`dotnet tool restore`; herramienta 10.0.14, paquetes EF 10.0.0). Ejecutar `dotnet ef` desde `src/ElCarbonazo.Api`; aplicar migraciones o bootstrap a una base persistente solo con autorización y destino confirmado. `docs/installation.md` explica el flujo.

## Pruebas y datos
- Las pruebas API necesitan PostgreSQL con permiso para crear/eliminar bases. Usan `ELCARBONAZO_TEST_ADMIN_CONNECTION` o, si falta, leen la conexión de Development; crean y eliminan `elcarbonazo_test_<uuid>` y no deben apuntar pruebas destructivas a una base existente. No imprimir ni versionar secretos.
- `tests/ElCarbonazo.AcceptanceHost` **no está en `ElCarbonazo.sln`**; compilarlo/probarlo por separado. `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --smoke` prepara y limpia una base temporal; `--cadencia` mide polling local sin render y `--medir` requiere interacción visual. Usa loopback HTTP de pruebas; cierre normal con Ctrl+C. Ver `docs/validacion-t5.md` para límites y limpieza.
- La aceptación T5/T6 es para el perfil universitario; la prueba física LAN, cobertura integral ≥80 % y medición cronometrada del render siguen pendientes antes de producción. Ver `docs/validacion-t5.md` y `docs/validacion-t6.md`; no afirmar validación visual o latencia no medidas.
