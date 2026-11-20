# Especificación 007 — Reporte sencillo de ventas

**Estado: APROBADO por el usuario para la defensa local.** El reporte es administrativo y de solo lectura para el perfil local de demostración.

## Fuente de datos y alcance

- Usar cobros confirmados de Caja (`cobro`) y snapshot de líneas del comprobante; no usar una tabla `factura`, porque no existe en el esquema efectivo.
- Filtrar por rango semiabierto `[desdeUtc, hastaUtc)`, convertir las fechas en Desktop a Managua; el límite inicial se incluye y el final se excluye.
- Mostrar total cobrado, cantidad de cuentas, ticket promedio, suma de `impuestos` realmente registrados y ranking de 10 productos por unidades vendidas, con nombre/precio históricos.
- Mostrar `Impuestos registrados (demostración)`, no `IVA recaudado`: Caja/003 fijan impuestos en cero y comprobante no fiscal. No cambiar impuestos ni crear factura fiscal.
- Jefa y Supervisor pueden consultar; otros roles reciben 403.
- No exportación CSV/PDF, objetivos, gráficos, desglose de utilidad/costo, datos de clientes, consulta en tiempo real ni reportes de inventario.

## Aceptación

Pruebas con cobros ficticios: límites de fechas, inclusividad documentada, conteos/sumas/promedio, top 10, cero ventas, productos renombrados después del cobro y autorización por rol. La UI presenta totales y ranking con estado vacío, error y paginación/rango válidos.
