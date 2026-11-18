# Validación observada — 006 Auditoría, cancelaciones y seguridad

## C2 — recuperación parcial no expuesta

`AnulacionRecuperacionService` y `AnulacionOperacionLock` implementan una base interna para consulta y barrera por UUID; no hay endpoint conectado. PostgreSQL temporal verificó creación y repetición de barrera, dos solicitudes concurrentes, rechazo de sesión revocada, conservación de resultado completado y rollback al fallar auditoría (3 pruebas focalizadas). La autorización y ejecución futuras deben adquirir el mismo advisory lock; aún no existen y no se ha probado la carrera de ejecución frente a barrera ni la intervención ajena. No se ha demostrado recuperación integral ni se ha habilitado cancelación de negocio.

Regresión posterior: formato y build correctos, 157 pruebas API y 262 Desktop aprobadas, y auditoría NuGet sin vulnerabilidades reportadas.

## C2 — autorización superior y rutas KDS, avance no integral

Se añadió el endpoint autenticado `POST /api/seguridad/autorizar-anulacion`: vuelve a validar sesión y rol, verifica contraseña de Jefa activa (incluida Jefa solicitante), crea una intención durable `Autorizada` con hash canónico y devuelve un secreto aleatorio solo una vez; PostgreSQL conserva solo SHA-256, sesión, autorizadora y vencimiento ≤120 s. Los fallos genéricos actualizan el contador separado por solicitante y HMAC del correo y registran auditoría sin guardar correo/credencial. El endpoint usa la barrera HMAC y no crea una nueva sesión. Una prueba temporal comprobó emisión, vínculo de intención y no almacenamiento del secreto en claro.

Se agregaron `GET /api/cocina/alertas-cancelacion` y `POST /api/cocina/anulaciones/{id}/acuse`, con paginación por ID y acuse serializado/idempotente. No hay aún una ruta de negocio que cree anulaciones/alertas, ni prueba focalizada de estas consultas/acuse. El cursor es actualmente un ID numérico, no el cursor opaco definido en el contrato.

**No habilita cancelaciones:** ejecución no implementada. El servicio de recuperación no está expuesto, y el futuro endpoint de ejecución debe compartir el advisory lock por operación, consumir el token único dentro de la transacción, barrer/revalidar roles y estado, y establecer resultado terminal frente a recuperación. Siguen pendientes umbral/expiración/TTL/cardinalidad de intentos, consumo concurrente, anulación y reversión acumulada, adaptación Caja, auditoría paginada y las carreras indicadas en el plan. Evidencia previa de regresión: formato/build correctos, 157 API y 262 Desktop; auditoría NuGet sin vulnerabilidades reportadas. No se han ejecutado todavía las regresiones posteriores a estos últimos cambios.

**Regresión ejecutada después de esos cambios:** `dotnet format ... --verify-no-changes` y build correctos; 158 pruebas API y 262 Desktop aprobadas; auditoría NuGet sin vulnerabilidades reportadas. Esta regresión no sustituye las pruebas de comportamiento pendientes listadas arriba.

## C2 — ejecución y compatibilidad, avance parcial posterior

Se implementó el endpoint de ejecución de anulación y se conectó con autorización, `HmacAutorizacionGuard`, operación durable y recuperación bajo el mismo lock por UUID. La operación bloquea el protocolo global de prepare de Caja antes de recursos, serializa destino/pedido/comandas/detalles/ingredientes, rechaza pago/cobro pendiente, comprueba que cada detalle tenga snapshot y que el total por ingrediente coincida con los movimientos originales, y valida que las reversiones anteriores más las propuestas no superen esos snapshots. Cambia solo líneas activas completas, revierte stock con movimientos append-only positivos, recalcula la cuenta usando precios históricos, emite una alerta KDS por evento y persiste resultado/auditoría dentro de la transacción protegida. Los rechazos de dominio deterministas quedan terminales tras rollback a savepoint. El GET por UUID y la barrera explícita ya tienen rutas; la barrera no ejecuta negocio.

Una prueba PostgreSQL temporal cubre el flujo completo (autorización de Jefa, anulación completa, reversión exacta, pedido/comanda cancelados, alerta KDS, acuse individual idempotente, consulta de historial, rollback íntegro ante fallo de auditoría y repetición terminal sin duplicar stock). Otra prueba cubre bloqueo tras cinco fallos y respuesta genérica de intento sexto, sin guardar el correo/credencial en auditoría. Se corrigieron en el proceso el `xmin` omitido en una consulta `FOR UPDATE`, la disposición accidental de la transacción propiedad del guardia y la comparación del JSONB reproducido como JSON semántico. Estos hallazgos quedan reflejados por sus pruebas; no se inspeccionaron datos sensibles.

Se adaptaron Caja y Cocina a líneas/comandas canceladas: Caja suma y genera comprobante con líneas activas, Cocina omite líneas canceladas y reagrupa pedidos considerando comandas activas; envío impide abrir otra cuenta en un grupo que conserva un pedido cancelado. La ruta KDS de acuse es idempotente bajo lock y la consulta lista solo alertas pendientes con cursores opacos. La prueba e2e del servicio confirma consulta y doble acuse idempotente de una alerta. Aún falta una prueba de cobro/comprobante con una cuenta parcialmente anulada y la compatibilidad de huella con operaciones v1 previamente preparadas.

**Limitaciones que mantienen C2 abierto:** ya se probaron anulaciones de línea consecutivas en la misma comanda: la primera deja la comanda activa y solo devuelve su porción, la segunda devuelve el snapshot restante y cierra comanda/pedido. No se han probado carreras concurrentes ni intercalaciones deterministas frente a prepare/execute de Caja, transición KDS, envío o movimiento de inventario. Faltan token expirado/revocado/replay concurrente, duración efectiva del umbral de cinco minutos, TTL/cardinalidad/limpieza de pares y más cobertura de cursores/filtros de auditoría.

## C3 — integración parcial de alertas KDS

Desktop consulta las alertas paginadas con el cursor opaco del API y las presenta con «ANULADO — NO PREPARAR», motivo y comanda. El acuse usa `IAnulacionSessionSender` de emisión única, valida actor/generación de sesión, no renueva ni reintenta y solo retira la alerta de la colección tras respuesta válida. Timeout, respuesta inválida o error incierto conserva el bloqueo local de escritura y no se reintenta. Se amplió `CocinaApiServiceTests` para el GET de alertas y el acuse de una sola emisión; las pruebas Desktop pasaron con 263 casos.

Se añadió el almacenamiento independiente `AnulacionJournal`, que persiste únicamente instalación/estado/UUID/propietario, permite limpiar solo después de terminal y conciliación, y falla cerrado ante archivos ausentes, inválidos o no protegidos. El provisionador Windows es explícito y no se llama durante el arranque. Pruebas cubren persistencia, reinicio, operaciones exclusivas, no guardar payload/credenciales y no reparar un archivo faltante. Aún no está cableado al arranque ni a un coordinador; por ello no hay modal/cliente de autorización ni se envían anulaciones desde Desktop. Tampoco se hizo revisión visual en ventana real. Faltan pruebas ViewModel específicas para alertas/cambio de sesión/acuse incierto.

Regresión después de estos cambios: formato y build correctos; **159 pruebas API y 265 Desktop** aprobadas; auditoría NuGet sin vulnerabilidades reportadas. `git diff --check` no reporta errores de whitespace; Git avisa solo de la normalización LF/CRLF esperada en archivos modificados. No se aplicaron migraciones a bases persistentes ni se hizo aceptación visual, LAN o de producción.

## C2 — avance parcial: snapshots de envío

`EnviosService` registra el consumo por detalle e ingrediente en la misma transacción que el envío y el movimiento agregado. Una prueba en PostgreSQL temporal comprobó dos líneas repetidas, cantidades separadas y replay sin duplicados; las pruebas existentes de carrera entre reemplazo de receta y envío comprueban ahora también el snapshot en ambos órdenes. El fallo de auditoría deja cero snapshots. Pasaron las 9 pruebas focalizadas `InventarioEnviosTests`.

Esto no implementa ni valida anulaciones, reversión acumulada, autorización superior, operación durable de 006, adaptación de Caja o alertas KDS. C2 permanece abierto; no se fabricaron snapshots históricos ni se aplicaron migraciones a bases persistentes.

Regresión posterior: formato y build correctos, 154 pruebas API y 262 Desktop aprobadas; auditoría NuGet sin vulnerabilidades reportadas. La primera ejecución API detectó un snapshot duplicado en el fixture de grants, que se adaptó y se volvió a probar. Esa inserción del fixture ahora encuentra el snapshot existente y usa `ON CONFLICT DO NOTHING`: comprueba permisos para la sentencia y el rechazo de duplicados, pero no demuestra por sí sola una nueva inserción efectiva con el rol runtime.

## C1 en curso

Se generó la migración EF aditiva `AddAuditoriaCancelacionesSeguridad` sobre 005 con `Down` bloqueado y un primer modelo para snapshots de consumo por línea, anulación, autorización, operación durable, alertas y límites; se ampliaron checks de movimiento y grants. No hay aún servicios ni interfaz de cancelación, y C1 no se considera completado.

En PostgreSQL temporal se comprobó una migración desde 005 con pedido pagado, comanda, detalle, consumo, turno, cobro y línea de comprobante, conservando importes e IDs representativos y sin fabricar snapshots ni anulaciones; también se comprobó el bloqueo de `Down`. Pruebas adicionales cubren algunos CHECK de operación, límite, consumo, acuse, hash y vigencia de autorización, FK de sesión y hash duplicado.

La prueba de grants consultó privilegios y, con rol temporal no propietario, insertó snapshots, anulación, alerta y movimiento positivo ficticios, actualizó las columnas permitidas y comprobó unicidad de snapshot y de reversión por anulación/ingrediente. Se rechazaron UPDATE/DELETE de evidencia y auditoría por falta de permisos. Este ejercicio SQL prueba persistencia/permisos, **no** autoriza anular una cuenta pagada ni demuestra el servicio de negocio; tampoco demuestra que diferentes anulaciones no superen el consumo original.

Al añadir columnas de 006, dos pruebas históricas que poblaban esquemas anteriores con el modelo EF actual fallaron por columnas inexistentes. Se adaptaron a un modelo histórico explícito antes de migrar; no se cambiaron migraciones anteriores. Tras ampliar pruebas pasaron formato, compilación, **144 pruebas API y 262 Desktop**; la auditoría NuGet no reportó vulnerabilidades en los orígenes consultados.

La revisión de `RefreshTokenCleanupService` muestra que limpia filas de `token_refresh_sesion`, no elimina `sesion_usuario`. La FK RESTRICT de autorización impide borrar una sesión referenciada; se verificó ese rechazo en PostgreSQL temporal. No se ha probado una política futura de borrado de sesiones ni implementado provisión/rotación de HMAC; cualquier limpieza futura debe respetar la retención de autorizaciones.

Esta evaluación inicial dejó pendientes escenarios adicionales de FK, índices, límites, coherencia entre pedido/comanda/detalle y reversión acumulada. El bloque posterior, descrito abajo, añadió FKs compuestas; los tests de C2 aún deben demostrar control acumulado bajo locks, atomicidad, autorización y concurrencia. También falta completar el aprovisionamiento administrativo y la integración HMAC antes de implementar el límite superior. No se afirma C1 completo, seguridad operativa, LAN ni aptitud de producción. No se aplicaron migraciones a bases persistentes.

## Bloque posterior de C1

La inspección detectó que el CHECK inicial aceptaba resultados terminales con JSON no objeto o status incompatibles. Se reforzaron el modelo y la migración 006 en desarrollo: objeto JSON, código no vacío, fecha terminal no anterior a creación y rangos HTTP por estado; también se rechazan controles en el motivo. Triggers impiden reescribir operaciones terminales, deshacer el consumo de autorización o modificar un acuse ya registrado. Pruebas PostgreSQL temporales cubrieron esos rechazos, incluyendo acuse bajo rol runtime; la API deberá implementar acuse idempotente sin intentar una segunda actualización.

El usuario aprobó que un cambio no coordinado de clave HMAC provoque espera durable de 24 horas para nuevas autorizaciones, sin afectar login. `plan.md §3.1` registra la política y la necesidad de serialización entre instancias; el mecanismo, aprovisionamiento inicial, tratamiento de metadatos ausentes y pruebas de rotación aún **no están implementados**. La suite repetida pasó formato, build, 144 API y 262 Desktop, y NuGet no reportó vulnerabilidades en los orígenes consultados. No se ha ejecutado un recorrido funcional 006 ni se han aplicado migraciones persistentes.

## Comprobación adicional de precisión

Una prueba PostgreSQL confirmó que `'NaN'::numeric > 0` es verdadero. Se modificaron únicamente los CHECK de las nuevas columnas de cantidad de `consumo_detalle` e importe de `anulacion_pedido` y la migración 006 en desarrollo para excluir explícitamente ese valor; se comprobó el rechazo con referencias existentes. No se afirma que las columnas numéricas anteriores estén protegidas contra valores especiales. La regresión posterior volvió a pasar formato, build, 144 API, 262 Desktop y auditoría NuGet sin vulnerabilidades reportadas.

## Coherencia histórica y barrera HMAC

La migración incorpora FKs compuestas para anulación/comanda/pedido, anulación/detalle/comanda y reversión/anulación/destino; PostgreSQL temporal rechazó combinaciones con referencias individuales existentes pero inconsistentes. La prueba histórica añadió un grupo activo, una comanda sin consumo y una receta con cantidad distinta al consumo anterior; la migración preservó esos datos sin fabricar snapshots. Las FKs no garantizan que la suma de varias reversiones no supere el consumo: eso requiere la transacción y pruebas de C2.

Se agregó por SQL de migración un singleton de metadatos HMAC en estado no provisionado, sin clave. El rol runtime no puede insertar, borrar ni modificar el indicador de aprovisionamiento. `HmacAutorizacionGuard` utiliza lock de fila y transacción; un cambio de huella confirma la espera de 24 horas antes de devolver bloqueo y el trabajo permitido se ejecuta bajo el mismo lock. Pruebas con reloj controlado cubrieron falta de clave/provisión/metadatos, reinicio de contexto, cambios sucesivos, rollback y 24 horas; otra observó bloqueo entre dos conexiones PostgreSQL. No se probó un despliegue multihost ni se implementó aún una ruta de autorización, aprovisionamiento administrativo o recuperación de metadatos. C1 sigue abierto; no se creó ni modificó ninguna base persistente.

Se generó y revisó el SQL de 005 a 006 sin aplicarlo a una base persistente. La regresión posterior pasó formato, build, 146 pruebas API y 262 Desktop; auditoría NuGet sin vulnerabilidades reportadas.

## Primera provisión HMAC

`HmacAutorizacionProvisioner` implementa una primera provisión explícita mediante conexión administrativa, sin ruta HTTP ni ejecución automática. Rechaza clave corta, estado ausente, estado ya provisionado o evidencia previa de operaciones/autorizaciones/límites; huella y evento se confirman en la misma transacción sin guardar clave ni huella en auditoría. En PostgreSQL temporal se comprobaron una provisión ganadora entre dos concurrentes, rollback ante fallo de auditoría, rechazos y ausencia de permisos del rol runtime. La generación/provisión operativa de clave, comando de instalación, recuperación de metadatos y su integración real siguen pendientes; no se ha aprovisionado una base persistente.

La secuencia de formato, build, pruebas API y Desktop y auditoría NuGet pasó: **149 API, 262 Desktop**, sin vulnerabilidades reportadas por los orígenes consultados.

## Comando explícito de primera provisión

Se conectó `--provisionar-hmac-autorizacion` antes del seeder de Development. El handler consulta base/dirección/puerto efectivos y requiere confirmación textual exacta; rechaza una clave corta o igual a JWT, y no imprime ninguna de ellas. En PostgreSQL temporal se comprobaron rechazo de confirmación y clave coincidente, provisión y rechazo de repetición, mediante el handler con entrada/salida simuladas; no se ejecutó el proceso CLI completo ni sobre una base persistente. `docs/installation.md` documenta el procedimiento y exige autorización previa y clave aleatoria externa.

La recuperación de metadatos ausentes o dañados continúa sin implementarse: se bloquea la provisión y se conserva evidencia, sin reparación automática. Falta probar integración de la barrera con el flujo de autorización y el control de consumo acumulado en C2. La regresión repetida pasó formato, build, **150 API y 262 Desktop**, y auditoría NuGet sin vulnerabilidades reportadas; C1 permanece abierto.

Una comprobación posterior ejecutó el comando real como proceso separado contra PostgreSQL temporal, con configuración y datos ficticios: se confirmó la provisión, sin imprimir claves en stdout/stderr. No acredita una instalación persistente ni recuperación de metadatos. La nueva regresión pasó formato, build, **151 API y 262 Desktop**, y auditoría NuGet sin vulnerabilidades reportadas.

## Cierre universitario local de C1 — recuperación de fila HMAC ausente

El usuario aprobó la recuperación administrativa explícita de una fila HMAC ausente con espera de 24 horas, sin borrar operaciones ni contadores. Se implementaron `HmacAutorizacionRecovery` y `--recuperar-hmac-autorizacion`; la primera provisión, recuperación y guardia toman una misma barrera advisory antes de la fila, incluso si falta. Pruebas con PostgreSQL temporal verificaron rollback si falla auditoría, mantenimiento de contadores, espera hasta su vencimiento, una recuperación ganadora entre dos concurrentes, denegación al rol runtime y confirmación incorrecta/correcta del handler.

El comando solo admite fila ausente y no reemplaza una fila existente. No se probó el proceso CLI de recuperación por separado, ni restauración de otros daños o sincronización multihost. El almacenamiento C1 se da por completado para el perfil local; **no** demuestra autorización ni anulación funcionales. C2 conserva pendientes la integración de esta barrera, generación de snapshots en envíos, reversión acumulada y carreras con Caja. La regresión pasó formato, build, **153 API y 262 Desktop**, y auditoría NuGet sin vulnerabilidades reportadas. No se aplicaron migraciones ni recuperación a bases persistentes.

## Reanudación autorizada — flujo Desktop y recorrido real local

El usuario autorizó reanudar 006 y completar el flujo de anulación para la demostración universitaria local. Se conservó el trabajo previo y no se modificaron migraciones aplicadas.

**Desktop.** `AnulacionCoordinator` registra la intención en un diario local antes de emitir, captura la generación de sesión antes de escribir el diario y la exige en autorización, ejecución y barrera; un cambio de sesión falla cerrado sin reejecutar. `AnulacionApiService` usa `IAnulacionSessionSender` de emisión única (sin refresco ni reintento), y el ViewModel serializa polling, envío y anulación con el mismo semáforo, además de revalidar la fotografía de la cuenta antes de confirmar. El modal exige motivo de 5–250 caracteres, correo y contraseña de Jefa, limpia la contraseña en `finally` y no la escribe en el diario ni en la auditoría. Una operación incierta bloquea nuevas anulaciones y envíos, pero permite salir de la vista; la recuperación se limita a consultar o establecer barrera y nunca reemite la ejecución.

**Diario local.** `WindowsAnulacionJournalProvisioner` crea explícitamente `recovery\anulaciones\anulaciones-journal.json` y su lock con ACL restringida a la cuenta Windows actual, Administradores y Sistema. Una segunda ejecución no sobrescribe un diario existente, aunque conserve una operación pendiente. `--aprovisionar-diario-anulaciones` y `scripts/instalar-diario-anulaciones.ps1` ejecutan la primera instalación; no se invocan al arrancar y no reparan archivos dañados. En este equipo se provisionó el diario real en `%ProgramData%\ElCarbonazo\recovery\anulaciones` y se verificó el estado `SinPendientes`, el UUID de instalación y la ACL. La prueba automatizada del provisionador usa archivos temporales reales; la creación en `%ProgramData%` se comprobó manualmente. No hay instalador empaquetado.

**Contrato HTTP.** La consulta y la barrera devuelven HTTP 200 cuando la operación ya está resuelta, incluido un resultado de negocio rechazado, para que Desktop pueda conciliar sin dejar el diario atascado. Una barrera serializada con la ejecución devuelve el resultado vigente: si la ejecución ganó, se informa completada y no se afirma que no hubo efecto. `CuentasService` filtra líneas canceladas y comandas sin líneas activas, de modo que salón no muestra importes anulados.

**Evidencia ejecutada.** Se añadieron pruebas Desktop para el coordinador (éxito, rechazo determinista, incertidumbre, replay, barrera ganadora, cambio de sesión, diario ausente, intervención ajena y validaciones) y para el ViewModel (confirmación única, limpieza de contraseña, conciliación, barrera, cuenta cambiada, rechazo y rol sin permiso). La prueba API de ejecución comprueba además el filtrado de la cuenta de salón. El nuevo modo `--anulaciones-smoke` de `ElCarbonazo.AcceptanceHost` recorrió por REST contra PostgreSQL temporal: autorización superior, ejecución, replay terminal sin duplicar la reversión, consulta resuelta, cuenta totalmente anulada oculta del salón y barrera durable que impide una ejecución posterior. El smoke usa sesión y credenciales ficticias y **no** prueba render ni LAN.

**Regresión final:** `dotnet format --verify-no-changes` y build correctos; **165 pruebas API y 288 Desktop** aprobadas; auditoría NuGet sin vulnerabilidades reportadas en los orígenes consultados. El AcceptanceHost se compiló por separado y el smoke terminó sin excepciones.

**Límites que impiden declarar aceptación integral o producción:**

- No se probaron carreras reales ni intercalaciones deterministas con prepare/execute de Caja, transición KDS, envío o ajuste de inventario.
- No se cubrieron token expirado/revocado, replay concurrente del mismo token ni consumo simultáneo de autorizaciones.
- El umbral de cinco fallos/ cinco minutos, el límite de 100 pares HMAC, su TTL y su limpieza no se ejercitaron de extremo a extremo.
- No se verificó compatibilidad de huellas de Caja v1/v2 ni una cuenta parcialmente anulada que luego se cobre y genere comprobante.
- El diario protege esta instalación Windows; no es una barrera distribuida entre terminales ni promete recuperación tras reinstalar el equipo.
- No se midió render, latencia, foco, teclado, contraste o escalado en ventana real; tampoco se validó LAN física.
- No hay instalador empaquetado validado ni autorización para operar el restaurante real.
