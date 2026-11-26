using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Profile;

public sealed class ProfileService : IProfileService
{
    private readonly IAuthRepository _authRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public ProfileService(
        IAuthRepository authRepository,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _authRepository = authRepository;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<UserSummaryDto>> GetOwnProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Result<UserSummaryDto>.Failure(new AppError(
                ErrorType.NotFound,
                "profile_not_found",
                "Perfil no disponible."));
        }

        return Result<UserSummaryDto>.Success(MapUserSummary(user));
    }

    public async Task<Result<UserSummaryDto>> UpdateOwnProfileAsync(
        Guid userId,
        UpdateOwnProfileRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Result<UserSummaryDto>.Failure(new AppError(
                ErrorType.NotFound,
                "profile_not_found",
                "Perfil no disponible."));
        }

        var errors = new List<string>();
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var publicRegion = string.IsNullOrWhiteSpace(request.PublicRegion) ? null : request.PublicRegion.Trim();

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length > 80)
        {
            errors.Add("El nombre es obligatorio (máximo 80 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Length > 100)
        {
            errors.Add("Los apellidos son obligatorios (máximo 100 caracteres).");
        }

        if (phone is not null && !IsValidPhone(phone))
        {
            errors.Add("El teléfono debe contener entre 7 y 20 caracteres numéricos o de formato telefónico válido.");
        }

        if (publicRegion is not null && publicRegion.Length > 120)
        {
            errors.Add("La zona general pública no debe superar 120 caracteres.");
        }

        var isTechnician = user.GetUserRole() == UserRole.Technician;
        string? specialties = null;
        string? serviceZone = null;
        string? businessName = null;
        int experienceYears = 0;

        if (isTechnician)
        {
            specialties = request.Specialties?.Trim() ?? string.Empty;
            serviceZone = request.ServiceZone?.Trim() ?? string.Empty;
            businessName = string.IsNullOrWhiteSpace(request.BusinessName) ? null : request.BusinessName.Trim();

            if (string.IsNullOrWhiteSpace(specialties) || specialties.Length > 300)
            {
                errors.Add("Las especialidades son obligatorias (máximo 300 caracteres).");
            }

            if (!request.ExperienceYears.HasValue || request.ExperienceYears.Value is < 0 or > 60)
            {
                errors.Add("Los años de experiencia deben estar entre 0 y 60.");
            }
            else
            {
                experienceYears = request.ExperienceYears.Value;
            }

            if (string.IsNullOrWhiteSpace(serviceZone) || serviceZone.Length > 150)
            {
                errors.Add("La zona de atención es obligatoria (máximo 150 caracteres).");
            }

            if (businessName is not null && businessName.Length > 120)
            {
                errors.Add("El nombre comercial no debe superar 120 caracteres.");
            }
        }

        if (errors.Count > 0)
        {
            return Result<UserSummaryDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos de tu perfil antes de guardar.",
                errors));
        }

        var utcNow = _clock.UtcNow;
        user.UpdateBasicProfile(firstName, lastName, phone, utcNow, publicRegion);

        if (isTechnician)
        {
            if (user.TechnicianProfile is null)
            {
                user.TechnicianProfile = new TechnicianProfile
                {
                    UserId = user.Id,
                    Specialties = specialties!,
                    ExperienceYears = experienceYears,
                    ServiceZone = serviceZone!,
                    BusinessName = businessName,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };
            }
            else
            {
                user.TechnicianProfile.UpdateDetails(specialties!, experienceYears, serviceZone!, businessName, utcNow);
            }
        }

        await _authRepository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "ProfileUpdated",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            metadata: new { role = user.GetUserRole().ToString(), hasPhone = phone is not null },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<UserSummaryDto>.Success(MapUserSummary(user));
    }

    private static bool IsValidPhone(string phone)
    {
        if (phone.Length is < 7 or > 20)
        {
            return false;
        }

        var digits = 0;
        foreach (var ch in phone)
        {
            if (char.IsDigit(ch))
            {
                digits++;
            }
            else if (ch is not ('+' or '-' or ' ' or '(' or ')'))
            {
                return false;
            }
        }

        return digits is >= 7 and <= 15;
    }

    private static UserSummaryDto MapUserSummary(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email,
        user.Phone,
        user.GetUserRole().ToString(),
        user.IsEmailVerified,
        user.TechnicianProfile is null
            ? null
            : new TechnicianProfileDto(
                user.TechnicianProfile.Specialties,
                user.TechnicianProfile.ExperienceYears,
                user.TechnicianProfile.ServiceZone,
                user.TechnicianProfile.BusinessName),
        user.PublicRegion);
}
