# Constitución del Proyecto — Bar Restaurante "El Carbonazo"

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
    - `ColorVino`: `#8C2331` (Botón primario, acento de marca, headers)[cite: 3].
    - `ColorCarbón`: `#2A2421` (Fondo de inputs, texto denso, bordes)[cite: 3].
    - `ColorHueso`: `#F8F5F0` (Fondo general de la aplicación)[cite: 3].
    - `ColorBeige`: `#E8E0D5` (Paneles secundarios y resúmenes)[cite: 3].
    - `ColorMesaLibre`: `#7CB342` | `ColorMesaOcupada`: `#8C2331` | `ColorMesaReservada`: `#E67E22` | `ColorMesaAgrupada`: `#2E86AB`[cite: 3].

## 5. Reglas de Dominio
- **Mesas:** El salón cuenta con 15 mesas físicas numeradas[cite: 1, 2]. Queda prohibido liberar una mesa con una cuenta pendiente[cite: 1].
- **Seguridad:** Las contraseñas se almacenan mediante algoritmos Hash con Sal[cite: 1]. Cierre de sesión tras 15 minutos de inactividad[cite: 1].