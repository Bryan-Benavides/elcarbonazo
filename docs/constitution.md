# Constitución del Proyecto — Bar Restaurante "El Carbonazo"

> **Perfil universitario vigente:** se busca un prototipo funcional demostrable, no una instalación lista para producción. Los requisitos de despliegue seguro, aceptación LAN física y endurecimiento avanzado se conservan como objetivos para una etapa posterior. Esta aclaración no elimina SDD, MVVM, aislamiento REST, validación TLS ni las reglas de negocio.

## 1. Principios Innegociables
- **Desarrollo Guiado por Especificaciones (SDD):** Ningún código se escribe sin una especificación aprobada previamente en `spec/`.
- **Estabilidad de Dependencias:** Prohibido actualizar o instalar paquetes NuGet con versiones no probadas o incompatibles.
- **Operación en Red Local (Offline First):** Toda la lógica de negocio y comunicación opera en LAN local sin depender de conexión a Internet[cite: 1].

## 2. Stack Tecnológico Cerrado
- **Framework:** .NET 10.
- **Frontend Desktop:** Avalonia UI 12.1.4 + SukiUI 7.0.1 + CommunityToolkit.Mvvm[cite: 3].
- **Backend API:** ASP.NET Core Web API[cite: 1].
- **Acceso a Datos:** Entity Framework Core + Npgsql (PostgreSQL)[cite: 1].

## 3. Reglas Arquitectónicas Inquebrantables
- **Aislamiento del Cliente:** La capa `ElCarbonazo.Desktop` solo interactúa con el sistema vía HTTP/REST hacia la API[cite: 3].
- **Prohibición de Acceso Directo:** El cliente Desktop tiene **estrictamente prohibido** referenciar `Microsoft.EntityFrameworkCore`, `Npgsql` o conectarse directamente a la base de datos[cite: 3].
- **Patrón MVVM Estricto:** Toda vista XAML en `Views/` debe tener su ViewModel correspondiente en `ViewModels/`. Los archivos `.axaml.cs` solo contienen `InitializeComponent()`[cite: 3].

## 4. Design System y Paleta Oficial
- **Tema Base:** SukiUI 7.0.1[cite: 3].
- **Recursos de Color:**
    - Fondo principal `#1E1E1E`; fondo secundario `#2D2D30`; bordes `#3F3F46`.
    - Texto principal `#F3F4F6`; texto secundario `#9CA3AF`.
    - Acento primario `#FF5722`; acento alterno `#E65100`.
     - Libre `#10B981` | Ocupada `#EF4444` | Reservada `#F59E0B` | Agrupada `#06B6D4`.
     - Variantes luminosas para indicadores del mapa autorizadas por el usuario: Libre `#27E7A0`, Ocupada `#FF5B66`, Reservada `#FFBF36`, Agrupada `#32D9F4`; conservan la misma semántica y no sustituyen el texto.
    - Dirección visual aprobada: tema oscuro «Carbón & Fuego», con navegación lateral inspirada en la referencia del usuario. Ver `docs/diseno-visual-propuesta.md`; la aplicación actual aún requiere adaptación.
    - Los colores semánticos deben acompañarse de texto y contraste legible; no se exige texto blanco sobre fondos brillantes.

## 5. Reglas de Dominio
- **Mesas:** El salón cuenta con 15 mesas físicas numeradas[cite: 1, 2]. Queda prohibido liberar una mesa con una cuenta pendiente[cite: 1].
- **Seguridad:** Las contraseñas se almacenan mediante algoritmos Hash con Sal[cite: 1]. Cierre de sesión tras 15 minutos de inactividad[cite: 1].
