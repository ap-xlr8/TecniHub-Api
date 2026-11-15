# DevSecOps

- CodeQL se prepara para C# en GitHub Actions. Los resultados solo estarán disponibles si GitHub permite Code Scanning para la visibilidad/plan del repositorio.
- NuGet Audit se ejecuta en restore; vulnerabilidades altas y críticas (NU1903/NU1904) se tratan como errores. Las bajas y moderadas se informan según los avisos del SDK para evaluación.
- Dependabot propone actualizaciones semanales, con grupos y límites por ecosistema; las versiones mayores se revisan en PR.
- Dependency Review se ejecuta en PR; las funciones que requieren GitHub Advanced Security/plan de pago deben confirmarse en la cuenta.
- Secret Scanning y Push Protection son ajustes de GitHub, no se pueden habilitar desde este repositorio local sin acceso remoto. En este momento no se ha publicado nada.
- ZAP requiere un entorno de staging y URL autorizada; el workflow de DAST está en el repositorio frontend y solo se ejecuta manualmente contra esa URL. No se ejecutan pruebas DAST contra producción.
- No se usa Docker, por lo que Trivy no aplica. SonarQube no se agrega para evitar una plataforma adicional de mantenimiento.

Registrar en `documentacion/` fecha, ejecución, resultado, hallazgo, severidad, corrección y estado (configurado, ejecutado, verificado, pendiente o no disponible). No inventar resultados.
