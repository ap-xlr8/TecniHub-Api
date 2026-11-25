# Configuración de entornos

## ASP.NET Core

La configuración usa el proveedor nativo de ASP.NET Core. `appsettings.json` tiene solo valores no sensibles y campos vacíos de ejemplo; `appsettings.Development.json` solo ajusta logging. El archivo de producción no contiene secretos. Se decidió PostgreSQL local para desarrollo y Neon (PostgreSQL administrado) como destino previsto. La integración de persistencia aún no está implementada. La cadena de conexión se guardará en User Secrets localmente, no en `appsettings.json`.

El proyecto API incluye `UserSecretsId`. Para añadir un secreto local solo cuando la dependencia correspondiente esté implementada:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena-de-desarrollo-local>" --project src/TECNIHUB.API
dotnet user-secrets set "Jwt:SigningKey" "<clave-secreta-minimo-32-bytes>" --project src/TECNIHUB.API
dotnet user-secrets set "Smtp:Username" "<correo-remitente-smtp>" --project src/TECNIHUB.API
dotnet user-secrets set "Smtp:Password" "<app-password-smtp>" --project src/TECNIHUB.API
dotnet user-secrets set "ImgBB:ApiKey" "<api-key-de-imgbb>" --project src/TECNIHUB.API
dotnet user-secrets set "PushNotifications:Vapid:PrivateKey" "<clave-generada-localmente>" --project src/TECNIHUB.API
```

No ejecutes estos ejemplos con marcadores textuales como si fueran credenciales válidas. User Secrets es para desarrollo; no es un almacén cifrado de producción. En despliegue se inyectarán como variables protegidas del entorno: `ConnectionStrings__DefaultConnection`, `Jwt__SigningKey`, `Smtp__Username`, `Smtp__Password` e `ImgBB__ApiKey`. Si el backend se publica en Azure App Service, se configuran en Application Settings/Connection Strings; los secretos nunca se colocan en el frontend.

| Clave | Tipo | Uso actual |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Privada | PostgreSQL local / Neon en despliegue |
| `Jwt:SigningKey` | Secreto | Firma HMAC-SHA256 de tokens JWT y códigos de verificación |
| `Smtp:Username` / `Smtp:Password` | Secreto | Envío de códigos de verificación y recuperación vía SMTP (Gmail App Password) |
| `ImgBB:ApiKey` | Secreto | Carga segura de imágenes desde backend (User Secrets local / variable protegida en despliegue) |
| `PushNotifications:Vapid:Subject` | Pública | No utilizada |
| `PushNotifications:Vapid:PublicKey` | Pública | No utilizada |
| `PushNotifications:Vapid:PrivateKey` | Secreto | Solo User Secrets local / almacén seguro de producción; no se crea ni consume todavía |

## Catálogo SEPOMEX (`postal_codes`)

El archivo oficial `CPdescarga.txt` de Correos de México (~15 MB) se mantiene fuera del repositorio Git y se importa a la tabla `postal_codes` mediante el script [import-sepomex.ps1](../scripts/import-sepomex.ps1):

```powershell
# Desarrollo local (contenedor Docker tecnihub-postgres-dev)
.\scripts\import-sepomex.ps1 -FilePath "..\CPdescargatxt\CPdescarga.txt"

# Despliegue (Neon PostgreSQL usando psql)
.\scripts\import-sepomex.ps1 -FilePath "..\CPdescargatxt\CPdescarga.txt" -PsqlConnectionString "<cadena-psql-neon>"
```

Cuando se incorpore una integración, sus opciones deben estar tipadas, registradas en DI y validadas al iniciar. No se deben escribir valores sensibles en logs, errores ni archivos versionados.
