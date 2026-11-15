# Guía de desarrollo

## Herramientas

- .NET SDK 10.0.400, fijado por `global.json` con avance dentro de la banda de características 10.0.
- IDE opcional compatible con C#/.NET 10.

## Primer arranque

```powershell
dotnet restore TECNIHUB.sln
dotnet build TECNIHUB.sln
dotnet test TECNIHUB.sln
dotnet run --project src/TECNIHUB.API
```

Revisa la URL HTTPS y HTTP exacta en `src/TECNIHUB.API/Properties/launchSettings.json`. El certificado local se puede preparar con `dotnet dev-certs https --trust`. No se requiere Docker ni una base de datos en el scaffold.

## Cambios

Mantén reglas de negocio fuera de API e Infrastructure. Añade una HU solo cuando esté aprobada y acompaña cada cambio con pruebas y actualización del contrato/documentación que afecte.
