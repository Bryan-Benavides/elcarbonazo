# Plan — cuentas de prueba

Utilizar un procedimiento explícito restringido a Development y PostgreSQL local, con confirmación exacta de base y destino. Rechazar usuarios inesperados, nombres o correos en conflicto y sesiones activas; conservar ID y correo de la Jefa. Generar hashes mediante el componente BCrypt del sistema. Aplicar cambios y auditoría en una única transacción, sin registrar secretos. No reintentar automáticamente si el resultado es incierto.

Antes de aplicarlo a `elcarbonazo`, probarlo en una base PostgreSQL temporal, incluyendo rechazo por conflictos y conservación de referencias. Luego verificar inicio de sesión y roles sin mostrar contraseñas ni tokens. No borrar ni modificar el catálogo ni el historial.
