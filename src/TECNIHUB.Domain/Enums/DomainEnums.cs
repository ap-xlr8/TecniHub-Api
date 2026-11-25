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

public enum RepairRequestStatus
{
    Open = 1,
    Assigned = 2,
    Scheduled = 3,
    InProgress = 4,
    ClosureRequested = 5,
    Completed = 6,
    Cancelled = 7
}

public enum SparePartCondition
{
    New = 1,
    Used = 2,
    Refurbished = 3
}

public enum SparePartModality
{
    Sale = 1,
    Exchange = 2,
    SaleOrExchange = 3
}

public enum ConversationContextType
{
    RepairRequest = 1,
    SparePart = 2
}
