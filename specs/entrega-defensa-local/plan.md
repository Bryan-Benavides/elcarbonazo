# Plan técnico — cierre y entrega de la demo

**Estado: propuesta pendiente de aprobación concreta; no autoriza cambios a una base persistente.**

## Enfoque mínimo

1. Inspeccionar los comandos actuales de migración, catálogo de demostración, recetas e inventario para reutilizarlos antes de crear otro mecanismo.
2. **Fase 1:** fijar/documentar la versión de PostgreSQL local compatible y crear una base vacía explícita `elcarbonazo_demo`; no asumir Docker ni nube.
3. Añadir `.env.example` sin secretos operativos y scripts PowerShell que comprueben `psql`/`pg_restore`, validen el nombre de base demo y ejecuten migración/seed solo contra ese destino. Evitar automatización oculta al iniciar API.
4. Si la funcionalidad actual no puede preparar recetas/stock, añadir datos sintéticos deterministas acotados a `elcarbonazo_demo_*`. No inventar pedidos pagados ni usar credenciales reales.
5. Probar migración limpia, seed repetible y smoke de reservaciones, salón, cocina, caja, inventario y reporte en una base temporal.
6. **Fase 2, después de aprobar Fase 1:** publicar `dotnet publish` Windows de API/Desktop y un dump PostgreSQL custom-format de la base demo sintética. Añadir procedimiento de restore a un DB nuevo, validación de checksum y conexión local.
7. Comprobar que ejecutables y dump no contienen secretos personales; preparar guía Windows y guion de demo de 15 minutos.

## Seguridad y preservación

- No exportar ni leer secretos/datos de la base original; no se requiere para la entrega.
- El dump distribuido debe generarse desde la base demo ficticia, no desde la base original del usuario. Un dump no reemplaza migraciones ni contiene los binarios publicados. No incluir el directorio `data` de PostgreSQL; usar `pg_dump`/`pg_restore`.
- No añadir contraseñas JWT/HMAC de producción ni credenciales personales al repositorio. Los valores demo deben ser locales, desechables y explícitamente no seguros.
- No ejecutar `docker compose down -v` de forma automática; borrar un volumen requiere acción explícita del usuario.
- No hacer auto-migración al arrancar API. Aplicar migraciones solo a la base demo nombrada por el script.
- Para generar el dump se usará exclusivamente una base `elcarbonazo_demo_*` ficticia; el restore será a otra base recién creada.

## Validación

Secuencia Fase 1: validar formato → build → pruebas API → pruebas Desktop → pruebas focalizadas 006/007 → smoke de demo aislado → auditoría NuGet → comprobar guía/comandos desde instalación limpia. Fase 2 solo empieza con Fase 1 verde: publicar binarios, generar/restore dump en una base distinta, verificar checksum y probar arranque sin Internet. No paralelizar builds de la misma solución.
