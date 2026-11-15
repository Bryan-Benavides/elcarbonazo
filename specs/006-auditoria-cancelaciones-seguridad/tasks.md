# Tareas propuestas 006 — Auditoría, cancelaciones y seguridad

**Estado: D0 aprobado por el usuario; C1 en curso.** Marcar una tarea solo con evidencia concreta. No aplicar migraciones a bases persistentes sin autorización expresa y destino confirmado.

## D0 — Especificación y aprobación

- [x] Crear borradores `spec.md`, `plan.md` y `tasks.md`, separados de 005; registrar decisiones del usuario sobre líneas completas e históricos sin desglose.
- [x] Inspeccionar código, contratos, entidades, grants y locks relevantes de 001/003/004/005 y documentar incompatibilidades; la comprobación de SQL generado e intercalaciones en PostgreSQL permanece en C1/C2.
- [x] Registrar inspección de `EnviosService`, `CajaOperacionesService`, `CajaCuentaLock`, `CajaElegibilidad`, `CajaCobroExecutor`, `CocinaService`, autenticación, grupos, mesas, grants y checks EF.
- [x] Registrar decisión del usuario: reautenticar siempre a Jefa, bloqueo superior separado propuesto de 5 fallos/5 minutos por par, y cierre explícito de grupo tras cancelación total.
- [x] Proponer detalles de intentos, identidad/retención/recuperación durable, alerta KDS, estados, legados y códigos de error, sujetos a aprobación.
- [x] Redactar adendas propuestas a 003/004/005 y nota propuesta en `docs/domain.md`, distinguiéndolas de funcionalidad implementada.
- [x] Aprobar modelo, contratos, matriz de roles, orden de locks propuesto y pruebas esenciales; el usuario autorizó comenzar C1 en bases temporales, incluidas las adendas y el bloqueo conservador de históricos. No implica autorizar migraciones persistentes ni prueba de concurrencia.

## C1 — Persistencia y grants

**Avance parcial:** modelo EF inicial y migración aditiva `AddAuditoriaCancelacionesSeguridad` generados; `Down` bloqueado. PostgreSQL temporal cubrió preservación representativa de pedido pagado, comanda, detalle, consumo, turno, cobro y comprobante 005, ausencia de snapshots fabricados y algunos CHECK/FK. Una prueba con rol runtime temporal ejecutó inserciones/actualizaciones de persistencia 006 y verificó unicidad y denegación de modificaciones de evidencia/auditoría; no es una cancelación de negocio. Se adaptaron pruebas de migración histórica 003/004/005 para usar un modelo EF histórico antes de migrar, sin alterar migraciones antiguas. Pasaron formato, build, 144 API, 262 Desktop y auditoría NuGet sin vulnerabilidades reportadas.

**Pendiente antes de cerrar C1:** ampliar pruebas de inserción/FK/constraints de entidades, límites y reversión, e inspeccionar SQL y condiciones de integridad. La FK RESTRICT de autorización conserva su sesión; la limpieza actual solo borra historial de refresh, no sesiones. Debe precisarse la provisión/rotación HMAC antes de C2. Las FKs individuales no garantizan coherencia entre pedido/comanda/detalle; restricciones entre filas y consumo acumulado requieren controles y pruebas transaccionales de C2. No se ha implementado cancelación funcional. Ver `docs/validacion-006.md`.

- [ ] Diseñar y probar evidencia por línea, anulaciones, autorización y operación durable, alertas y restricciones; mantener historial append-only.
- [ ] Crear migración aditiva con `Down` protegido y revisar SQL; migrar desde vacío y 005 representativo solo en PostgreSQL temporal, sin fabricar históricos.
- [ ] Actualizar grants mínimos y comprobarlos con rol temporal no propietario, incluidos locks, secuencias y denegación de UPDATE/DELETE históricos.

## C2 — API, seguridad, inventario y KDS

- [ ] Validar credencial superior, permisos, límites de intentos y auditoría de fallos sin secretos; probar expiración, sesión, rol, recurso y replay.
- [ ] Implementar identidad durable, ejecución única y barrera/reconciliación explícitas, sin reenvío automático y con pruebas de respuesta perdida.
- [ ] Cancelar líneas completas o comandas, bloquear evidencia insuficiente y cuentas pagadas/en cobro; devolver consumo exacto con recálculo y auditoría atómicos.
- [ ] Implementar alertas y acuse por evento, consultas paginadas y autorización por rol.
- [ ] Probar carreras reales con Caja prepare/execute, envío, KDS y ajuste; rollback por fallo de auditoría y ausencia de duplicación.

## C3 — Desktop

- [ ] Clientes REST y coordinador sin reintentos; tratamiento de credenciales, cambio de sesión, incertidumbre y recuperación conforme a D0.
- [ ] Modal con resumen, motivo y confirmación; KDS con alerta textual y acuse; vistas paginadas de auditoría por rol.
- [ ] Pruebas con fakes de validación, lecturas obsoletas, respuestas inválidas y un solo envío; revisión de foco, teclado, contraste y escalado en ventana real.

## C4 — Integración y aceptación

- [ ] Ejecutar formato → build → pruebas API → pruebas Desktop → auditoría NuGet; AcceptanceHost aparte y recorridos ficticios en PostgreSQL temporal.
- [ ] Registrar evidencia y limitaciones en `docs/validacion-006.md`, incluidos casos no probados y límites de recuperación.
- [ ] Realizar recorrido práctico, solicitar revisión y aceptación explícita del usuario; no declarar LAN física, aptitud productiva ni validación visual no medida.
