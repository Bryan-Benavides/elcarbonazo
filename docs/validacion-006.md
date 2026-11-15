# Validación observada — 006 Auditoría, cancelaciones y seguridad

## C1 en curso

Se generó la migración EF aditiva `AddAuditoriaCancelacionesSeguridad` sobre 005 con `Down` bloqueado y un primer modelo para snapshots de consumo por línea, anulación, autorización, operación durable, alertas y límites; se ampliaron checks de movimiento y grants. No hay aún servicios ni interfaz de cancelación, y C1 no se considera completado.

En PostgreSQL temporal se comprobó una migración desde 005 con pedido pagado, comanda, detalle, consumo, turno, cobro y línea de comprobante, conservando importes e IDs representativos y sin fabricar snapshots ni anulaciones; también se comprobó el bloqueo de `Down`. Pruebas adicionales cubren algunos CHECK de operación, límite, consumo, acuse, hash y vigencia de autorización, FK de sesión y hash duplicado.

La prueba de grants consultó privilegios y, con rol temporal no propietario, insertó snapshots, anulación, alerta y movimiento positivo ficticios, actualizó las columnas permitidas y comprobó unicidad de snapshot y de reversión por anulación/ingrediente. Se rechazaron UPDATE/DELETE de evidencia y auditoría por falta de permisos. Este ejercicio SQL prueba persistencia/permisos, **no** autoriza anular una cuenta pagada ni demuestra el servicio de negocio; tampoco demuestra que diferentes anulaciones no superen el consumo original.

Al añadir columnas de 006, dos pruebas históricas que poblaban esquemas anteriores con el modelo EF actual fallaron por columnas inexistentes. Se adaptaron a un modelo histórico explícito antes de migrar; no se cambiaron migraciones anteriores. Tras ampliar pruebas pasaron formato, compilación, **144 pruebas API y 262 Desktop**; la auditoría NuGet no reportó vulnerabilidades en los orígenes consultados.

La revisión de `RefreshTokenCleanupService` muestra que limpia filas de `token_refresh_sesion`, no elimina `sesion_usuario`. La FK RESTRICT de autorización impide borrar una sesión referenciada; se verificó ese rechazo en PostgreSQL temporal. No se ha probado una política futura de borrado de sesiones ni implementado provisión/rotación de HMAC; cualquier limpieza futura debe respetar la retención de autorizaciones.

Faltan escenarios adicionales de FK, índices, límites, coherencia entre pedido/comanda/detalle y reversión acumulada; la base actual usa FKs individuales y no garantiza por sí sola esa coherencia entre filas. Los tests de C2 deben demostrarla bajo locks, junto con atomicidad, autorización y concurrencia. También falta concretar y validar el aprovisionamiento y rotación de la clave HMAC antes de implementar el límite superior. Sin esas comprobaciones no se afirma C1 completo, seguridad operativa, LAN ni aptitud de producción. No se aplicaron migraciones a bases persistentes.
