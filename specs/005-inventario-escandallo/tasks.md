# Tareas 005 — Inventario y escandallo

**Estado: C1 y C2 completados para el perfil universitario local aislado; C3–C4 pendientes.** La API aplica inventario al enviar, pero aún falta la interfaz y la aceptación integral de 005. No autoriza migraciones a bases persistentes.

## D0 — Especificación y decisión

- [x] Preparar `spec.md`, `plan.md` y estas tareas, incluyendo compatibilidad 003/004, modelo, roles, locks, límites y pruebas.
- [x] Documentar adenda propuesta a 003 y distinguir diseño de funcionalidad implementada en el dominio.
- [x] Aprobar receta obligatoria, política de unidad, roles de lectura y límites de incertidumbre. El usuario autorizó adoptar las opciones recomendadas para la lógica del negocio y los márgenes establecidos.
- [x] Inspeccionar el envío y contratos existentes y concretar versiones, normalización Unicode, límites de líneas, catálogo y orden previsto. No sustituye las pruebas reales de SQL y carreras de C1/C2; volver a aprobar cualquier cambio funcional.

## C1 — Persistencia

**Evidencia C1:** `InventarioPersistenceTests` probó en PostgreSQL temporal migración desde 004 con pedido, comanda, pago y líneas de comprobante, ausencia de consumos fabricados, reversión bloqueada conservando un movimiento, precisión decimal y restricciones esenciales, incluida unicidad de consumo por comanda/ingrediente. La prueba de grants ejecutó altas, locks, cambios y reemplazo de líneas con rol temporal limitado y confirmó el rechazo de UPDATE/DELETE del kardex. No acredita todos los escenarios ni un despliegue real.

- [x] Entidades y Fluent API; restricciones e índices sin modificar migraciones anteriores. Los eventos funcionales y rollback de auditoría se implementan y prueban en C2.
- [x] Migración aditiva y `Down` protegido; validar preservación representativa 003/004 y ausencia de consumos fabricados en PostgreSQL temporal.
- [x] Grants mínimos, secuencias y prueba con rol runtime temporal no propietario; preservar append-only.

Formato, build y 232 pruebas Desktop pasaron. Una ejecución completa de API terminó por tiempo; la siguiente pasó 128 pruebas. Tras añadir una aserción adicional de unicidad/reversión se volvieron a ejecutar las cuatro pruebas focalizadas con éxito, no toda la suite. Auditoría NuGet sin vulnerabilidades reportadas en los orígenes consultados.

## C2 — API y Pedidos

**Evidencia:** API incorpora rutas y servicios de inventario/recetas y `EnviosService` agrega y descuenta bajo locks. Las pruebas cubren operaciones/auditoría, roles de las nueve rutas HTTP, faltantes, agregación, replay y dos pedidos compitiendo por stock. La competencia de pedidos ejecuta solicitudes simultáneas sin forzar ambos órdenes; las pruebas de reemplazo de receta/envío y creación de receta/inactivación observaron bloqueos PostgreSQL en ambos órdenes. Otra prueba comprobó que inactivación espera un envío y rechaza versión obsoleta. No se afirma una matriz exhaustiva de intercalaciones ni pruebas físicas LAN.

**Regresión:** las pruebas anteriores preparan recetas y stock ficticios explícitos en PostgreSQL temporal; la migración histórica crea su historial con el esquema 004, sin invocar el envío 005 antes de migrar. Pasaron formato, build, 141 pruebas API, 232 Desktop y auditoría NuGet sin vulnerabilidades reportadas. `AcceptanceHost --pedidos-smoke` y `--caja-smoke` pasaron con inventario ficticio temporal; el smoke de caja comprobó dos consumos por dos envíos y que los cobros no los duplican. Usa REST/ViewModel y sesión/diario adaptados, sin ventana ni validación de LAN. La demostración anterior no debe declararse operativa con el nuevo código hasta preparar una base autorizada y completar C3/C4.

- [x] Servicios, validaciones, contratos REST, permisos y auditoría transaccional; pruebas HTTP y de servicios cubren los casos esenciales, no todas las combinaciones.
- [x] Descuento agregado al enviar, bloqueo de productos/recetas/ingredientes y rechazo sin efectos parciales.
- [x] Conservar idempotencia 003 y compatibilidad Caja. Las limitaciones de incertidumbre/reinicio permanecen; la coordinación UI de nuevas escrituras corresponde a C3.
- [x] Probar carreras esenciales, fallos de auditoría, roles, límites, duplicados y persistencia para el perfil local; la matriz exhaustiva queda fuera de esta aceptación parcial.

## C3 — Desktop

**Avance:** Pedidos muestra faltantes validados y conserva el borrador. La vista incluye consulta y filtro de ingredientes, alertas, kardex paginado y, para Jefa, creación/edición de ingredientes, entradas/ajustes y escandallo editable con versión, selección y confirmación. El coordinador compartido conserva el bloqueo incierto al cerrar/reabrir y no usa GET para desbloquear. Pasaron formato, build y 256 pruebas Desktop. `AcceptanceHost --inventario-smoke` comprobó creación, entrada, edición, kardex y guardado de receta con ViewModel/REST y PostgreSQL temporal; usa sesión adaptada y no abre ventana. Faltan buscador de productos, ampliar pruebas de formularios/errores y revisión visual de estados reales; C3 sigue abierto.

- [ ] Clientes REST y DTOs, formularios E-04/S-05, navegación por rol y validación decimal exacta.
- [ ] Integrar faltantes y receta ausente en Pedidos; conservar borrador y reglas de intención.
- [ ] Coordinar sesión, lecturas obsoletas e incertidumbre; probar fakes y respuestas inválidas.

## C4 — Integración y aceptación

- [ ] Ejecutar pruebas esenciales API/Desktop y recorridos ficticios de mesa y grupo hasta Caja, sin doble descuento.
- [ ] Ejecutar formato → build → tests API → tests Desktop → auditoría de paquetes; AcceptanceHost aparte si se utiliza.
- [ ] Registrar evidencia real y limitaciones en `docs/validacion-005.md`; solicitar revisión práctica y aceptación del usuario.
- [ ] Registrar explícitamente alcance universitario y pendientes posteriores, sin afirmar validación de producción ni LAN.

## Posterior, fuera de aceptación universitaria

- [ ] Diseñar recuperación durable de escrituras administrativas, conciliación entre terminales y tras reinicio antes de uso que la requiera.
- [ ] Evaluar anulación/devolución, unidades convertibles, costos, compras reales y requisitos operativos solo con nueva especificación aprobada.
- [ ] Evaluar seguridad, instalación, auditoría operativa, restauración y validación física antes de cualquier uso real.
