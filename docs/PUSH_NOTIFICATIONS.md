# Notificaciones Push

La arquitectura aprobada es una única infraestructura dentro del backend, sin microservicio ni broker dedicado. En este scaffold no hay suscripciones, persistencia, endpoints ni envíos.

En una HU posterior se evaluarán persistencia y proveedor SQL; contratos para registro/expiración de dispositivos, preferencias, historial/lectura, destinatarios y reintentos; VAPID con clave pública en Angular y clave privada exclusivamente en backend; separación de destinatarios administrativos y funcionales; autenticación, autorización y validación de webhooks de GitHub. Los eventos de negocio y políticas de retención siguen pendientes.
