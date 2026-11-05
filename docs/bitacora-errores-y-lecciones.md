# Bitácora de errores y lecciones — El Carbonazo

## Para qué sirve

Esta bitácora conserva errores observados durante el desarrollo, su impacto y controles concretos para reducir la probabilidad de repetirlos. Es una **ayuda de trabajo y revisión**, no garantiza ausencia de errores ni reemplaza las especificaciones, el código, las pruebas o la validación del usuario.

Antes de modificar una parte del proyecto, revisar las entradas relacionadas. Cuando se confirme un error nuevo, agregarlo o actualizar una entrada existente después de investigar su causa. Distinguir siempre **hecho comprobado**, **causa confirmada** y **hipótesis**; si falta evidencia, indicarlo. No incluir contraseñas, tokens, cadenas de conexión, datos personales ni información sensible en ejemplos o trazas.

Las reglas de `docs/constitution.md`, `docs/domain.md` y las especificaciones aprobadas en `specs/` prevalecen sobre esta bitácora. Las propuestas futuras pertenecen a `docs/evolucion-futura-por-modulos.md`; una lección no autoriza introducir funcionalidad sin especificación.

### Formato para nuevas entradas

Copiar este formato y completar solo lo que se haya verificado:

```text
### ERR-NNN — Título breve
- Estado: investigando | corregido | pendiente de validación | cerrado | pendiente
- Contexto y síntoma: qué ocurrió y en qué condiciones.
- Causa: confirmada o hipótesis, indicando la evidencia disponible.
- Corrección: qué se cambió o qué falta decidir.
- Prevención: comprobación concreta para trabajos futuros.
- Evidencia: archivos, prueba, comando o validación; señalar lo que no se verificó.
```

## Interfaz y experiencia

### ERR-001 — Hover de tarjetas anunciado sin comprobación visual
- **Estado:** corregido; comportamiento confirmado visualmente por el usuario.
- **Contexto y síntoma:** al pasar el cursor sobre una tarjeta de mesa no se observaba el efecto esperado, aunque el XAML compilaba.
- **Causa:** el selector aplicado inicialmente dependía de una relación con la plantilla del botón que no produjo el resultado visual esperado. La compilación no demostraba que el estilo alcanzara la tarjeta.
- **Corrección:** se aplicó el estado visual al borde de la tarjeta y se definió una plantilla del botón de mesa para evitar el fondo de hover no deseado.
- **Prevención:** al cambiar estilos Avalonia/SukiUI comprobar selectores, prioridad, plantillas y estados reales; validar hover, selección y foco en ejecución cuando corresponda. No declarar una animación comprobada solo porque compila.
- **Evidencia:** `src/ElCarbonazo.Desktop/Views/MesasView.axaml`; el usuario confirmó que la animación funciona. Ver `docs/validacion-t5.md`.

### ERR-002 — Texto de botones ilegible sobre fondo oscuro
- **Estado:** corregido; nuevos colores confirmados por el usuario.
- **Contexto y síntoma:** en el mapa oscuro, el texto negro de varios botones tenía contraste insuficiente.
- **Causa:** la apariencia heredada de controles no coincidía con las superficies oscuras del mapa; no basta con definir colores de fondo si la plantilla o tema aplica otro color al contenido.
- **Corrección:** se definió un tema para botones del mapa con colores para estados normal, hover, foco y deshabilitado, manteniendo la dirección «Carbón & Fuego».
- **Prevención:** comprobar texto, fondo y estados interactivos en ejecución; calcular contraste de las combinaciones reales y mantener distinción visible entre habilitado y deshabilitado. No asumir que un tema global claro funciona sobre paneles oscuros.
- **Evidencia:** `src/ElCarbonazo.Desktop/Views/MesasView.axaml`; comprobación visual reportada por el usuario. El checklist visual exhaustivo sigue pendiente para producción.

### ERR-003 — Confirmación de liberación poco visible
- **Estado:** corregido; la lógica de cancelar está cubierta por prueba; la revisión visual futura debe conservarse en la aceptación.
- **Contexto y síntoma:** el usuario no identificó la opción de cancelar al intentar verificar la liberación de una mesa. En la captura presentada estaba seleccionada una mesa `Libre`, por lo que `Liberar` estaba deshabilitado; la captura por sí sola no demuestra una causa única del problema con una mesa ocupada.
- **Corrección:** la confirmación de liberación se trasladó a un cuadro central con acciones explícitas de cancelar y confirmar.
- **Prevención:** probar la confirmación con estados elegibles y no elegibles, a tamaño mínimo y con desplazamiento; verificar que cancelar no envíe la mutación y que los mensajes no se confundan con una acción ya ejecutada.
- **Evidencia:** `Views/MesasView.axaml`; `ReleaseRequiresConfirmationAndUsesCurrentVersion` en `MesasPollingLifecycleTests.cs`.

## Sesión, transporte y concurrencia

### ERR-004 — Carrera cuando refresh termina inmediatamente
- **Estado:** corregido y cubierto por regresión automatizada.
- **Contexto y síntoma:** una prueba de refresh rechazado produjo `NullReferenceException` en vez de informar sesión expirada.
- **Causa:** una tarea de refresh podía completarse sincrónicamente y liberar el campo compartido antes de que el consumidor guardara una referencia local.
- **Corrección:** se conserva la tarea en una variable local antes de iniciar la limpieza del campo compartido.
- **Prevención:** probar tareas que completan tanto sincrónicamente como de forma diferida, además de consumidores concurrentes, logout y expiración. No asumir que un método asíncrono siempre cede el control.
- **Evidencia:** `Services/SessionManager.cs`; `FailedRefreshThrowsInsteadOfReturningDisposedResponse` y `ConcurrentHumanInteractionsShareOneRefresh` en pruebas Desktop.

### ERR-005 — GET y mutación podían estar simultáneamente en curso
- **Estado:** corregido y cubierto por pruebas focalizadas.
- **Contexto y síntoma:** se descartaba la respuesta de una lectura antigua, pero podía iniciarse una escritura antes de que el GET anterior terminara.
- **Causa:** invalidar una generación evita aplicar una respuesta obsoleta, pero no cancela ni espera por sí mismo la solicitud física.
- **Corrección:** el coordinador reserva la mutación, invalida la lectura y espera a que termine antes de enviar; si falla la lectura o cambia la sesión durante la espera, no envía el cambio.
- **Prevención:** distinguir consistencia de resultados y ausencia de solapamiento; probar GET lento, fallo, cambio de sesión y cancelación, sin perder el clic ni comparar versiones como relojes.
- **Evidencia:** `Services/MesasRequestCoordinator.cs`; `OldReadFinishesBeforeMutationAndCannotApply` y `MutationWaitsForActivePollingWithoutApplyingItsStaleResult`.

### ERR-006 — Plazo de lectura no incluía deserialización
- **Estado:** corregido para la lectura de mesas y probado con una lectura bloqueada.
- **Contexto y síntoma:** el timeout del transporte cubría la recepción del cuerpo, pero su alcance terminaba antes del procesamiento JSON en el servicio.
- **Corrección:** el GET de mesas usa un token con plazo que abarca solicitud y deserialización; se verifica la cancelación después de procesar.
- **Prevención:** definir el alcance completo del timeout y probar envío, cuerpo y procesamiento. Reconocer que la cancelación es cooperativa y no prometer una interrupción inmediata de trabajo que no observe el token.
- **Evidencia:** `Services/MesasApiService.cs`; `ReadDeadlineCoversJsonBodyProcessing`.

### ERR-007 — Un rechazo de cierre de grupo parecía éxito
- **Estado:** corregido y cubierto por prueba.
- **Contexto y síntoma:** ante una cuenta pendiente, el mensaje de rechazo podía ser sustituido por «Grupo cerrado».
- **Causa:** el código de aplicación de resultado trataba el cierre como exitoso sin verificar que la operación hubiera terminado correctamente.
- **Corrección:** se aplica el mensaje de éxito únicamente cuando la operación realmente fue exitosa; el rechazo mantiene su mensaje y solicita reconciliación.
- **Prevención:** probar explícitamente éxito, 4xx, conflicto, 5xx, timeout y fallo de transporte; no ejecutar lógica de éxito desde `finally` sin una señal de resultado válido.
- **Evidencia:** `ViewModels/MesasViewModel.Grupos.cs`; `GroupFailureIsNotRetriedAndRequiresReconciliation`.

## Ejecución, validación y medición

### ERR-008 — API y Desktop apuntaban a puertos distintos
- **Estado:** causa identificada y ejecución corregida con el perfil adecuado.
- **Contexto y síntoma:** el login informó que no podía conectar con la API. La API se había iniciado con el perfil HTTP por defecto mientras Desktop apuntaba al perfil HTTPS local.
- **Corrección:** iniciar la API con el perfil que coincida con la configuración utilizada por Desktop; no desactivar TLS para ocultar el problema.
- **Prevención:** al dar instrucciones de ejecución, comprobar `launchSettings.json`, dirección configurada y entorno; distinguir red, credenciales, configuración y errores TLS. Usar overrides de Development solo en pruebas locales.
- **Evidencia:** `src/ElCarbonazo.Api/Properties/launchSettings.json`, `Services/DesktopConfigurationService.cs` y la comprobación de conexión realizada durante la conversación.

### ERR-009 — Confundir duración del PUT con convergencia visual
- **Estado:** aclarado y documentado; medición cronometrada de extremo a extremo pendiente para producción.
- **Contexto y síntoma:** los 47 valores mostrados por el anfitrión correspondían a la duración de PUT, no al tiempo que la tarjeta tardaba en renderizarse.
- **Causa:** el instrumento solo marcaba la confirmación HTTP; no registraba el instante de presentación en la pantalla observadora.
- **Corrección:** se documentaron por separado los tiempos de PUT, la cadencia local medida y la estimación visual del usuario de 1–1,5 s, junto con sus límites.
- **Prevención:** describir exactamente inicio, fin, reloj, entorno y resolución de cada medición. No convertir una estimación visual o un tiempo de red en una garantía de render ni en un SLA; esperar a observar cada muestra cuando se evalúen estados sucesivos.
- **Evidencia:** `docs/validacion-t5.md` y el modo `--cadencia`/`--medir` del anfitrión de aceptación.

### ERR-010 — Herramienta temporal de medición terminó ante 401
- **Estado:** corregido en la herramienta; no implica un cambio de reglas de autenticación de la aplicación.
- **Contexto y síntoma:** durante la medición, una respuesta 401 provocó una excepción no controlada y cerró el anfitrión temporal.
- **Causa:** se asumía que la sesión de la herramienta permanecería autorizada durante toda la medición. El 401 observado no identifica por sí solo si la sesión había expirado o sido revocada.
- **Corrección:** la herramienta informa el código de autenticación y solicita una nueva acción humana para iniciar sesión; no repite automáticamente una escritura. Los 5xx y fallos de transporte tras enviar se informan como resultado potencialmente incierto.
- **Prevención:** probar expiración, revocación, 401/403, errores de servidor y cancelación también en herramientas auxiliares. Mantener aislada la base temporal y no mostrar tokens en consola.
- **Evidencia:** `tests/ElCarbonazo.AcceptanceHost/Program.cs`; el usuario confirmó que el modo de medición funcionó después del ajuste.

## Revisión al cerrar una tarea

### ERR-011 — Barra de ventana clara y maximizar sin confirmar
- **Actualización:** el usuario confirmó que el botón central no responde con clic real, aunque el doble clic en la barra sí funciona. La prueba previa con mensajes de ventana no representaba esa interacción. Se reemplazó el botón de SukiUI por uno propio, evitando su interceptación especial de hit test para Snap Layout; el menú Snap Layout al pasar el puntero no está disponible en el reemplazo. La causa exacta en el equipo del usuario y la corrección con clic físico siguen pendientes de confirmación.
- **Corrección posterior:** el reemplazo inicialmente carecía del rol `User` de decoración de ventana; SukiUI podía interpretar la pulsación como arrastre de la barra. Se asignó dicho rol y se verificó en una ventana de prueba mediante eventos de ratón del sistema que el botón maximiza y restaura. Falta la confirmación del usuario en su configuración.
- **Estado:** ajustado; captura de la ventana propia y prueba local de maximizar/restaurar realizadas, pendiente revisión del usuario en su configuración y escalado.
- **Contexto y síntoma:** el usuario observó una barra blanca demasiado ancha y reportó que maximizar/restaurar no funcionaba. Una primera configuración de tema oscuro seguía mostrando una barra clara con iconos de bajo contraste.
- **Causa:** la barra usa la plantilla y el fondo de `GlassCard` de SukiUI; cambiar solo el tema de la ventana no garantizó un fondo oscuro. No se ha establecido una causa única del fallo original de maximizar.
- **Corrección:** adaptar el fondo de la barra mediante el recurso y plantilla instalados, reducir el tamaño de sus iconos y mantener las decoraciones y capacidades de ventana activas. Se comprobó localmente una transición a maximizada y de vuelta a normal en una ventana de prueba.
- **Prevención:** revisar la plantilla de la versión realmente instalada, comprobar contraste y tamaño en una captura de la ventana propia, y probar los controles de ventana en ejecución. No afirmar compatibilidad con otros equipos o escalas sin probarlos.
- **Evidencia:** `src/ElCarbonazo.Desktop/MainWindow.axaml`; la comprobación local no sustituye la aceptación visual del usuario.

- ¿Se consultaron la especificación y las entradas relacionadas antes de editar?
- ¿Se agregó o ejecutó una prueba que reproduce el fallo cuando es viable?
- ¿Se verificó el comportamiento real cuando una prueba automatizada no cubre la interfaz o el entorno?
- ¿Se conservaron los datos y cambios ajenos, usando bases temporales para pruebas destructivas?
- ¿La respuesta distingue claramente implementado, probado, observado por el usuario y pendiente?
- ¿Se debe registrar una nueva lección, indicando evidencia y sin exponer secretos?
