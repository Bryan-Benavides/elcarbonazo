# Validación D4 — pedidos de salón

## Estado

**D4 aceptada por el usuario para el alcance Desktop de salón, no para operación real.** El usuario confirmó que verificó satisfactoriamente los puntos 1, 2, 3, 5, 6 y 7 del checklist: conexión/catálogo, borrador, confirmación/envío, cuenta heredada de solo lectura, botón deshabilitado e interacción/layout. La captura posterior muestra catálogo cargado, cuenta 3 abierta, dos comandas abiertas y total C$ 490.00. Verifica la representación de cuenta y comandas, pero no muestra una comanda `Lista`.

El checklist inicial pedía comprobar la transición a `Lista` desde Cocina. Se corrigió esa instrucción: la vista Desktop de Cocina corresponde a D5 y aún no está implementada. No era un paso ejecutable en D4. D3 sí implementa la API de Cocina y el smoke automatizado recorrió una transición a `Lista`, pero eso no equivale a revisión visual Desktop. La comprobación visual del aviso `Lista` en salón queda para la integración D5/D6.

Se ajustó el diagnóstico para distinguir problemas de autorización, ruta/destino, servidor o datos inválidos, sin revelar excepciones ni credenciales. El usuario confirmó también la apariencia/funcionalidad del botón deshabilitado y la interacción/layout en su ejecución.

## Verificación local aislada

Las pruebas de ViewModel incluyen la intercalación en que un envío espera una lectura de cuenta y esta revela una cuenta heredada: el envío queda bloqueado y conserva el borrador. También comprueban que una lectura completada tras cerrar la vista no presenta sus datos. El 9 de noviembre de 2026 pasaron formato, compilación, 50 pruebas API y 127 Desktop, y la auditoría de paquetes no reportó vulnerabilidades. Estas pruebas no verifican la apariencia ni la interacción visual.

`dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --pedidos-smoke` crea una base propia `elcarbonazo_test_<uuid>`, aplica migraciones únicamente en ella, carga el catálogo ficticio y ejecuta clientes REST de Desktop contra la API local. Comprueba cuenta individual y de grupo, envío/repetición idempotente, importes y transición de cocina hasta `Lista`. Usa una sesión adaptada y no crea ventana ni mide render; una interrupción forzada podría dejar recursos temporales que deben identificarse antes de limpiar. No es validación física LAN.

La ejecución local del 9 de noviembre de 2026 completó el recorrido y la limpieza. EF Core emitió dos avisos `MultipleCollectionIncludeWarning` durante consultas: indican posible coste de una consulta con varias colecciones, no un fallo de la prueba. No se ha medido su impacto ni identificado aquí cada consulta; revisar antes de asumir rendimiento para volúmenes altos y no cambiar a consultas divididas sin preservar consistencia.

Para la revisión visual con datos aislados, ejecutar desde la raíz:

```powershell
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --pedidos
```

El anfitrión muestra su URL HTTP loopback, las variables de Development y credenciales temporales; la instalación Desktop aún necesita su `idInstalacion` válido. Abrir otra terminal con las variables que muestra el anfitrión y lanzar Desktop. Mesa 04 comienza libre y usa el catálogo ficticio; mesa 01 y el grupo 02/03 son cuentas heredadas de solo lectura. Al terminar cerrar las ventanas de prueba y detener el anfitrión con Ctrl+C; este elimina únicamente la base que creó. No ejecutar este recorrido sobre datos reales ni usar HTTP de prueba en producción.

## Pendiente fuera de D4: D5/D6

- [ ] En D5/D6, operar una comanda desde KDS Desktop hasta `Lista` y comprobar en salón el estado agregado/aviso. No afirmar observación visual antes de ejecutar esta integración.
- [ ] En D5, validar KDS con teclado/táctil, foco, FIFO, notas, estados vacíos, reconexión y concurrencia según su especificación.
- [x] D4 de salón aceptada por confirmación del usuario para los puntos 1, 2, 3, 5, 6 y 7; la captura verifica cuenta con comandas abiertas. No incluye UI KDS ni aceptación para operación real.
