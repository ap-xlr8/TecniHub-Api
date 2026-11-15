# Aseguramiento de calidad

CI ejecuta formato, restauración, compilación y pruebas. xUnit provee pruebas unitarias y `WebApplicationFactory` permite probar el host HTTP. Actualmente hay un smoke test que comprueba que OpenAPI responde en Development; los archivos `UnitTests` contienen el caso vacío de plantilla que debe sustituirse por pruebas significativas al incorporar lógica real.

No se fija un porcentaje de cobertura antes de tener casos de uso. La Definition of Done inicial exige: HU aprobada, criterios cubiertos por pruebas apropiadas, formato/build/test limpios, revisión propia documentada y documentación/API actualizada cuando aplique. Aún no hay aprobación humana independiente.
