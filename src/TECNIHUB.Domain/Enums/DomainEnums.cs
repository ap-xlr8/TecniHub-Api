namespace TECNIHUB.Domain.Enums;

public enum UserRole
{
    Client = 1,
    Technician = 2
}

public enum AccountStatus
{
    PendingVerification = 1,
    Active = 2,
    PendingDeletion = 3,
    Anonymized = 4
}

public enum VerificationPurpose
{
    EmailVerification = 1,
    PasswordReset = 2
}

public enum AuditOutcome
{
    Success = 1,
    Failure = 2,
    Blocked = 3
}
