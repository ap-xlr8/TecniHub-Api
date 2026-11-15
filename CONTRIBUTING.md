# Contribuir

1. Usa una rama `HU-XXX-descripcion` desde `sprint/nombre-sprint`; integra la HU a su sprint mediante PR. Después integra el sprint a `developer` y prepara versiones desde `release/vX.Y.Z`.
2. Usa commits [Conventional Commits](https://www.conventionalcommits.org/), por ejemplo `chore: scaffold API projects` o `docs: document local setup`.
3. Antes de abrir un PR, ejecuta restore, formato, build y tests según el README.
4. Completa `.github/PULL_REQUEST_TEMPLATE.md` con evidencias reales. No declares revisiones humanas que no ocurrieron.
5. No incluyas secretos, credenciales, claves privadas, archivos `bin/` u `obj/`.

Como actualmente hay una persona desarrolladora, CI y revisión propia documentada son los controles posibles. No se exige una aprobación independiente hasta que exista otra persona revisora.
