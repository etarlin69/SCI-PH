using System.ComponentModel.DataAnnotations;
using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.DTOs.Security
{
    // The patient's email comes from the patient record (primary active Email identifier), never from the request.
    public sealed record InvitePatientRequest([Required] Guid PatientId);

    // Role must be one of SecurityRoles.InvitableStaff; Administrator cannot be granted through an invitation.
    public sealed record InviteStaffRequest(
        [Required] Guid ClinicalStaffId,
        [Required, EmailAddress, MaxLength(256)] string Email,
        [Required, MaxLength(64)] string Role);

    // The client presents only the token: subject, email and role come from the stored invitation.
    public sealed record ActivateAccountRequest(
        [Required, MaxLength(512)] string Token,
        [Required, MaxLength(256)] string Password);

    public sealed record InvitationSummary(
        Guid Id,
        InvitationSubjectType SubjectType,
        Guid SubjectId,
        string Email,
        string IntendedRole,
        DateTimeOffset ExpiresAtUtc,
        bool EmailSent);

    public enum InvitationOutcome
    {
        Created = 1,
        SubjectNotFound = 2,
        SubjectAlreadyLinked = 3,
        SubjectHasNoEmail = 4,
        InvalidRole = 5,
        EmailInUse = 6,
        InvitationNotFound = 7,
        InvitationNotPending = 8
    }

    public sealed record InvitationResult(InvitationOutcome Outcome, InvitationSummary? Invitation)
    {
        public static InvitationResult Failed(InvitationOutcome outcome) => new(outcome, null);
    }

    public sealed record AccountActivationResult(bool Succeeded, IReadOnlyList<string> Errors)
    {
        public static AccountActivationResult Success { get; } = new(true, []);

        // Generic on purpose: invalid, expired, used and revoked invitations look the same to the caller.
        public static AccountActivationResult InvalidInvitation { get; } = new(false, []);

        public static AccountActivationResult PasswordRejected(IReadOnlyList<string> errors) => new(false, errors);
    }

    public sealed record NewAccountInvitation(
        InvitationSubjectType SubjectType,
        Guid SubjectId,
        string Email,
        string IntendedRole,
        string TokenHash,
        DateTimeOffset ExpiresAtUtc,
        Guid? CreatedByUserId);

    public sealed record StoredAccountInvitation(
        Guid Id,
        InvitationSubjectType SubjectType,
        Guid SubjectId,
        string Email,
        string IntendedRole,
        AccountInvitationStatus Status,
        DateTimeOffset ExpiresAtUtc);

    // Clinical record seen from Identity: existence, current account link and contact email. No clinical data.
    public sealed record ClinicalSubject(
        InvitationSubjectType Type,
        Guid Id,
        Guid? IdentityUserId,
        string? ContactEmail,
        bool IsActive);
}
