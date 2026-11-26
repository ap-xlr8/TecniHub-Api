# Contrato API — TECNIHUB

La API expone los endpoints de autenticación, perfiles, catálogo postal SEPOMEX, carga de imágenes, solicitudes de reparación, itinerario, refacciones y chat interno. En `Development`, el documento OpenAPI está disponible en `/openapi/v1.json`.

## Convenciones de seguridad y sesión

- **Access Token (JWT Bearer):** vigencia de 15 minutos, mantenido en memoria en el frontend y enviado en `Authorization: Bearer <token>`.
- **Refresh Token:** vigencia de **7 días**, entregado exclusivamente en cookie `HttpOnly; SameSite=Strict; Path=/api/auth` (`tecnihub_rt`, con `Secure` en HTTPS/producción), guardado como hash SHA-256 con rotación y detección de reutilización.
- **Privacidad de domicilio:** en tablones y detalles de solicitudes abiertas (`Open`) los campos `street`, `exteriorNumber`, `interiorNumber` y `addressReference` nunca se devuelven en el JSON (`fullAddress: null`). Solo se revelan al cliente propietario y al técnico asignado tras confirmar la asignación.
- **Imágenes (`ImgBB`):** 0 a 5 imágenes opcionales por solicitud o refacción. El backend valida firma real (magic bytes JPEG/PNG/WebP) y tamaño máximo de 5 MB antes de subir a ImgBB; PostgreSQL almacena únicamente la URL pública y fechas UTC.
- **Auditoría (`audit_logs`):** toda operación persistente se registra con fecha/hora UTC (`occurred_at`), actor, acción, entidad y resultado, sin guardar contraseñas, tokens ni contenido de mensajes del chat.

## Endpoints de Autenticación y Cuenta

| Método | Ruta | Autorización | Descripción |
|---|---|---|---|
| `GET` | `/health` | Pública | Estado de salud del API y PostgreSQL |
| `POST` | `/api/auth/register/client` | Pública (`AuthRateLimit`) | Registro de cuenta `Client` (`201`, `400`, `409`) |
| `POST` | `/api/auth/register/technician` | Pública (`AuthRateLimit`) | Registro de cuenta `Technician` (`201`, `400`, `409`) |
| `POST` | `/api/auth/verify-email` | Pública (`AuthRateLimit`) | Valida código de 6 dígitos y activa la cuenta (`200`, `400`, `429`) |
| `POST` | `/api/auth/resend-verification` | Pública (`AuthRateLimit`) | Reenvía código respetando cooldown/límite (`200`, `400`, `503`) |
| `POST` | `/api/auth/login` | Pública (`AuthRateLimit`) | Autenticación y emisión de JWT + cookie refresh (`200`, `400`, `401`, `403`) |
| `POST` | `/api/auth/refresh` | Cookie `tecnihub_rt` | Renovación transparente con rotación (`200`, `401`) |
| `POST` | `/api/auth/logout` | Cookie / Opcional JWT | Revoca refresh token y limpia cookie (`200`) |
| `POST` | `/api/auth/password-reset/request` | Pública (`AuthRateLimit`) | Solicitud genérica sin enumeración (`200`, `400`, `503`) |
| `POST` | `/api/auth/password-reset/confirm` | Pública (`AuthRateLimit`) | Confirma código y actualiza contraseña (`200`, `400`, `429`) |
| `GET` | `/api/auth/me` | JWT | Resumen de la cuenta autenticada (`200`, `401`) |
| `DELETE` | `/api/auth/account` | JWT | Inicia eliminación gradual (`PendingDeletion`), desactiva publicaciones abiertas y revoca sesiones (`200`, `401`) |

## Endpoints Operativos

| Método | Ruta | Autorización | Descripción |
|---|---|---|---|
| `GET` | `/api/profile/me` | JWT | Consulta del perfil propio (`200`, `401`, `404`) |
| `PUT` | `/api/profile/me` | JWT | Edición del perfil propio sin modificar rol, correo ni contraseña (`200`, `400`, `401`) |
| `GET` | `/api/postal-codes/{postalCode}` | JWT | Consulta del catálogo SEPOMEX por CP de 5 dígitos (`200`, `400`) |
| `POST` | `/api/media/images` | JWT (`MediaUploadRateLimit`) | Valida magic bytes (JPEG/PNG/WebP ≤ 5 MB) y aloja en ImgBB (`201`, `400`, `429`, `503`) |
| `POST` | `/api/repairs` | `ClientOnly` | Publica solicitud de reparación con domicilio y hasta 5 URLs (`201`, `400`) |
| `GET` | `/api/repairs/mine` | `ClientOnly` | Lista solicitudes del cliente autenticado (`200`) |
| `GET` | `/api/repairs/board` | `TechnicianOnly` | Tablón de solicitudes disponibles (`Open`), ubicación solo hasta colonia (`200`) |
| `GET` | `/api/repairs/itinerary` | `TechnicianOnly` | Itinerario cronológico privado del técnico con indicador de traslape (`200`) |
| `GET` | `/api/repairs/{id}` | JWT (dueño o técnico autorizado) | Detalle de reparación con control estricto de domicilio (`200`, `403`, `404`) |
| `PUT` | `/api/repairs/{id}/images` | `ClientOnly` (dueño) | Actualiza o reemplaza las URLs de imágenes de la solicitud (`200`, `400`, `403`, `409`) |
| `POST` | `/api/repairs/{id}/accept` | `TechnicianOnly` | Aceptación atómica y creación de conversación privada (`200`, `409`) |
| `PUT` | `/api/repairs/{id}/schedule` | `TechnicianOnly` (asignado) | Programa fecha/hora y reporta traslapes ±60 min (`200`, `400`, `403`, `409`) |
| `POST` | `/api/repairs/{id}/start` | `TechnicianOnly` (asignado) | Cambia estado a `En proceso` (`200`, `403`, `409`) |
| `POST` | `/api/repairs/{id}/request-closure` | `TechnicianOnly` (asignado) | Solicita confirmación de cierre al cliente (`200`, `400`, `403`, `409`) |
| `POST` | `/api/repairs/{id}/confirm-closure` | `ClientOnly` (dueño) | Confirma finalización y cambia a `Completado` (`200`, `403`, `409`) |
| `POST` | `/api/repairs/{id}/cancel` | `ClientOnly` (dueño) | Cancela solicitud mientras siga `Open` (`200`, `403`, `409`) |
| `GET` | `/api/spare-parts` | `TechnicianOnly` | Tablón de refacciones disponibles (`200`) |
| `GET` | `/api/spare-parts/mine` | `TechnicianOnly` | Lista publicaciones propias del técnico (`200`) |
| `GET` | `/api/spare-parts/{id}` | `TechnicianOnly` | Detalle de refacción y medios de contacto externo (`200`, `404`) |
| `POST` | `/api/spare-parts` | `TechnicianOnly` | Publica refacción con precio obligatorio en MXN (`201`, `400`) |
| `PUT` | `/api/spare-parts/{id}` | `TechnicianOnly` (dueño) | Actualiza datos o fotos de una refacción propia (`200`, `400`, `403`, `404`) |
| `PATCH` | `/api/spare-parts/{id}/availability` | `TechnicianOnly` (dueño) | Cambia disponibilidad activa (`200`, `403`, `404`) |
| `GET` | `/api/chat/conversations` | JWT | Bandeja de conversaciones autorizadas del usuario (`200`) |
| `GET` | `/api/chat/unread-count` | JWT | Contador de mensajes no leídos (`200`) |
| `POST` | `/api/chat/conversations/repair/{repairId}` | JWT (dueño o técnico asignado) | Abre/obtiene chat vinculado a una reparación asignada (`200`, `403`, `404`) |
| `POST` | `/api/chat/conversations/spare-part/{sparePartId}` | `TechnicianOnly` | Abre/obtiene chat vinculado a una refacción disponible (`200`, `400`, `404`, `409`) |
| `GET` | `/api/chat/conversations/{id}/messages` | JWT (participante) | Historial y polling incremental (`?after=<utc>`), marca recibidos como leídos (`200`, `403`, `404`) |
| `POST` | `/api/chat/conversations/{id}/messages` | JWT (participante, `ChatRateLimit`) | Envía mensaje de texto plano (1–1000 chars) (`201`, `400`, `403`, `409`, `429`) |
| `GET` | `/api/profiles/technicians/{technicianId}` | JWT | Perfil público del técnico y reseñas verificadas (`200`, `404`) |
| `GET` | `/api/profiles/clients/{clientId}` | `TechnicianOnly` (con asignación confirmada) | Perfil público limitado del cliente sin PII privada ni calificación (`200`, `403`, `404`) |
| `GET` | `/api/repairs/{id}/review` | JWT (dueño o técnico asignado) | Consulta reseña opcional asociada a una reparación (`200`, `204`, `403`, `404`) |
| `POST` | `/api/repairs/{id}/review` | `ClientOnly` (dueño, `Completed`) | Registra reseña opcional única (`1..5`) para un servicio completado (`201`, `400`, `403`, `409`) |
