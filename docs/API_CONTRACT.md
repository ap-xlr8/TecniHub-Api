# Contrato API — Sprint 1 (Altas y Seguridad)

La API expone los endpoints del **Sprint 1** (`HU-001` a `HU-004`) bajo `/api/auth`, además de los endpoints de verificación de rol y salud. En `Development`, el documento OpenAPI está disponible en `/openapi/v1.json`.

## Convenciones de seguridad y sesión

- **Access Token (JWT Bearer):** corta duración (15 minutos), se mantiene en memoria en el frontend y se envía en `Authorization: Bearer <token>`.
- **Refresh Token:** vigencia de **7 días**, se entrega exclusivamente en cookie `HttpOnly; SameSite=Strict; Path=/api/auth` (`tecnihub_rt`, con `Secure` en HTTPS/producción) y se almacena en PostgreSQL como hash SHA-256 con rotación y detección de reutilización.
- **Códigos de verificación y recuperación:** 6 dígitos generados criptográficamente, guardados como hash SHA-256, vigencia de **10 minutos**, máximo **5 intentos** y bloqueo de **1 hora** al agotarlos.
- **Política de contraseñas:** 8 a 16 caracteres, sin espacios, mínimo 1 mayúscula, 1 minúscula, 1 número y 1 carácter especial.
- **Auditoría:** cada acción relevante se registra en `audit_logs` con fecha/hora UTC (`occurred_at`), actor, acción, entidad y resultado, sin guardar contraseñas ni tokens.

## Endpoints

| Método | Ruta | Autorización | Descripción |
|---|---|---|---|
| `GET` | `/health` | Pública | Estado de salud del API y PostgreSQL |
| `POST` | `/api/auth/register/client` | Pública | `HU-001` Registro de cuenta con rol `Client` (`201`, `400`, `409`) |
| `POST` | `/api/auth/register/technician` | Pública | `HU-002` Registro de cuenta con rol `Technician` y perfil base (`201`, `400`, `409`) |
| `POST` | `/api/auth/verify-email` | Pública | Valida código de 6 dígitos y activa la cuenta (`200`, `400`, `429`) |
| `POST` | `/api/auth/resend-verification` | Pública | Reenvía código respetando bloqueo de 1 hora (`200`, `400`, `429`) |
| `POST` | `/api/auth/login` | Pública | `HU-003` Autenticación y emisión de JWT + cookie refresh (`200`, `400`, `401`, `403`) |
| `POST` | `/api/auth/refresh` | Cookie `tecnihub_rt` | Renovación transparente y rotación de sesión de 7 días (`200`, `401`) |
| `POST` | `/api/auth/logout` | Cookie / Opcional JWT | Revoca refresh token y limpia cookie (`200`) |
| `POST` | `/api/auth/password-reset/request` | Pública | `HU-004` Solicitud con respuesta genérica sin revelar existencia (`200`, `400`, `503`) |
| `POST` | `/api/auth/password-reset/confirm` | Pública | `HU-004` Valida código de un solo uso y actualiza contraseña (`200`, `400`, `429`) |
| `GET` | `/api/auth/me` | JWT | Devuelve el resumen del usuario autenticado (`200`, `401`) |
| `DELETE` | `/api/auth/account` | JWT | Inicia eliminación gradual (`PendingDeletion`) y revoca sesiones (`200`, `401`) |
| `GET` | `/api/client/summary` | JWT (`ClientOnly`) | Endpoint protegido exclusivo del rol `Client` (`200`, `401`, `403`) |
| `GET` | `/api/technician/summary` | JWT (`TechnicianOnly`) | Endpoint protegido exclusivo del rol `Technician` (`200`, `401`, `403`) |
