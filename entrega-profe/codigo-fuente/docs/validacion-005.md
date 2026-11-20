# Validación observada — 005 Inventario y escandallo

## Persistencia y API (C1–C2)

Las pruebas usan bases PostgreSQL temporales `elcarbonazo_test_<uuid>`. La migración se comprobó con historial representativo de 003/004, sin fabricar consumos, y su reversión automática queda bloqueada. No se ha aplicado a una base persistente de demostración por estas pruebas.

Se verificaron validaciones y permisos HTTP esenciales, movimientos y auditoría, agregación decimal, rechazo por stock o receta, rollback ante fallo de auditoría y reproducción del mismo envío sin duplicar consumo. Dos envíos compitieron por el último stock; una prueba simultánea no demuestra todas las intercalaciones. Pruebas con bloqueo observado en PostgreSQL cubrieron ambos órdenes de reemplazo de receta/envío y creación de receta/inactivación; otra comprobó inactivación esperando a envío y rechazo de versión obsoleta.

Pasaron formato, build, 141 pruebas API, 232 Desktop y auditoría de paquetes sin vulnerabilidades reportadas en los orígenes consultados. `AcceptanceHost --pedidos-smoke` y `--caja-smoke` pasaron con recetas y existencias ficticias en bases temporales. El segundo verificó consumo por envío y ausencia de un segundo descuento al cobrar. Usa REST y ViewModel con sesión/diario adaptados: no acredita render, ACL reales ni LAN.

## Desktop (C3) y límites pendientes

Pedidos interpreta el rechazo de stock y muestra faltantes solo si sus datos son coherentes. La vista incluye búsqueda paginada de productos, filtro de ingredientes, kardex y formularios de Jefa. Pruebas con fakes cubren paginación, filtros obsoletos, errores de lectura, versiones, cambios de sesión y bloqueo de escrituras inciertas. La secuencia de formato, build, 141 pruebas API, 262 Desktop y auditoría de paquetes pasó; sin vulnerabilidades reportadas en los orígenes consultados.

`AcceptanceHost --inventario-smoke` ejerció creación, entrada, edición, kardex y guardado de receta mediante ViewModel, REST y PostgreSQL temporal, con sesión adaptada; no abrió ventana ni probó render o teclado. Una inspección separada abrió la ventana real como Jefa contra API temporal y capturó la sección inicial de ingredientes a 1116×728 píxeles exteriores: se observaron búsqueda, filtro, controles de consulta y estados textuales. El agente no revisó integralmente los formularios, foco, teclado, escalado, desconexión ni contraste compuesto. No se afirma una matriz exhaustiva de fallos, recuperación durable administrativa, aptitud de producción o uso con dinero real.

## C4 — Regresión y aceptación local

Se repitió la secuencia formato → build → API → Desktop → auditoría: pasaron 141 pruebas API y 262 Desktop, sin vulnerabilidades reportadas por los orígenes consultados. El anfitrión se formateó y compiló aparte; `--inventario-smoke` y `--caja-smoke` pasaron en bases temporales. El recorrido integrado de mesa y grupo verificó pedidos, Cocina, cobro, comprobante, liberación/cierre explícito y ausencia de segundo descuento. Los smoke usan sesión adaptada y no acreditan render, ACL, LAN ni revisión humana.

El usuario confirmó: «las revisiones [...] están bien» y comunicará mejoras futuras. No detalló qué escenarios, dispositivos o condiciones revisó; su aceptación cierra C4 para el perfil universitario local, sin acreditar por separado las verificaciones técnicas no documentadas.

Para revisar sin alterar la base persistente de demostración, desde la raíz en una terminal PowerShell:

```powershell
dotnet run --project tests/ElCarbonazo.AcceptanceHost -- --pedidos
```

Mantener esa terminal abierta. El anfitrión crea una base temporal con catálogo, recetas y stock ficticios y muestra la URL loopback; en otra terminal ejecutar exactamente las instrucciones de entorno que muestra y `dotnet run --project src/ElCarbonazo.Desktop`. Usar la cuenta ficticia que muestra el anfitrión. Las cuentas iniciales de mesa 1 y grupo 2/3 son heredadas de solo lectura; para un pedido nuevo usar mesa 4. `desktop.json` sigue siendo necesario. Al terminar, cerrar Desktop y detener el anfitrión con Ctrl+C, que limpia su base temporal.

Como guía para revisiones futuras: comprobar ingredientes y alertas, búsqueda de productos, receta, entrada/ajuste ficticio y kardex; después recorrer pedido → Cocina → Caja y comprobar los mensajes de faltantes. Una operación administrativa incierta mantiene el bloqueo durante la sesión; no borrar diarios ni interpretar un GET como prueba de terminación.
