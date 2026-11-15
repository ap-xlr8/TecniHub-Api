# Contrato API

La API se encuentra vacía intencionalmente: no hay recursos de negocio, autenticación, versionamiento ni modelo de datos definidos en esta fase. El endpoint técnico `GET /health` devuelve el estado básico del proceso (todavía sin dependencias externas). El esquema OpenAPI generado por ASP.NET Core está disponible como `/openapi/v1.json` en Development. En otros ambientes no se publica el documento por defecto.

Cada endpoint se agregará cuando exista una historia aprobada, con método/ruta, esquema de entrada y respuesta, códigos de error, validaciones y requisitos de autorización documentados. No se deben inventar contratos a partir de los nombres de las carpetas o de un ejemplo del template.
