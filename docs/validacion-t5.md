# Validación T5 — mapa de mesas

Estado: **aceptada para el perfil universitario**, con sincronización funcional verificada, cadencia medida localmente y convergencia estimada visualmente por el usuario. La medición cronometrada de render y la prueba física LAN siguen pendientes antes de producción. Este registro no convierte el prototipo en apto para producción.

## Evidencia automatizada

El 4 de noviembre de 2026 se ejecutaron las pruebas existentes contra una base PostgreSQL temporal aislada y pruebas Desktop con servicios simulados. La API verificó, entre otros, el contrato HTTP de 15 mesas, cambios de estado y grupos. Estas pruebas no demuestran el comportamiento visual ni sustituyen una prueba Desktop conectada a la API.

La suite Desktop incluye una comprobación de cadencia saludable próxima a 2 segundos con un servicio controlado y una comprobación de ausencia de solapamiento con lectura lenta. No constituye una medición LAN real ni incluye render.

El 5 de noviembre de 2026, `--cadencia` ejecutó el ciclo real de `MesasViewModel` y `MesasApiService` contra la API y PostgreSQL temporales en el mismo equipo, usando HTTP loopback. Se recibieron seis GET satisfactorios: intervalos entre inicios 2012,7; 2013,7; 2007,4; 2013,0 y 2015,2 ms; duraciones de GET y deserialización 37,4; 10,1; 7,7; 9,0; 14,9 y 6,6 ms. Máximo de GET simultáneos: uno. El despacho fue inline, la sesión simulada y no hubo ventana ni render. No representa una medición sobre una segunda terminal o red física LAN.

Contraste calculado para texto y fondos del sistema visual (luminancia relativa WCAG): `#F3F4F6` sobre `#1E1E1E` 15,15:1 y sobre `#2D2D30` 12,48:1; `#9CA3AF` sobre esos fondos 6,57:1 y 5,41:1, respectivamente. No sustituye revisar estados interactivos, escalado y render reales.

## Comprobaciones reportadas por el usuario

El usuario informó que funcionan la desconexión y reconexión de la API, el cierre de sesión y los nuevos colores de botones. Reportó la actualización entre sesiones como probable, sin una medición de tiempo confirmada. No dispone de usuarios de otros roles ni pedidos pendientes para aceptación visual; no se han creado datos manuales. Las pruebas de API usan una base temporal aislada y no sustituyen esa aceptación visual.

Posteriormente, el usuario verificó visualmente con el anfitrión temporal que Cocina puede consultar sin habilitar acciones de mesa o agrupación, y que Jefa no puede liberar la mesa ni cerrar el grupo preparados con pedidos pendientes. No implica que toda mesa ocupada o todo grupo tenga una cuenta pendiente.

**Sincronización funcional verificada por el usuario:** en el entorno temporal, la mesa 04 observada en Desktop cambia entre `Libre` y `Ocupada` al solicitar los cambios desde la consola de medición. El usuario informó además la estimación visual indicada más abajo; no se recibió grabación ni cronometraje de las muestras.

El usuario facilitó posteriormente 47 registros de confirmación de PUT del anfitrión local: duración mínima 9 ms, máxima 53 ms, media 15,91 ms y mediana 13 ms. Se trata exclusivamente de la duración HTTP del PUT medida por el anfitrión; no incluye la espera del siguiente polling, duración del GET ni render. No se recibieron tiempos cronometrados de presentación ni grabación. Algunas solicitudes se hicieron con menos de 2 segundos de separación y pueden superar la frecuencia de muestreo; no se puede inferir que cada estado intermedio haya sido mostrado.

**Estimación visual del usuario:** en esa prueba local, el cambio de la tarjeta parecía tardar aproximadamente entre 1 y 1,5 segundos después del aviso `API CONFIRMÓ`. Es una observación manual aproximada, sin cronometraje ni registro de muestras individuales; no permite calcular mínimos, máximos o percentiles fiables. La consola se dibuja después de recibir la respuesta y su propio retardo no está medido. La duración del GET y la cadencia se midieron por separado en la ejecución local descrita arriba, no durante esta observación visual. No es un límite garantizado ni una medición en una segunda terminal LAN.

Para una prueba visual futura de esos casos se necesita una instalación de pruebas separada con datos controlados; no se deben modificar los datos existentes ni añadir administración de usuarios o pedidos ad hoc al MVP. El mecanismo para preparar y retirar esa instalación debe acordarse antes de ejecutarlo.

### Anfitrión temporal local

Desde PowerShell en la raíz, ejecute `dotnet run --project tests/ElCarbonazo.AcceptanceHost`. Utiliza PostgreSQL local y las credenciales administrativas de pruebas configuradas por `ELCARBONAZO_TEST_ADMIN_CONNECTION` o por Development solo para crear una base nueva `elcarbonazo_test_<id>`; no usa como destino la base de desarrollo. El servidor escucha solo en `127.0.0.1`, con puerto aleatorio y HTTP exclusivamente de pruebas. La consola muestra las variables de entorno para lanzar Desktop en otra terminal y las credenciales temporales. No cambie `desktop.json`: se sigue necesitando su UUID de instalación válido.

La mesa 1 se prepara ocupada con pedido pendiente; las mesas 2 y 3 forman un grupo con pedido pendiente. Use las demás mesas para cambios y mediciones. Las cuentas Jefa y Cocina solo existen en la base temporal; use ambas en ventanas separadas. El anfitrión hace logout de su sesión de preparación antes de entregar el control.

Al terminar cierre las ventanas Desktop de prueba y pulse Ctrl+C en el anfitrión; este detiene su API y elimina únicamente la base que creó. Un cierre forzado, un fallo eléctrico o un error de limpieza puede dejar el proceso o la base temporal; no elimine bases de datos sin identificar inequívocamente su origen. Los logs temporales se conservan para diagnóstico. Las credenciales y HTTP de este entorno nunca se usan en producción.

Para comprobar solo arranque, preparación y limpieza automáticos puede ejecutarse `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --smoke`; esta modalidad no deja tiempo para conectar Desktop. No demuestra aceptación visual ni mide render.

Para repetir la medición local de seis GET sin ventana puede ejecutarse `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --cadencia`. Este modo crea y limpia su propia base temporal y no mide presentación en pantalla.

## Aceptación pendiente con la aplicación

Usar una instalación de pruebas aislada, nunca datos reales de operación. Registrar entorno, fecha y resultado:

- [ ] Autenticarse y comprobar las 15 mesas, números, capacidades, estados y colores con texto legible.
- [ ] Para los roles autorizados, probar reserva, ocupación y liberación con cancelación y confirmación; verificar rechazo por cuenta pendiente mediante datos de prueba controlados.
- [ ] Comprobar que Personal de Cocina solo puede consultar.
- [ ] Seleccionar mesas libres, crear un grupo, comprobar sus miembros y cerrar con confirmación; verificar el rechazo ante cuenta compartida pendiente.
- [ ] Comprobar conflicto entre dos sesiones de prueba, desconexión y recuperación, y los avisos de sesión pertinentes sin repetir escrituras.
- [ ] Revisar panel de detalle, acciones y confirmaciones a tamaño mínimo 800×560 y a un tamaño mayor; verificar desplazamiento, foco y contraste. El efecto al pasar el mouse sobre tarjetas ya fue confirmado por el usuario.
- [ ] Cerrar sesión y comprobar retorno a login.

## Medición de convergencia pendiente

Para una medición local aproximada, inicie el anfitrión con `dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --medir`, abra Desktop como Cocina con la dirección temporal indicada y coloque la consola y la tarjeta de la mesa 04 visibles simultáneamente. La consola utiliza Jefa: no inicie Jefa en otra ventana porque una nueva sesión revoca la anterior. Con la consola enfocada, pulse Espacio una vez para iniciar la sesión de medición; espere el aviso de sesión lista. Grabe la pantalla con tasa de cuadros conocida y pulse Espacio para cada muestra; espere a ver el cambio en Desktop antes de pulsar de nuevo. Tome al menos cinco muestras y conserve los fotogramas que muestran tanto `API CONFIRMÓ` como el primer cambio visible. La consola alterna `Libre` y `Ocupada` con una sesión de prueba Jefa y no imprime tokens.

La diferencia entre esos fotogramas aproxima la convergencia; indique la resolución de la grabación (por ejemplo, ± un cuadro) y que el marcador se muestra **después** de recibir la respuesta, con retardo de consola/render no medido. Por ello no es una medición exacta desde el instante interno del commit: si la tarjeta cambia antes del marcador, registre la muestra y no la trate como negativa ni como cero. El marcador incluye la duración del PUT, que no es la latencia del GET. Si la sesión de medición expira o es revocada, el programa informa el código recibido; el siguiente Espacio inicia sesión de nuevo y otro Espacio inicia una muestra. Ninguna escritura rechazada o incierta se reintenta automáticamente. No afirmar que se midió la duración del GET o la cadencia si no se recopilaron por separado.

Con API y Desktop saludables en el entorno de prueba, realizar cambios desde una segunda sesión y registrar el instante en que la API confirma cada cambio y el instante en que la tarjeta de la primera sesión lo muestra renderizado. Usar una referencia de tiempo común o una grabación con cronómetro visible; anotar resolución del método, número de muestras, mínimo, máximo y condiciones (equipo, red y versión). Registrar también duración del GET y espera cuando estén disponibles. No confundir recepción HTTP o actualización del ViewModel con render en pantalla.

Comprobar la cadencia nominal de inicio de GET de 2 segundos sin solapamiento y documentar cualquier desviación. El objetivo nominal incluye espera de polling, duración del GET y despacho/render; **no es una garantía absoluta de 2 segundos**. Solo se dispone de la estimación visual local indicada arriba, no de una medición cronometrada de extremo a extremo.
