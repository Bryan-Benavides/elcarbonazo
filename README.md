# El Carbonazo — POS Restaurant (MVP)

## Stack
- .NET 10
- Backend: ASP.NET Core Web API + EF Core + Npgsql (PostgreSQL)
- Frontend: Avalonia UI 12.1.4 + SukiUI 7.0.1 + CommunityToolkit.Mvvm 8.4.0

## Requisitos
- .NET 10 SDK
- PostgreSQL 15+

## Ejecutar
```bash
dotnet build ElCarbonazo.sln
dotnet run --project src/ElCarbonazo.Api
dotnet run --project src/ElCarbonazo.Desktop
```

## Arquitectura
- `src/ElCarbonazo.Api` — API REST + EF Core (solo BD)
- `src/ElCarbonazo.Desktop` — Cliente Avalonia (solo HTTP a API)
- `docs/` — Dominio, Constitución
- `specs/` — Especificaciones por módulo (SDD)