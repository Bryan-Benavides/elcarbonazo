# Especificación del Módulo 001: Autenticación y Control de Mesas (MVP)

## Contexto
Este módulo establece los cimientos de seguridad y la visualización interactiva del salón del restaurante[cite: 2].

## Requisitos Funcionales (Notación EARS)

### RF-01: Autenticación de Usuarios
- **CUANDO** un empleado ingresa su nombre de usuario y contraseña en la vista `LoginView`, **EL SISTEMA DEBE** autenticar las credenciales contra la API mediante validación Hash con Sal y retornar un Token JWT válido[cite: 1, 2].
- **SI** las credenciales son incorrectas, **EL SISTEMA DEBE** mostrar un banner de error con la clase `BannerError` en la interfaz sin revelar detalles específicos de la falla[cite: 3].

### RF-02: Consulta de Estado de Mesas
- **MIENTRAS** el usuario tenga un rol autorizado (Jefa, Encargado de Caja, Mesero, Personal de Cocina, Supervisor), **EL SISTEMA DEBE** renderizar la cuadrícula interactiva de 15 mesas en `MesasView` con sus colores correspondientes según el estado (`Libre`, `Ocupada`, `Reservada`, `Agrupada`)[cite: 1, 2, 3].
- **CUANDO** el estado de una mesa cambie en la base de datos, **EL SISTEMA DEBE** actualizar la visualización en la interfaz Desktop en un intervalo no mayor a 2 segundos[cite: 1].

### Criterios de Finalización
- Inicio de sesión funcional consumiendo el endpoint `POST /api/auth/login`.
- Mapa de 15 mesas renderizado con botones de 100x100px y las 4 sillas decorativas en las esquinas según el Design System[cite: 3].