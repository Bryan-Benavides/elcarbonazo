# Plan técnico 006 — Reservaciones sencillas

**Estado: aprobado para implementación y pruebas temporales; no ejecutar migraciones en bases persistentes.**

1. Agregar entidad `Reservacion` con FK restrictiva a `Mesa`, nombre de referencia, cantidad, inicio/fin UTC, estado y timestamps. Índice por mesa/estado/inicio/fin; no introducir extensión PostgreSQL.
2. Serializar altas por fila de `mesa` (`FOR UPDATE`), comprobar existencia/estado/aforo, validar que el intervalo no se solape con una reservación `Activa` y grabar la auditoría dentro de la transacción. La consulta previa no reserva el espacio.
3. Crear DTOs REST y servicios; disponibilidad/listado filtran intervalo UTC y el endpoint mutante vuelve a validar. Cancelación idempotente y auditada.
4. Agregar un resumen de próxima reservación al DTO de mesa, derivado con hora actual del servidor; preservar `numero`, `estado` y `version`. No comparar `xmin` para ordenar eventos.
5. Desktop agrega un formulario compacto de mesa, alias, aforo e inicio/fin; lista próximas reservas, muestra errores 409 sin reintentar y deja cancelar con confirmación. Mapa muestra una etiqueta visible con fecha/hora local, no solo color.
6. Pruebas PostgreSQL temporales: migración, solapamiento/contigüidad, concurrencia observada, aforo, cancelación/reuso, permisos, auditoría y preservación de pedidos/grupos. Pruebas Desktop: validación, sesión, conflictos y lectura obsoleta.
7. No añadir usuarios de cliente/contacto ni integración externa. No cambiar regla de impuestos/Caja.
