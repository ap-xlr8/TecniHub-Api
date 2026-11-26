using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Domain.Entities;

public sealed class RepairRequest
{
    public const int MaxImages = 5;

    public Guid Id { get; set; }
    public Guid ClientUserId { get; set; }
    public User? ClientUser { get; set; }

    public Guid? AssignedTechnicianId { get; set; }
    public User? AssignedTechnician { get; set; }

    public string ApplianceName { get; set; } = string.Empty;
    public string? BrandModel { get; set; }
    public string IssueDescription { get; set; } = string.Empty;

    public DateOnly PreferredDate { get; set; }
    public string PreferredTimeSlot { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string? City { get; set; }
    public string Neighborhood { get; set; } = string.Empty;

    // Sensitive address details: exposed only to owner and assigned technician after confirmed assignment.
    public string Street { get; set; } = string.Empty;
    public string? ExteriorNumber { get; set; }
    public string? InteriorNumber { get; set; }
    public string? AddressReference { get; set; }

    public RepairRequestStatus Status { get; set; } = RepairRequestStatus.Open;

    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ScheduledStartAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ClosureRequestedAt { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<RepairRequestImage> Images { get; set; } = [];

    public bool IsConfirmedAssignment =>
        AssignedTechnicianId.HasValue
        && Status is RepairRequestStatus.Assigned
            or RepairRequestStatus.Scheduled
            or RepairRequestStatus.InProgress
            or RepairRequestStatus.ClosureRequested
            or RepairRequestStatus.Completed;

    public bool CanExposeFullAddressTo(Guid actorUserId) =>
        actorUserId == ClientUserId
        || (IsConfirmedAssignment && AssignedTechnicianId == actorUserId);

    public bool Accept(Guid technicianUserId, DateTimeOffset utcNow)
    {
        if (Status != RepairRequestStatus.Open || AssignedTechnicianId.HasValue)
        {
            return false;
        }

        AssignedTechnicianId = technicianUserId;
        AssignedAt = utcNow;
        Status = RepairRequestStatus.Assigned;
        UpdatedAt = utcNow;
        return true;
    }

    public bool Schedule(Guid technicianUserId, DateTimeOffset scheduledStartUtc, DateTimeOffset utcNow)
    {
        if (AssignedTechnicianId != technicianUserId)
        {
            return false;
        }

        if (Status is not (RepairRequestStatus.Assigned or RepairRequestStatus.Scheduled))
        {
            return false;
        }

        ScheduledStartAt = scheduledStartUtc;
        Status = RepairRequestStatus.Scheduled;
        UpdatedAt = utcNow;
        return true;
    }

    public bool StartWork(Guid technicianUserId, DateTimeOffset utcNow)
    {
        if (AssignedTechnicianId != technicianUserId)
        {
            return false;
        }

        if (Status is not (RepairRequestStatus.Assigned or RepairRequestStatus.Scheduled))
        {
            return false;
        }

        StartedAt = utcNow;
        Status = RepairRequestStatus.InProgress;
        UpdatedAt = utcNow;
        return true;
    }

    public bool RequestClosure(Guid technicianUserId, string? notes, DateTimeOffset utcNow)
    {
        if (AssignedTechnicianId != technicianUserId || Status != RepairRequestStatus.InProgress)
        {
            return false;
        }

        ClosureNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ClosureRequestedAt = utcNow;
        Status = RepairRequestStatus.ClosureRequested;
        UpdatedAt = utcNow;
        return true;
    }

    public bool ConfirmClosure(Guid clientUserId, DateTimeOffset utcNow)
    {
        if (ClientUserId != clientUserId || Status != RepairRequestStatus.ClosureRequested)
        {
            return false;
        }

        CompletedAt = utcNow;
        Status = RepairRequestStatus.Completed;
        UpdatedAt = utcNow;
        return true;
    }

    public bool Cancel(DateTimeOffset utcNow)
    {
        if (Status != RepairRequestStatus.Open)
        {
            return false;
        }

        CancelledAt = utcNow;
        Status = RepairRequestStatus.Cancelled;
        UpdatedAt = utcNow;
        return true;
    }
}
