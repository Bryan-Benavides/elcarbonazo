# Plan técnico 007 — Reporte sencillo de ventas

**Estado: aprobado para implementación y pruebas temporales; no introduce mutaciones ni migraciones.**

1. Agregar endpoint GET de reporte con fechas UTC requeridas y máximo de rango definido; consultar exclusivamente cobros confirmados y snapshots históricos, con `AsNoTracking` y agregación en SQL cuando el proveedor traduzca la consulta.
2. No usar catálogo actual para nombre/precio histórico; revisar qué campos conserva efectivamente `ComprobanteCobroLinea` antes de fijar query.
3. Devolver resumen agregado y máximo 10 filas de ranking; validar overflow/decimal y rango vacío. `ImpuestosRegistrados` debe provenir de `cobro.impuestos`, actualmente cero.
4. Aplicar rol Jefa/Supervisor en API y volver a validar sesión. No exponer referencias externas ni datos personales.
5. Desktop: vista de reporte con filtros `desde/hasta`, métricas compactas y tabla; errores en español, sin navegación a destinos no implementados.
6. Pruebas PostgreSQL temporales para agregados, límites, snapshots históricos, sin cobros, permisos y SQL traducible; Desktop para filtros, errores, sesión y resultados.
7. No cambiar RF fiscales de 004 ni representar el reporte como contabilidad/fiscal.
