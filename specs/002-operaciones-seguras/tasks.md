# Tareas — Operaciones de mesa recuperables

> **Pospuestas:** no bloquean T5/T6 del perfil universitario. Se conservan para revisión si se decide endurecer el proyecto en una etapa posterior.

- [ ] **S1: Cerrar diseño y compatibilidad** — Alinear referencias del módulo 001, fijar límites operativos iniciales y despliegue coordinado; revisar técnicamente garantías de concurrencia, auditoría, permisos y recuperación antes de implementar. El diseño continúa pendiente de esa validación; no marcarlo completado solo por editar documentos.
- [ ] **S2: Persistencia API** — Modelo, constraints, índices, migración posterior, serialización tipada/canónica y marca terminal durable; pruebas sobre bases PostgreSQL temporales.
- [ ] **S3: Transacciones de dominio** — Refactorizar mesas/grupos para una transacción exterior única con auditoría; preservar reglas existentes, orden de locks y comportamiento ante conflictos.
- [ ] **S4: Protocolo API** — Preparación, ejecución, consulta, barrera de cancelación y archivo; recuperación supervisada por Jefa/Supervisor sin ejecutar operación ajena, con auditoría transaccional; deshabilitar explícitamente mutaciones legadas; autorización, errores y pruebas de carreras/caídas/compatibilidad.
- [ ] **S5: Recuperación Desktop** — Persistencia local segura, contratos y servicios nuevos, recuperación por usuario, cancelación segura, sesión y pruebas con fakes. No guardar secretos ni ejecutar automáticamente tras reinicio.
- [ ] **S6: Integración T5** — Coordinador, polling, acciones de mesa/grupo, estados y reconciliación de acuerdo con 001 y 002; no marcar T5 hasta verificar criterios aplicables.
- [ ] **S7: Verificación** — Formato, build, pruebas API en PostgreSQL temporal, pruebas Desktop, revisión de aislamiento, auditoría NuGet y documentación de despliegue/recuperación. T3.10 y checklist visual T6 siguen pendientes cuando requieran entorno físico.
