# Tareas 007 — cierre y entrega de la demo local

**Estado: alcance 006/007 aprobado; entrega local propuesta para fase posterior. Dirección del usuario: implementar reservas individuales y reporte sencillo, probar todo, luego entregar ejecutables y dump demo; PostgreSQL local nativo, no nube ni Docker para la defensa.**

## Cierre de alcance

- [ ] Limitar trabajo nuevo a reservas 006, reporte 007 y entrega local; mantener pausado el 006 de anulaciones sin borrar cambios locales.
- [ ] Mantener 002 pospuesto.

## Fase 1 — Pruebas aisladas

- [ ] Añadir plantilla de entorno sin secretos personales y script PowerShell que valide explícitamente el nombre de base demo PostgreSQL local.
- [ ] Reutilizar migraciones y seed actuales; preparar recetas/stock demo solo si hace falta para el flujo aprobado. Comprobar segunda ejecución sin duplicados.
- [ ] Probar reservas/reporte y los módulos existentes exclusivamente contra una base temporal/demo nueva; nunca contra la base original.

## Prueba de aceptación

- [ ] Ejecutar smoke: login → reserva → mesa/pedido → Cocina → Caja/comprobante → reporte → verificar consumo único.
- [ ] Ejecutar formato, build, suites API/Desktop y auditoría de paquetes.

## Fase 2 — Publicación para la defensa (solo tras Fase 1 aprobada)

- [ ] Publicar API y Desktop para Windows, sin credenciales personales ni configuración de producción.
- [ ] Generar dump PostgreSQL de una base demo ficticia recién creada; no exportar la base original.
- [ ] Probar restore del dump a una base demo nueva, checksum, conexión API/Desktop y recorrido de humo.
- [ ] Entregar el paquete (ejecutables + dump + guía) en un Release/Drive aprobado y comprobar descarga en otra laptop.

## Guía y cierre

- [ ] Escribir guía corta para clonar, configurar PostgreSQL local, levantar API/Desktop, restaurar el dump, presentar y detener.
- [ ] Registrar evidencia, límites y el hecho de que el recorrido es local/universitario, no LAN ni producción.
- [ ] Verificar el procedimiento en instalación limpia o documentar qué parte no pudo verificarse.

## Evidencia inicial (antes de preparar el paquete)

- [x] Formato y build correctos; 163 pruebas API y 267 Desktop aprobadas tras reservas/reporte.
- [x] Auditoría NuGet sin vulnerabilidades reportadas en los orígenes consultados.
- [x] `AcceptanceHost --pedidos-smoke`, `--caja-smoke` e `--inventario-smoke` pasaron usando bases PostgreSQL temporales creadas y limpiadas por las pruebas.
- [x] `--caja-smoke` comprueba reserva/mapa/cancelación y reporte comparado con cobros confirmados de prueba, además de pedido/Cocina/Caja; no valida render ni LAN.
- [ ] Verificar visualmente las nuevas pantallas y flujo interactivo con una sesión Desktop conectada; no se afirma que los smoke prueben render.
- [ ] Verificar el recorrido con API y Desktop en una instalación persistente nueva de demo; no usar la base personal/original.

## Pendiente/no incluido

- 006 de auditoría/anulaciones, 002, IVA/facturación fiscal, aceptación LAN, despliegue productivo, restauración/copia de la base original, instalador MSI y endurecimiento de producción.
