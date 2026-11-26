using System.Net.Mail;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.Application.Auth.Services;

public sealed class AuthService : IAuthService
{
    public const string GenericRecoveryNotice =
        "Si existe una cuenta con ese correo, recibirás instrucciones para continuar.";

    public const string GenericResendVerificationMessage =
        "Si existe una cuenta pendiente de verificación con ese correo, enviaremos un nuevo código.";

    private static readonly TimeSpan AccountDeletionRetentionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan CodeResendCooldown = TimeSpan.FromSeconds(60);
    private const int MaxCodesPerHour = 3;
    private static readonly string TimingMitigationPlaceholderHash =
        string.Concat("v1.210000.", Convert.ToBase64String(new byte[16]), ".", Convert.ToBase64String(new byte[32]));

    private readonly IAuthRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public AuthService(
        IAuthRepository repository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IEmailSender emailSender,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<RegistrationResponse>> RegisterClientAsync(
        RegisterClientRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateCommonRegistration(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password,
            request.Phone);

        if (validationErrors.Count > 0)
        {
            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos proporcionados.",
                validationErrors));
        }

        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        if (await _repository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            await _auditLogger.LogAsync(
                action: "UserRegistrationRejectedDuplicate",
                entityName: "User",
                entityId: null,
                outcome: AuditOutcome.Failure,
                metadata: new { role = nameof(UserRole.Client) },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.Conflict,
                "email_already_registered",
                "Ya existe una cuenta registrada con este correo electrónico."));
        }

        var utcNow = _clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = (int)UserRole.Client,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsEmailVerified = false,
            Status = AccountStatus.PendingVerification,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        var rawCode = _tokenService.GenerateSixDigitCode();
        var verificationCode = CreateVerificationCode(user.Id, VerificationPurpose.EmailVerification, rawCode, utcNow);

        await _repository.AddUserAsync(user, cancellationToken);
        await _repository.AddVerificationCodeAsync(verificationCode, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var emailSent = await _emailSender.SendVerificationCodeAsync(
            user.Email,
            user.FirstName,
            rawCode,
            cancellationToken);

        await _auditLogger.LogAsync(
            action: "UserRegisteredClient",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: emailSent ? AuditOutcome.Success : AuditOutcome.Failure,
            actorUserId: user.Id,
            actorRole: nameof(UserRole.Client),
            metadata: new { emailDelivered = emailSent },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        if (!emailSent)
        {
            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "email_delivery_failed",
                "La cuenta fue creada, pero no fue posible enviar el código de verificación. Intenta reenviar el código."));
        }

        return Result<RegistrationResponse>.Success(new RegistrationResponse(
            "Registro completado. Ingresa el código de verificación enviado a tu correo.",
            user.Email,
            RequiresEmailVerification: true));
    }

    public async Task<Result<RegistrationResponse>> RegisterTechnicianAsync(
        RegisterTechnicianRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateCommonRegistration(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password,
            request.Phone);

        if (string.IsNullOrWhiteSpace(request.Specialties) || request.Specialties.Trim().Length > 300)
        {
            validationErrors.Add("Las especialidades son obligatorias (máximo 300 caracteres).");
        }

        if (request.ExperienceYears is < 0 or > 60)
        {
            validationErrors.Add("Los años de experiencia deben estar entre 0 y 60.");
        }

        if (string.IsNullOrWhiteSpace(request.ServiceZone) || request.ServiceZone.Trim().Length > 150)
        {
            validationErrors.Add("La zona de atención es obligatoria (máximo 150 caracteres).");
        }

        if (!string.IsNullOrWhiteSpace(request.BusinessName) && request.BusinessName.Trim().Length > 120)
        {
            validationErrors.Add("El nombre comercial no debe superar 120 caracteres.");
        }

        if (validationErrors.Count > 0)
        {
            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos proporcionados.",
                validationErrors));
        }

        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        if (await _repository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            await _auditLogger.LogAsync(
                action: "UserRegistrationRejectedDuplicate",
                entityName: "User",
                entityId: null,
                outcome: AuditOutcome.Failure,
                metadata: new { role = nameof(UserRole.Technician) },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.Conflict,
                "email_already_registered",
                "Ya existe una cuenta registrada con este correo electrónico."));
        }

        var utcNow = _clock.UtcNow;
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            RoleId = (int)UserRole.Technician,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsEmailVerified = false,
            Status = AccountStatus.PendingVerification,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            TechnicianProfile = new TechnicianProfile
            {
                UserId = userId,
                Specialties = request.Specialties.Trim(),
                ExperienceYears = request.ExperienceYears,
                ServiceZone = request.ServiceZone.Trim(),
                BusinessName = string.IsNullOrWhiteSpace(request.BusinessName) ? null : request.BusinessName.Trim(),
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }
        };

        var rawCode = _tokenService.GenerateSixDigitCode();
        var verificationCode = CreateVerificationCode(user.Id, VerificationPurpose.EmailVerification, rawCode, utcNow);

        await _repository.AddUserAsync(user, cancellationToken);
        await _repository.AddVerificationCodeAsync(verificationCode, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var emailSent = await _emailSender.SendVerificationCodeAsync(
            user.Email,
            user.FirstName,
            rawCode,
            cancellationToken);

        await _auditLogger.LogAsync(
            action: "UserRegisteredTechnician",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: emailSent ? AuditOutcome.Success : AuditOutcome.Failure,
            actorUserId: user.Id,
            actorRole: nameof(UserRole.Technician),
            metadata: new { emailDelivered = emailSent },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        if (!emailSent)
        {
            return Result<RegistrationResponse>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "email_delivery_failed",
                "La cuenta fue creada, pero no fue posible enviar el código de verificación. Intenta reenviar el código."));
        }

        return Result<RegistrationResponse>.Success(new RegistrationResponse(
            "Registro completado. Ingresa el código de verificación enviado a tu correo.",
            user.Email,
            RequiresEmailVerification: true));
    }

    public async Task<Result<GenericMessageResponse>> VerifyEmailAsync(
        VerifyEmailRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(request.Email) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "El correo y el código de verificación son obligatorios."));
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _repository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null || user.Status != AccountStatus.PendingVerification)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_verification_code",
                "El código de verificación no es válido o ha expirado."));
        }

        var utcNow = _clock.UtcNow;
        var lockout = await _repository.GetActiveLockoutAsync(user.Id, VerificationPurpose.EmailVerification, utcNow, cancellationToken);
        if (lockout is not null)
        {
            await _auditLogger.LogAsync(
                action: "EmailVerificationBlocked",
                entityName: "VerificationCode",
                entityId: lockout.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.TooManyRequests,
                "verification_locked",
                "Has excedido el máximo de intentos permitidos. Debes esperar 1 hora antes de volver a intentarlo."));
        }

        var latestCode = await _repository.GetLatestVerificationCodeAsync(user.Id, VerificationPurpose.EmailVerification, cancellationToken);
        if (latestCode is null || latestCode.IsConsumed || latestCode.IsExpired(utcNow))
        {
            await _auditLogger.LogAsync(
                action: "EmailVerificationExpiredOrMissing",
                entityName: "VerificationCode",
                entityId: latestCode?.Id.ToString(),
                outcome: AuditOutcome.Failure,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "expired_verification_code",
                "El código de verificación no es válido o ha expirado."));
        }

        var providedHash = _tokenService.ComputeScopedCodeHash(
            latestCode.Id,
            user.Id,
            VerificationPurpose.EmailVerification,
            request.Code.Trim());
        if (!_tokenService.FixedTimeEquals(latestCode.CodeHash, providedHash))
        {
            var isNowLocked = latestCode.RegisterFailedAttempt(utcNow);
            await _repository.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                action: isNowLocked ? "EmailVerificationLockedOut" : "EmailVerificationFailed",
                entityName: "VerificationCode",
                entityId: latestCode.Id.ToString(),
                outcome: isNowLocked ? AuditOutcome.Blocked : AuditOutcome.Failure,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                metadata: new { attempts = latestCode.AttemptCount },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            if (isNowLocked)
            {
                return Result<GenericMessageResponse>.Failure(new AppError(
                    ErrorType.TooManyRequests,
                    "verification_locked",
                    "Has excedido el límite de 5 intentos. Debes esperar 1 hora para solicitar o validar otro código."));
            }

            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_verification_code",
                "El código de verificación no es válido o ha expirado."));
        }

        latestCode.MarkConsumed(utcNow);
        user.MarkEmailVerified(utcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "EmailVerified",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse("Correo verificado correctamente. Ya puedes iniciar sesión."));
    }

    public async Task<Result<GenericMessageResponse>> ResendVerificationCodeAsync(
        ResendVerificationRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidEmail(request.Email))
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_email",
                "Ingresa un correo electrónico con formato válido."));
        }

        var user = await _repository.GetUserByEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        if (user is null || user.Status != AccountStatus.PendingVerification)
        {
            return Result<GenericMessageResponse>.Success(
                new GenericMessageResponse(GenericResendVerificationMessage));
        }

        var utcNow = _clock.UtcNow;
        var lockout = await _repository.GetActiveLockoutAsync(user.Id, VerificationPurpose.EmailVerification, utcNow, cancellationToken);
        if (lockout is not null || await IsCodeIssuanceThrottledAsync(user.Id, VerificationPurpose.EmailVerification, utcNow, cancellationToken))
        {
            await _auditLogger.LogAsync(
                action: "VerificationCodeResendThrottled",
                entityName: "VerificationCode",
                entityId: lockout?.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Success(
                new GenericMessageResponse(GenericResendVerificationMessage));
        }

        await _repository.InvalidatePendingCodesAsync(user.Id, VerificationPurpose.EmailVerification, utcNow, cancellationToken);

        var rawCode = _tokenService.GenerateSixDigitCode();
        var newCode = CreateVerificationCode(user.Id, VerificationPurpose.EmailVerification, rawCode, utcNow);
        await _repository.AddVerificationCodeAsync(newCode, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var sent = await _emailSender.SendVerificationCodeAsync(user.Email, user.FirstName, rawCode, cancellationToken);

        await _auditLogger.LogAsync(
            action: "VerificationCodeResent",
            entityName: "VerificationCode",
            entityId: newCode.Id.ToString(),
            outcome: sent ? AuditOutcome.Success : AuditOutcome.Failure,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        if (!sent)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "email_delivery_failed",
                "No fue posible enviar el mensaje. Intenta nuevamente respetando los límites."));
        }

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse(GenericResendVerificationMessage));
    }

    public async Task<Result<AuthSessionResult>> LoginAsync(
        LoginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "El correo y la contraseña son obligatorios."));
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _repository.GetUserByEmailAsync(normalizedEmail, cancellationToken);

        if (user is null || user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            _ = _passwordHasher.Verify(request.Password, TimingMitigationPlaceholderHash);

            await _auditLogger.LogAsync(
                action: "LoginFailed",
                entityName: "User",
                entityId: user?.Id.ToString(),
                outcome: AuditOutcome.Failure,
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "invalid_credentials",
                "Correo o contraseña no válidos."));
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await _auditLogger.LogAsync(
                action: "LoginFailed",
                entityName: "User",
                entityId: user.Id.ToString(),
                outcome: AuditOutcome.Failure,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "invalid_credentials",
                "Correo o contraseña no válidos."));
        }

        if (!user.IsEmailVerified || user.Status == AccountStatus.PendingVerification)
        {
            await _auditLogger.LogAsync(
                action: "LoginBlockedUnverified",
                entityName: "User",
                entityId: user.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Forbidden,
                "email_not_verified",
                "Debes verificar tu correo electrónico antes de iniciar sesión."));
        }

        var utcNow = _clock.UtcNow;
        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var refreshExpiresAt = utcNow.AddDays(RefreshToken.LifetimeDays);

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = _tokenService.ComputeSha256Hash(rawRefreshToken),
            ExpiresAt = refreshExpiresAt,
            IpAddress = metadata.IpAddress,
            UserAgent = metadata.UserAgent,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        await _repository.AddRefreshTokenAsync(refreshTokenEntity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "UserLoggedIn",
            entityName: "RefreshToken",
            entityId: refreshTokenEntity.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<AuthSessionResult>.Success(new AuthSessionResult(
            accessToken,
            accessExpiresAt,
            rawRefreshToken,
            refreshExpiresAt,
            MapUserSummary(user)));
    }

    public async Task<Result<AuthSessionResult>> RefreshSessionAsync(
        string? rawRefreshToken,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "missing_refresh_token",
                "La sesión no es válida o ha expirado."));
        }

        var utcNow = _clock.UtcNow;
        var tokenHash = _tokenService.ComputeSha256Hash(rawRefreshToken);
        var existingToken = await _repository.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.User is null)
        {
            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "invalid_refresh_token",
                "La sesión no es válida o ha expirado."));
        }

        if (existingToken.RevokedAt.HasValue)
        {
            await _repository.RevokeAllUserRefreshTokensAsync(existingToken.UserId, utcNow, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                action: "RefreshTokenReuseDetected",
                entityName: "RefreshToken",
                entityId: existingToken.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: existingToken.UserId,
                actorRole: existingToken.User.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "revoked_refresh_token",
                "La sesión fue revocada. Inicia sesión nuevamente."));
        }

        if (!existingToken.IsActive(utcNow) || existingToken.User.Status != AccountStatus.Active)
        {
            return Result<AuthSessionResult>.Failure(new AppError(
                ErrorType.Unauthorized,
                "expired_refresh_token",
                "La sesión ha expirado. Inicia sesión nuevamente."));
        }

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(existingToken.User);
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshExpiresAt = utcNow.AddDays(RefreshToken.LifetimeDays);

        var replacementToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            TokenHash = _tokenService.ComputeSha256Hash(newRawRefreshToken),
            ExpiresAt = newRefreshExpiresAt,
            IpAddress = metadata.IpAddress,
            UserAgent = metadata.UserAgent,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        existingToken.Revoke(utcNow, replacementToken.Id);

        await _repository.AddRefreshTokenAsync(replacementToken, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "SessionRefreshed",
            entityName: "RefreshToken",
            entityId: replacementToken.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: existingToken.UserId,
            actorRole: existingToken.User.GetUserRole().ToString(),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<AuthSessionResult>.Success(new AuthSessionResult(
            accessToken,
            accessExpiresAt,
            newRawRefreshToken,
            newRefreshExpiresAt,
            MapUserSummary(existingToken.User)));
    }

    public async Task<Result<GenericMessageResponse>> LogoutAsync(
        string? rawRefreshToken,
        Guid? actorUserId,
        string? actorRole,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        Guid? resolvedUserId = actorUserId;
        string? resolvedRole = actorRole;
        string? revokedTokenId = null;

        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = _tokenService.ComputeSha256Hash(rawRefreshToken);
            var existingToken = await _repository.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);
            if (existingToken is not null)
            {
                existingToken.Revoke(utcNow);
                revokedTokenId = existingToken.Id.ToString();
                resolvedUserId ??= existingToken.UserId;
                resolvedRole ??= existingToken.User?.GetUserRole().ToString();
                await _repository.SaveChangesAsync(cancellationToken);
            }
        }

        await _auditLogger.LogAsync(
            action: "UserLoggedOut",
            entityName: "RefreshToken",
            entityId: revokedTokenId,
            outcome: AuditOutcome.Success,
            actorUserId: resolvedUserId,
            actorRole: resolvedRole,
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse("Sesión cerrada correctamente."));
    }

    public async Task<Result<GenericMessageResponse>> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "email_required",
                "El correo electrónico es obligatorio."));
        }

        if (!IsValidEmail(request.Email))
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_email",
                "Ingresa un correo electrónico con formato válido."));
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _repository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        var utcNow = _clock.UtcNow;

        if (user is null || user.Status != AccountStatus.Active)
        {
            await _auditLogger.LogAsync(
                action: "PasswordResetRequested",
                entityName: "User",
                entityId: null,
                outcome: AuditOutcome.Success,
                metadata: new { accountMatched = false },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Success(
                new GenericMessageResponse(GenericRecoveryNotice));
        }

        var lockout = await _repository.GetActiveLockoutAsync(user.Id, VerificationPurpose.PasswordReset, utcNow, cancellationToken);
        if (lockout is not null || await IsCodeIssuanceThrottledAsync(user.Id, VerificationPurpose.PasswordReset, utcNow, cancellationToken))
        {
            await _auditLogger.LogAsync(
                action: "PasswordResetRequestBlocked",
                entityName: "VerificationCode",
                entityId: lockout?.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Success(
                new GenericMessageResponse(GenericRecoveryNotice));
        }

        await _repository.InvalidatePendingCodesAsync(user.Id, VerificationPurpose.PasswordReset, utcNow, cancellationToken);

        var rawCode = _tokenService.GenerateSixDigitCode();
        var resetCode = CreateVerificationCode(user.Id, VerificationPurpose.PasswordReset, rawCode, utcNow);
        await _repository.AddVerificationCodeAsync(resetCode, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var emailSent = await _emailSender.SendPasswordResetCodeAsync(
            user.Email,
            user.FirstName,
            rawCode,
            cancellationToken);

        await _auditLogger.LogAsync(
            action: "PasswordResetRequested",
            entityName: "VerificationCode",
            entityId: resetCode.Id.ToString(),
            outcome: emailSent ? AuditOutcome.Success : AuditOutcome.Failure,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            metadata: new { emailDelivered = emailSent },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        if (!emailSent)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "email_delivery_failed",
                "No fue posible enviar el mensaje. Intenta nuevamente respetando los límites."));
        }

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse(GenericRecoveryNotice));
    }

    public async Task<Result<GenericMessageResponse>> ConfirmPasswordResetAsync(
        ConfirmPasswordResetRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (!IsValidEmail(request.Email))
        {
            errors.Add("Ingresa un correo electrónico con formato válido.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add("El código de recuperación es obligatorio.");
        }

        errors.AddRange(PasswordPolicy.Validate(request.NewPassword));

        if (errors.Count > 0)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos proporcionados.",
                errors));
        }

        var user = await _repository.GetUserByEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_reset_code",
                "El código de recuperación no es válido o ha expirado."));
        }

        var utcNow = _clock.UtcNow;
        var lockout = await _repository.GetActiveLockoutAsync(user.Id, VerificationPurpose.PasswordReset, utcNow, cancellationToken);
        if (lockout is not null)
        {
            await _auditLogger.LogAsync(
                action: "PasswordResetBlocked",
                entityName: "VerificationCode",
                entityId: lockout.Id.ToString(),
                outcome: AuditOutcome.Blocked,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.TooManyRequests,
                "reset_locked",
                "Has excedido el límite de 5 intentos. Debes esperar 1 hora antes de volver a intentarlo."));
        }

        var latestCode = await _repository.GetLatestVerificationCodeAsync(user.Id, VerificationPurpose.PasswordReset, cancellationToken);
        if (latestCode is null || latestCode.IsConsumed || latestCode.IsExpired(utcNow))
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_reset_code",
                "El código de recuperación no es válido o ha expirado."));
        }

        var providedHash = _tokenService.ComputeScopedCodeHash(
            latestCode.Id,
            user.Id,
            VerificationPurpose.PasswordReset,
            request.Code.Trim());
        if (!_tokenService.FixedTimeEquals(latestCode.CodeHash, providedHash))
        {
            var isNowLocked = latestCode.RegisterFailedAttempt(utcNow);
            await _repository.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                action: isNowLocked ? "PasswordResetLockedOut" : "PasswordResetFailed",
                entityName: "VerificationCode",
                entityId: latestCode.Id.ToString(),
                outcome: isNowLocked ? AuditOutcome.Blocked : AuditOutcome.Failure,
                actorUserId: user.Id,
                actorRole: user.GetUserRole().ToString(),
                metadata: new { attempts = latestCode.AttemptCount },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            if (isNowLocked)
            {
                return Result<GenericMessageResponse>.Failure(new AppError(
                    ErrorType.TooManyRequests,
                    "reset_locked",
                    "Has excedido el límite de 5 intentos. Debes esperar 1 hora antes de volver a intentarlo."));
            }

            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_reset_code",
                "El código de recuperación no es válido o ha expirado."));
        }

        latestCode.MarkConsumed(utcNow);
        user.UpdatePasswordHash(_passwordHasher.Hash(request.NewPassword), utcNow);
        await _repository.RevokeAllUserRefreshTokensAsync(user.Id, utcNow, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "PasswordResetCompleted",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse("Tu contraseña se actualizó correctamente. Ya puedes iniciar sesión."));
    }

    public async Task<Result<GenericMessageResponse>> RequestAccountDeletionAsync(
        Guid userId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Result<GenericMessageResponse>.Failure(new AppError(
                ErrorType.NotFound,
                "user_not_found",
                "Cuenta no encontrada."));
        }

        var utcNow = _clock.UtcNow;
        user.RequestGradualDeletion(utcNow, AccountDeletionRetentionWindow);
        await _repository.RevokeAllUserRefreshTokensAsync(user.Id, utcNow, cancellationToken);
        await _repository.DeactivateActiveListingsForUserAsync(user.Id, utcNow, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "AccountDeletionRequested",
            entityName: "User",
            entityId: user.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: user.Id,
            actorRole: user.GetUserRole().ToString(),
            metadata: new { scheduledPurgeAt = user.ScheduledPurgeAt },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<GenericMessageResponse>.Success(
            new GenericMessageResponse("Tu cuenta fue marcada para eliminación gradual y tus sesiones activas fueron cerradas."));
    }

    public async Task<Result<UserSummaryDto>> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Result<UserSummaryDto>.Failure(new AppError(
                ErrorType.NotFound,
                "user_not_found",
                "Cuenta no disponible."));
        }

        return Result<UserSummaryDto>.Success(MapUserSummary(user));
    }

    public async Task<int> ProcessScheduledAccountPurgesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var usersToPurge = await _repository.GetUsersPendingPurgeAsync(utcNow, cancellationToken);
        if (usersToPurge.Count == 0)
        {
            return 0;
        }

        foreach (var user in usersToPurge)
        {
            user.AnonymizePersonalData(utcNow);
            await _repository.RevokeAllUserRefreshTokensAsync(user.Id, utcNow, cancellationToken);
            await _repository.AnonymizeUserOperationalPiiAsync(user.Id, utcNow, cancellationToken);
            await _auditLogger.LogAsync(
                action: "AccountAnonymized",
                entityName: "User",
                entityId: user.Id.ToString(),
                outcome: AuditOutcome.Success,
                actorUserId: null,
                actorRole: "System",
                cancellationToken: cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return usersToPurge.Count;
    }

    private VerificationCode CreateVerificationCode(
        Guid userId,
        VerificationPurpose purpose,
        string rawCode,
        DateTimeOffset utcNow)
    {
        var codeId = Guid.NewGuid();
        return new VerificationCode
        {
            Id = codeId,
            UserId = userId,
            Purpose = purpose,
            CodeHash = _tokenService.ComputeScopedCodeHash(codeId, userId, purpose, rawCode),
            ExpiresAt = utcNow.AddMinutes(VerificationCode.ExpirationMinutes),
            AttemptCount = 0,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    private async Task<bool> IsCodeIssuanceThrottledAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var latest = await _repository.GetLatestVerificationCodeAsync(userId, purpose, cancellationToken);
        if (latest is not null && utcNow - latest.CreatedAt < CodeResendCooldown)
        {
            return true;
        }

        var recentCount = await _repository.CountRecentVerificationCodesAsync(
            userId,
            purpose,
            utcNow.AddHours(-1),
            cancellationToken);

        return recentCount >= MaxCodesPerHour;
    }

    private static List<string> ValidateCommonRegistration(
        string? firstName,
        string? lastName,
        string? email,
        string? password,
        string? phone)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 80)
        {
            errors.Add("El nombre es obligatorio (máximo 80 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length > 100)
        {
            errors.Add("Los apellidos son obligatorios (máximo 100 caracteres).");
        }

        if (!IsValidEmail(email))
        {
            errors.Add("Ingresa un correo electrónico con formato válido.");
        }

        if (!string.IsNullOrWhiteSpace(phone) && (phone.Trim().Length < 7 || phone.Trim().Length > 20))
        {
            errors.Add("El teléfono debe tener entre 7 y 20 caracteres.");
        }

        errors.AddRange(PasswordPolicy.Validate(password));
        return errors;
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !trimmed.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(trimmed);
            return string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

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
