using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class TecniHubDbContextFactory : IDesignTimeDbContextFactory<TecniHubDbContext>
{
    public TecniHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TECNIHUB_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Define la variable de entorno TECNIHUB_MIGRATION_CONNECTION para ejecutar herramientas de migración.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<TecniHubDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(TecniHubDbContext).Assembly.FullName));

        return new TecniHubDbContext(optionsBuilder.Options);
    }
}
