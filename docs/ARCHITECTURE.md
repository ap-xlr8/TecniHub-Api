# Arquitectura del backend

## Estado actual

La solución usa .NET 10 LTS y contiene cuatro proyectos productivos y dos proyectos de pruebas. No contiene entidades de TECNIHUB, acceso a datos, autenticación ni casos de uso. La API publica el documento OpenAPI en desarrollo y no define endpoints de negocio.

## Regla de dependencias

| Proyecto | Puede referenciar | No debe referenciar |
|---|---|---|
| Domain | Nada de los otros proyectos | Application, Infrastructure, API, EF Core, ASP.NET Core |
| Application | Domain | Infrastructure, API |
| Infrastructure | Application, Domain | API |
| API | Application, Infrastructure | Reglas o persistencia de negocio dentro de sus controladores |
| UnitTests | Domain, Application | Infrastructure para pruebas unitarias puras |
| IntegrationTests | API | — |

La API será el punto de composición. Al añadir casos de uso, servicios técnicos y contratos aprobados, se registrarán allí las extensiones DI correspondientes. No se agregan métodos DI vacíos ni capas de negocio ficticias.

## Persistencia

Se decidió PostgreSQL local para desarrollo y Neon (PostgreSQL administrado) como destino previsto. La integración de persistencia todavía no está en la solución. La selección e incorporación del ORM (por ejemplo, EF Core con proveedor Npgsql) se realizará al implementar acceso a datos.

## Comprobación

Las referencias actuales se validan con `dotnet list TECNIHUB.sln reference` y la solución se compila en CI. Una prueba de arquitectura explícita podrá añadirse cuando la estructura tenga suficientes tipos y contratos para justificarla.
