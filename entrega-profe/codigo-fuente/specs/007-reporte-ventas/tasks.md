# Tareas 007 — Reporte sencillo de ventas

**Estado: especificación y plan aprobados por el usuario; implementación iniciada. No modificar reglas fiscales ni datos persistentes.**

- [x] Confirmar el contrato/rango del reporte y los campos históricos disponibles en snapshot de comprobante.
- [x] Implementar query GET agregado de solo lectura con `AsNoTracking`.
- [x] Restringir a Jefa/Supervisor.
- [x] Implementar filtros y tabla de métricas/ranking en Desktop.
- [x] Prueba HTTP de ceros, ranking vacío y rechazo por rol no autorizado.
- [x] Probar agregados con cobros ficticios, límites temporales, top 10 y snapshot histórico renombrado.
- [x] Comparar reporte REST con cobros confirmados por servicios reales de Caja en el smoke temporal.
- [ ] Validar UI visualmente y documentar explícitamente que impuestos = 0/no fiscal.
- [x] La regresión completa de 163 API y 267 Desktop pasó; incluye comprobaciones focalizadas de reporte y reservas.
