using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Auth.Services;
using TECNIHUB.Infrastructure.Audit;
using TECNIHUB.Infrastructure.Email;
using TECNIHUB.Infrastructure.Persistence;
using TECNIHUB.Infrastructure.Security;
using TECNIHUB.Infrastructure.Time;

namespace TECNIHUB.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        services.Configure<JwtOptions>(options =>
        {
            var section = configuration.GetSection(JwtOptions.SectionName);
            options.Issuer = section["Issuer"] ?? options.Issuer;
            options.Audience = section["Audience"] ?? options.Audience;
            options.SigningKey = section["SigningKey"] ?? string.Empty;
            if (int.TryParse(section["AccessTokenMinutes"], out var minutes) && minutes > 0)
            {
                options.AccessTokenMinutes = minutes;
            }
        });

        var smtpSection = configuration.GetSection(SmtpOptions.SectionName);
        var smtpUsername = smtpSection["Username"] ?? string.Empty;
        var smtpPassword = smtpSection["Password"] ?? string.Empty;
        services.Configure<SmtpOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(smtpSection["Host"]))
            {
                options.Host = smtpSection["Host"]!;
            }

            if (int.TryParse(smtpSection["Port"], out var port) && port > 0)
            {
                options.Port = port;
            }

            if (bool.TryParse(smtpSection["EnableSsl"], out var enableSsl))
            {
                options.EnableSsl = enableSsl;
            }

            options.Username = smtpUsername;
            options.Password = smtpPassword;

            if (!string.IsNullOrWhiteSpace(smtpSection["FromName"]))
            {
                options.FromName = smtpSection["FromName"]!;
            }

            options.FromEmail = smtpSection["FromEmail"] ?? string.Empty;
        });

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<TecniHubDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(TecniHubDbContext).Assembly.FullName));
            }
        });

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();

        if (!string.IsNullOrWhiteSpace(smtpUsername) && !string.IsNullOrWhiteSpace(smtpPassword))
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else if (isDevelopment)
        {
            services.AddScoped<IEmailSender, DevEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, UnconfiguredEmailSender>();
        }

        services.AddScoped<IAuditLogger, AuditLoggerService>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
