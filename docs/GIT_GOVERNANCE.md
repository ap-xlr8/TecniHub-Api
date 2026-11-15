# Gobernanza Git

El repositorio local se inicializó en `main`; todavía no tiene commits, remoto, ramas adicionales ni reglas de protección. Flujo documentado: `HU-XXX-descripcion` → `sprint/nombre-sprint` → `developer` → `release/vX.Y.Z` → `main`. Hotfixes y excepciones deben documentarse. No se creó una rama de HU no aprobada.

Usa Pull Requests para dejar trazabilidad, aunque el autor sea también la única persona que revisa. Registra una auto-revisión y evidencias; nunca marques aprobación humana inexistente. En GitHub no se exigirá una aprobación independiente hasta que exista otro revisor. Activar checks requeridos, rulesets, Secret Scanning y Push Protection requiere repositorio remoto y confirmar plan/permisos.

Antes de integrar, sincroniza tu rama con su base; ante conflictos, conserva ambas intenciones funcionales, resuelve en la rama de trabajo, ejecuta CI y documenta la decisión. No hagas force push a ramas compartidas.
