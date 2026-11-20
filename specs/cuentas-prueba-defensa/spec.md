# Cuentas de prueba para defensa local

El usuario autorizó sustituir el nombre y contraseña de la Jefa `admin` existente por `jefa` y crear cuentas `cocina` y `mesero`, con las contraseñas acordadas en la conversación. Solo para la base local `elcarbonazo` de la presentación; no es administración general de usuarios ni habilita uso comercial.

Conservar el ID y correo de la Jefa para preservar referencias históricas y autorización superior. No cambiar catálogo, pedidos, cobros ni diarios. Las contraseñas se guardan únicamente como hashes BCrypt; nunca se escriben en el repositorio, argumentos, auditoría o salida. Se requiere comprobación exacta de destino y usuario, bloqueo ante conflictos y transacción con auditoría.
