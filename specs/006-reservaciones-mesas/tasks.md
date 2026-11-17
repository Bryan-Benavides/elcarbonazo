# Tareas 006 — Reservaciones sencillas

**Estado: especificación y plan aprobados por el usuario; implementación iniciada. No ejecutar migraciones en bases persistentes.**

## API y persistencia

- [x] Entidad, configuración EF y migración aditiva `20261117012501_AddReservaciones`; `Down` protegido.
- [x] Disponibilidad/listado UTC y creación transaccional con fila de mesa bloqueada para controlar solapamiento.
- [x] Cancelación idempotente y auditoría.
- [x] Consulta de próxima reservación para mesa sin cambiar su estado de negocio.
- [x] Pruebas temporales de intervalos contiguos, aforo, carrera concurrente, cancelación y auditoría.
- [x] Prueba HTTP de autorización/contratos para reservas y reporte inicial.

## Desktop

- [x] Formulario compacto, lista y cancelación con confirmación.
- [x] Etiqueta de próxima reservación con hora local legible en tarjeta de mesa.
- [x] Errores de conflicto, desconexión y sesión sin reintentar escrituras.
- [x] Pruebas ViewModel de incertidumbre y pruebas REST de creación, cancelación, disponibilidad y permisos.
- [ ] Revisión visual de la vista real.

## Aceptación

- [x] Probar dos reservas concurrentes para el mismo espacio/hora en PostgreSQL temporal; exactamente una se confirma.
- [x] Ejecutar formato → build → API (163 pruebas) → Desktop (267 pruebas) → auditoría NuGet.
- [x] En el mismo entorno temporal, comprobar por REST reserva/mapa/cancelación y el recorrido de pedido/Cocina/Caja; sin ventana ni render.
