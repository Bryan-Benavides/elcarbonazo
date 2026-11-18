# Tareas 007 — cierre y entrega de la demo local

**Estado: alcance de entrega actualizado por solicitud del usuario: preparar catálogo de defensa con los 90 productos, recetas e ingredientes proporcionados en una base PostgreSQL local aislada; validar flujos y después entregar ejecutables y dump restaurable. PostgreSQL local nativo, sin nube ni Docker.**

## Cierre de alcance

- [x] Limitar trabajo nuevo a anulaciones 006, reservas, reporte y entrega local. El 006 de anulaciones se reanudó por autorización del usuario, se implementó y quedó aceptado para el perfil universitario local, sin borrar cambios previos.
- [x] Mantener 002 pospuesto.

## Fase 1 — Pruebas aisladas

- [ ] Añadir plantilla de entorno sin secretos personales y script PowerShell que valide explícitamente el nombre de la base PostgreSQL local de defensa.
- [x] Definir y probar carga explícita de los 90 productos, ingredientes, recetas y existencias suministrados; abortar ante datos existentes incompatibles. Stock inicial por movimientos auditados; repetición idéntica sin cambios.
- [x] Transcribir y validar las fuentes versionadas iniciales de productos (90 filas) e ingredientes (116 filas). Mantequilla se corrigió a gramos por indicación posterior del usuario.
- [x] Transcribir 90 recetas por porción (452 líneas) y comprobar referencias, identificadores y stock inicial suficiente para una porción de cada receta individualmente. Esto no verifica la transcripción línea por línea ni garantiza stock para las 90 porciones juntas.
- [x] Crear `scripts/validar-catalogo-defensa.py` y ejecutarlo sin conexión: valida estructura, precios, nombres, unidades, cantidades y referencias. No comprueba aún fidelidad individual de la transcripción ni prueba de importación.
- [x] Preparar y probar `CatalogoDefensaSeeder`: PostgreSQL temporal verificó carga completa, repetición idéntica sin duplicación, rechazo de producto modificado, conservación de pedido abierto y rollback al fallar auditoría. El comando explícito exige Development, nombre de base confirmado, localhost y una Jefa activa.
- [x] Con autorización del usuario cargar `elcarbonazo`; una consulta posterior contó 90 productos, 116 ingredientes, 90 recetas activas, 452 líneas, 116 entradas y un pedido abierto.
- [x] `AcceptanceHost --catalogo-defensa-smoke` cargó el catálogo en base temporal y probó pedidos individual y grupo, Cocina, Caja y ausencia de doble consumo; sesión adaptada, sin render ni LAN. Pasaron 165 pruebas API y 268 Desktop; el primer intento completo API agotó cinco minutos y la repetición pasó.
- [ ] Habilitar y probar `AcceptanceHost --catalogo-defensa` para revisión manual del catálogo nuevo en una base temporal sin cambiar la principal.
- [ ] Validar visualmente el recorrido con `elcarbonazo`: las migraciones y el catálogo ya están aplicados; el smoke comprobó el recorrido en base temporal, no en la ventana persistente. Las anulaciones 006 ya están implementadas, pero su demostración exige la primera provisión HMAC y `AutorizacionSuperior__ClaveHmac` configurada.
- [ ] Probar reservas/reporte y los módulos existentes exclusivamente contra una base temporal/demo nueva; nunca contra la base original. Usar `elcarbonazo` para la defensa en vivo es una decisión del usuario y deja de ser una copia prístina.
- [ ] Resolver unidades/nombres incompatibles y completar cualquier dato de stock faltante antes de activar todas las recetas; no inventar conversiones ni existencias.

## Prueba de aceptación

- [ ] Ejecutar smoke: login → reserva → mesa/pedido → Cocina → Caja/comprobante → reporte → verificar consumo único. `--caja-smoke` ya cubre ese recorrido; `scripts\ejecutar-smoke-integral.ps1` lo ejecuta junto con `--anulaciones-smoke` e `--inventario-smoke` en bases temporales separadas.
- [ ] Ejecutar formato, build, suites API/Desktop y auditoría de paquetes.

## Fase 2 — Publicación para la defensa (solo tras Fase 1 aprobada)

- [ ] Publicar API y Desktop para Windows, sin credenciales personales ni configuración de producción.
- [ ] Generar dump PostgreSQL de la base local de defensa aislada recién creada; no exportar ninguna base preexistente ni temporal de pruebas.
- [ ] Probar restore del dump a otra base local de defensa recién creada, checksum, conexión API/Desktop y recorrido de humo.
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

## Verificación de la base personal `elcarbonazo` (18-nov-2026)

Consulta de solo lectura acordada con el usuario para decidir si puede usarla en la defensa:

- [x] 18 migraciones EF aplicadas; la última es `20261117012501_AddReservaciones`, e incluye `20261115164448_AddAuditoriaCancelacionesSeguridad`.
- [x] Catálogo presente: 90 productos, 116 ingredientes activos, 90 recetas activas, 452 líneas de receta; 0 ingredientes con stock cero o bajo mínimo.
- [x] Una sola cuenta de acceso: `admin`, rol `Jefa`, activa.
- [x] 15 mesas; la 1 y la 2 quedaron `Ocupada` por datos heredados, y 12 grupos de mesa sin uso.
- [x] Un turno de caja `Abierto` desde el 17-nov, un cobro en efectivo de 60.00 con su comprobante y 6 consumos de inventario.
- [ ] `estado_hmac_autorizacion` está en `provisionado = false`: la anulación 006 responderá `AUTORIZACION_SUPERIOR_NO_DISPONIBLE` hasta ejecutar la primera provisión.
- [ ] El pedido abierto 1 (mesa 1, total 115.00) no tiene comandas ni líneas: no se puede cobrar ni liberar la mesa con las rutas actuales. Requiere decisión del usuario (conservarlo, corregir la base con autorización o trasladar la defensa a una copia).
- [x] La clave `AutorizacionSuperior__ClaveHmac` no estaba definida en el entorno; se configuró y aprovisionó durante esta verificación (ver `docs/validacion-006.md`).

## Pendiente/no incluido

- 002, IVA/facturación fiscal, aceptación LAN, despliegue productivo, restauración/copia de la base original, instalador MSI y endurecimiento de producción.
