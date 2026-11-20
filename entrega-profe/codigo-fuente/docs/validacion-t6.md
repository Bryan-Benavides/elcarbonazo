# Validación T6 — Desktop

Estado: **aceptada para el perfil universitario**. Este registro separa pruebas automatizadas y comprobaciones visuales; no afirma cobertura integral ni aptitud de producción.

## Resultado de verificación

Se ejecutaron formato, build sin errores ni advertencias, 9 pruebas API y 79 pruebas Desktop satisfactorias, y auditoría de vulnerabilidades sin hallazgos para los orígenes consultados. Desktop y sus pruebas no incluyen referencias a la API, EF Core ni Npgsql. La medición de cobertura integral no se ejecutó; el umbral ≥80 % queda pendiente para producción.

## Cobertura focalizada

- Login correcto y rechazado, errores de red y limpieza de contraseña.
- Refresh concurrente single-flight, respuesta tardía después de logout y refresh rechazado con respuesta inmediata.
- Actividad humana limitada, polling sin actividad, advertencia/expiración por inactividad y logout ante fallo de red.
- Reloj de servidor y zona de Managua; distinción de fallo TLS y red; bloqueo de sesión ante fallo TLS.
- Configuración de producción/desarrollo, HTTPS, identidad de instalación y override de desarrollo con archivos de prueba aislados.
- Polling, backoff, coordinación, conflictos, cuentas pendientes, incertidumbre, permisos y grupos mediante dobles controlados.
- Integración de API con PostgreSQL temporal aislado; se mantiene independiente de las pruebas Desktop.

## Revisión visual disponible y pendiente

El usuario informó haber probado login, mapa, acciones, agrupación, desconexión/reconexión, cierre de sesión, colores de botones y acceso de Cocina. Los detalles y limitaciones están en `docs/validacion-t5.md`.

No se ha documentado una revisión visual directa de los avisos de sesión/inactividad y del bloqueo TLS, ni una revisión completa del foco y navegación por teclado. Las pruebas automatizadas anteriores validan estados, pero no equivalen a inspeccionar su presentación. La cobertura cuantitativa ≥80 % y un checklist visual exhaustivo permanecen como objetivos previos a producción.
