# Plan técnico — cierre y entrega de la demo

**Estado: alcance actualizado por solicitud del usuario. No autoriza cambios a una base persistente hasta confirmar el destino exacto de defensa.**

## Enfoque mínimo

1. Inspeccionar los comandos actuales de migración, catálogo, recetas e inventario; reutilizar contratos y servicios. La carga nueva debe ser explícita, completa y segura, no una mutación del seeder demo anterior.
2. **Fase 1:** el usuario confirmó PostgreSQL local y autorizó expresamente `elcarbonazo` como destino; hizo un respaldo. Preservar registros preexistentes y verificar que las tablas de catálogo e inventario estén vacías. Las pruebas destructivas se mantienen limitadas a bases temporales creadas para ellas.
3. Añadir `.env.example` sin secretos operativos y scripts PowerShell que comprueben `psql`/`pg_restore`, validen el nombre de base demo y ejecuten migración/seed solo contra ese destino. Evitar automatización oculta al iniciar API.
4. Versionar los archivos fuente del catálogo y preparar herramienta para productos, ingredientes y recetas suministrados, con prevalidación total, transacción, confirmación exacta de destino y aborto si catálogo o inventario no están vacíos. Preservar tablas ajenas, incluido el pedido abierto. Crear stock únicamente mediante movimientos `EntradaCompra` auditados. No inventar cantidades ni pedidos pagados ni usar credenciales reales.
5. Probar migración limpia, seed repetible y smoke de reservaciones, salón, cocina, caja, inventario y reporte en una base temporal.
   Para la revisión manual se habilita `AcceptanceHost --catalogo-defensa`: crea su propia base temporal, carga los mismos archivos y permanece abierto hasta Ctrl+C. No combina este modo con el seeder ficticio anterior ni modifica `elcarbonazo`.
6. **Fase 2, después de aprobar Fase 1:** publicar `dotnet publish` Windows de API/Desktop y un dump PostgreSQL custom-format de la base local de defensa aislada. Añadir procedimiento de restore a un DB nuevo, validación de checksum y conexión local.
7. Comprobar que ejecutables y dump no contienen secretos personales; preparar guía Windows y guion de demo de 15 minutos.

## Seguridad y preservación

- No exportar ni leer secretos/datos de ninguna base preexistente; no se requiere para la entrega.
- El dump distribuido debe generarse exclusivamente desde la base local de defensa recién creada, nunca desde base original, temporal de pruebas ni otra base preexistente. Un dump no reemplaza migraciones ni contiene los binarios publicados. No incluir el directorio `data` de PostgreSQL; usar `pg_dump`/`pg_restore`.
- No añadir contraseñas JWT/HMAC de producción ni credenciales personales al repositorio. Los valores demo deben ser locales, desechables y explícitamente no seguros.
- No ejecutar `docker compose down -v` de forma automática; borrar un volumen requiere acción explícita del usuario.
- No hacer auto-migración al arrancar API. Aplicar migraciones solo a la base demo nombrada por el script.
- Para generar el dump se usará exclusivamente una base `elcarbonazo_demo_*` de defensa aislada; el restore será a otra base recién creada.

## Validación

Secuencia Fase 1: validar formato → build → pruebas API → pruebas Desktop → pruebas focalizadas 006/007 → smoke de demo aislado → auditoría NuGet → comprobar guía/comandos desde instalación limpia. Fase 2 solo empieza con Fase 1 verde: publicar binarios, generar/restore dump en una base distinta, verificar checksum y probar arranque sin Internet. No paralelizar builds de la misma solución.
