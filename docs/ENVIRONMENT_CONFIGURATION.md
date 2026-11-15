# Configuración de entornos

## ASP.NET Core

La configuración usa el proveedor nativo de ASP.NET Core. `appsettings.json` tiene solo valores no sensibles y campos vacíos de ejemplo; `appsettings.Development.json` solo ajusta logging. El archivo de producción no contiene secretos. Se decidió PostgreSQL local para desarrollo y Neon (PostgreSQL administrado) como destino previsto. La integración de persistencia aún no está implementada. La cadena de conexión se guardará en User Secrets localmente, no en `appsettings.json`.

El proyecto API incluye `UserSecretsId`. Para añadir un secreto local solo cuando la dependencia correspondiente esté implementada:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena-de-desarrollo-local>" --project src/TECNIHUB.API
dotnet user-secrets set "PushNotifications:Vapid:PrivateKey" "<clave-generada-localmente>" --project src/TECNIHUB.API
dotnet user-secrets set "SendGrid:ApiKey" "<api-key-de-sendgrid>" --project src/TECNIHUB.API
dotnet user-secrets set "ImgBB:ApiKey" "<api-key-de-imgbb>" --project src/TECNIHUB.API
```

No ejecutes estos ejemplos con marcadores textuales como si fueran credenciales válidas. User Secrets es para desarrollo; no es un almacén cifrado de producción. En despliegue se inyectarán como variables protegidas del entorno: `ConnectionStrings__DefaultConnection`, `SendGrid__ApiKey` e `ImgBB__ApiKey`. Si el backend se publica en Azure App Service, se configuran en Application Settings/Connection Strings; los secretos nunca se colocan en el frontend. No se han agregado credenciales ni se ha conectado Neon.

| Clave | Tipo | Uso actual |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Privada | PostgreSQL local / Neon previsto; no consumida hasta implementar persistencia |
| `SendGrid:ApiKey` | Secreto | Pendiente de integración; User Secrets local / variable protegida en despliegue |
| `ImgBB:ApiKey` | Secreto | Pendiente de integración; User Secrets local / variable protegida en despliegue |
| `PushNotifications:Vapid:Subject` | Pública | No utilizada |
| `PushNotifications:Vapid:PublicKey` | Pública | No utilizada |
| `PushNotifications:Vapid:PrivateKey` | Secreto | Solo User Secrets local / almacén seguro de producción; no se crea ni consume todavía |

Cuando se incorpore una integración, sus opciones deben estar tipadas, registradas en DI y validadas al iniciar. No se deben escribir valores sensibles en logs, errores ni archivos versionados.
