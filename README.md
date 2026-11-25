# TECNIHUB Backend

API REST base para TECNIHUB en ASP.NET Core sobre .NET 10 LTS. La solución está preparada para comenzar el desarrollo, sin autenticación ni funcionalidades de negocio.

## Requisitos

- .NET SDK 10.0.400 o posterior dentro de la banda 10.0; la versión se selecciona mediante `global.json`.
- Windows, Linux o macOS compatibles con .NET 10.

## Restaurar, compilar y probar

Desde esta carpeta:

```powershell
dotnet restore TECNIHUB.sln
dotnet format TECNIHUB.sln --verify-no-changes --no-restore
dotnet build TECNIHUB.sln --configuration Release --no-restore
dotnet test TECNIHUB.sln --configuration Release --no-build
```

Ejecuta la API con `dotnet run --project src/TECNIHUB.API`. Los puertos HTTP/HTTPS están definidos en `src/TECNIHUB.API/Properties/launchSettings.json`. En desarrollo, OpenAPI está disponible en `/openapi/v1.json`.

`GET /health` es un endpoint técnico de disponibilidad del proceso; todavía no comprueba dependencias externas.

## Secretos locales

El proyecto API ya tiene un `UserSecretsId`. La base elegida es PostgreSQL local, con Neon como servicio previsto; persistencia y servicios aún deben implementarse. No se han almacenado valores secretos. Agrega tus credenciales locales con User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena-postgresql-local-o-neon>" --project src/TECNIHUB.API
dotnet user-secrets set "Jwt:SigningKey" "<clave-secreta-minimo-32-bytes>" --project src/TECNIHUB.API
dotnet user-secrets set "Smtp:Username" "<correo-remitente-smtp>" --project src/TECNIHUB.API
dotnet user-secrets set "Smtp:Password" "<app-password-smtp>" --project src/TECNIHUB.API
dotnet user-secrets set "ImgBB:ApiKey" "<api-key-de-imgbb>" --project src/TECNIHUB.API
```

Los marcadores anteriores son ejemplos; reemplázalos por valores reales localmente y nunca los subas al repositorio. Consulta `docs/ENVIRONMENT_CONFIGURATION.md`.

## Estructura

- `Domain`: núcleo sin referencias externas.
- `Application`: depende solo de Domain.
- `Infrastructure`: depende de Application y Domain; todavía no incorpora proveedor de persistencia.
- `API`: entrada HTTP y composición del host.
- `UnitTests` e `IntegrationTests`: pruebas xUnit.

Consulta `docs/ARCHITECTURE.md` y `docs/DEVELOPMENT_GUIDE.md`.
