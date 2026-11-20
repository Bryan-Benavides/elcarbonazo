# Especificación 006 — Reservaciones sencillas de mesas

**Estado: APROBADO por el usuario para la defensa local.** El anterior 006 de auditoría/anulaciones queda pausado y fuera de la defensa; su código local y evidencia no se borran. La agrupación y cuenta compartida ya implementadas no se rehacen.

## Objetivo y alcance

Permitir a salón comprobar y registrar una reservación programada para **una mesa individual**, evitando dos reservaciones activas solapadas. Mostrar la próxima reservación en el mapa. Los grupos numerosos continúan usando agrupación existente, sin reservación de múltiples mesas.

Incluye PostgreSQL, endpoints REST, vistas de disponibilidad/listado/alta/cancelación y un indicador textual en Mapa. Fechas almacenadas en UTC; Desktop presenta hora local de Managua.

## Reglas mínimas

- Una reservación contiene mesa, nombre/alias de referencia (1–60 caracteres), cantidad de personas (1..capacidad), inicio/fin UTC y estado `Activa` o `Cancelada`.
- Duración debe ser positiva y no superar 8 horas; inicio entre ahora y 365 días. Se rechazan fechas inválidas y control characters. No se exigen teléfono, adelanto, asignación de personal ni notificaciones.
- Disponibilidad usa intervalo semiabierto `[inicio, fin)`: dos rangos que solo comparten el límite no se solapan. Solo reservaciones activas ocupan disponibilidad.
- El GET de disponibilidad es informativo; el POST de creación vuelve a verificar y reservar atómicamente, serializando por mesa. Conflicto responde 409; nunca depender solo del GET.
- Cancelar es explícito e idempotente; no cambia pedidos ni libera/ocupa mesas. Abrir un pedido desde una mesa `Reservada` conserva las reglas vigentes de 001/003.
- La agenda lista solo reservas activas; las canceladas se conservan en la base como historial y no ocupan disponibilidad.
- El mapa añade una indicación separada para la próxima reservación, sin transformar una reserva futura en estado actual ni reemplazar estado textual/color de mesa.
- Roles para consultar: los cinco roles existentes. Crear/cancelar: Jefa, Encargado de Caja, Mesero y Supervisor; Cocina solo lectura.
- Sin calendario avanzado, recurrencia, reservas de grupos, conflictos de capacidad entre reservas, SMS/correo, gestión de clientes ni informes de asistencia/no-show.

## Rutas mínimas propuestas

- `GET /api/reservaciones/disponibilidad?desdeUtc=&hastaUtc=&personas=`
- `GET /api/reservaciones?desdeUtc=&hastaUtc=`
- `POST /api/reservaciones`
- `POST /api/reservaciones/{id}/cancelar`

Los contratos finales se fijan en el plan. Las mutaciones requieren sesión/rol, límites de body y auditoría transaccional existente. No aplicar migraciones persistentes sin autorización y destino confirmado.

## Aceptación

Crear, consultar, detectar solapamiento, aceptar rangos contiguos, cancelar y reservar nuevamente en PostgreSQL temporal; forzar dos POST concurrentes para la misma mesa/rango y comprobar que solo uno gana. Comprobar roles, capacidad, fechas y mensajes en Desktop. Mantener los tests de 001–005 verdes.
