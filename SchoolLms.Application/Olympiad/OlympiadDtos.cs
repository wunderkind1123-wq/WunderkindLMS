namespace SchoolLms.Application.Olympiad;

public sealed record OlympiadEventInput(
    string Title, string? Description, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string? Location, string? LocationMapUrl, int? Capacity, string? OnFull,
    DateTime? RegistrationOpensAt, DateTime? RegistrationClosesAt, decimal MaxScore,
    bool ShowScore, bool ShowRank, bool ShowCertificate, bool ShowInvite,
    string? RulesText, string? RulesFileUrl, string? RulesFileName, string? RulesDisplay);

public sealed record OlympiadEventDto(
    Guid Id, int Code, string Title, string Description, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    DateTime StartsAt, string Location, string LocationMapUrl, int? Capacity, string OnFull,
    DateTime? RegistrationOpensAt, DateTime? RegistrationClosesAt, string Status, decimal MaxScore,
    bool ShowScore, bool ShowRank, bool ShowCertificate, bool ShowInvite,
    string RulesText, string RulesFileUrl, string RulesFileName, string RulesDisplay, int RulesVersion,
    int Registered, int Waitlist, int Attended, int NoShow, int Cancelled, int WithScore,
    bool RegistrationOpen, DateTime? CheckinClosedAt, DateTime? ResultsSentAt, string DeepLink, DateTime CreatedAt);

public sealed record OlympiadRegistrationDto(
    Guid Id, Guid EventId, Guid ParticipantId, string Number, int NumberValue, string Status, int WaitlistPosition,
    string FullName, string Phone, int Grade, string School, string ParentName, string ParentUsername,
    bool HasTelegram, DateTime CreatedAt, DateTime? RulesAcceptedAt, int RulesVersion,
    DateTime? CheckedInAt, string CheckedInBy, string CheckinMethod,
    decimal? Score, decimal? Percent, int? Rank, DateTime? ScoreUpdatedAt, string ScoreUpdatedBy, string ScoreSource,
    DateTime? ResultSentAt, bool ResultChanged, string? LeadId, string CreatedVia);

public sealed record OlympiadParticipantInput(string FullName, string Phone, int Grade, string School);

public sealed record OlympiadManualRegistrationInput(Guid? ParticipantId, OlympiadParticipantInput? Participant);

public sealed record OlympiadParticipantDto(
    Guid Id, string FullName, string Phone, int Grade, string School, string ParentName, string ParentUsername,
    bool HasTelegram, int Events, int Attended, decimal? BestPercent, string? LeadId, DateTime? LeadConvertedAt, DateTime CreatedAt);

public sealed record OlympiadParticipantCardDto(OlympiadParticipantDto Participant, List<OlympiadParticipantRegDto> Registrations);

public sealed record OlympiadParticipantRegDto(
    Guid RegistrationId, Guid EventId, string EventTitle, DateOnly Date, string Number, string Status,
    decimal? Score, decimal MaxScore, decimal? Percent, int? Rank);

public sealed record ScoreInput(decimal? Score);

public sealed record ImportConfirmInput(List<ImportScore> Rows);
public sealed record ImportScore(Guid RegistrationId, decimal Score);

public sealed record OlympiadReminderInput(
    int? OffsetMinutes, DateTime? SendAt, string? Text, List<OlympiadAttachment>? Attachments, string? Target);

public sealed record OlympiadReminderDto(
    Guid Id, string Kind, int? OffsetMinutes, DateTime? SendAt, DateTime? DueAt, string Text,
    List<OlympiadAttachment> Attachments, string Target, string Status, DateTime? SentAt, int SentCount);

public sealed record OlympiadBroadcastInput(
    string? Title, string? Text, List<OlympiadAttachment>? Attachments, List<OlympiadButton>? Buttons, OlympiadAudience? Audience);

public sealed record OlympiadBroadcastDto(
    Guid Id, string Title, string Text, List<OlympiadAttachment> Attachments, List<OlympiadButton> Buttons,
    OlympiadAudience Audience, DateTime? ScheduledAt, string Status, int Total, int Sent, int Failed, int Blocked, int Pending,
    string CreatedBy, DateTime CreatedAt, DateTime? StartedAt, DateTime? FinishedAt);

public sealed record BroadcastSendInput(DateTime? ScheduledAt);

public sealed record TestMessageInput(string? Text, List<OlympiadAttachment>? Attachments, List<OlympiadButton>? Buttons);

public sealed record OlympiadSettingsDto(
    bool BotConfigured, string BotTokenMasked, string BotUsername, string BotName,
    int GradeFrom, int GradeTo, string DefaultOnFull,
    string RulesText, string RulesFileUrl, string RulesFileName, string RulesDisplay, int RulesVersion,
    bool Reminder1Enabled, int Reminder1Minutes, bool Reminder2Enabled, int Reminder2Minutes,
    bool ResultShowScore, bool ResultShowRank, bool ResultShowCertificate, bool ResultShowInvite,
    string InviteText, string InviteUrl, string CertificateFormat, string AdminLink, DateTime? UpdatedAt,
    string AdminPhones = "");

/// <param name="AdminPhones">Bot adminlari telefonlari (qator yoki vergul bilan). null — o'zgarmaydi.</param>
public sealed record OlympiadSettingsInput(
    int GradeFrom, int GradeTo, string? DefaultOnFull,
    string? RulesText, string? RulesFileUrl, string? RulesFileName, string? RulesDisplay,
    bool Reminder1Enabled, int Reminder1Minutes, bool Reminder2Enabled, int Reminder2Minutes,
    bool ResultShowScore, bool ResultShowRank, bool ResultShowCertificate, bool ResultShowInvite,
    string? InviteText, string? InviteUrl, string? CertificateFormat, string? AdminLink,
    string? AdminPhones = null);

public sealed record BotTokenInput(string? Token);

public sealed record OlympiadChannelInput(string Title, string ChatRef, string? InviteLink, int SortOrder, bool IsActive);

public sealed record OlympiadChannelDto(Guid Id, string Title, string ChatRef, string InviteLink, int SortOrder, bool IsActive, string LastError);

public sealed record OlympiadTemplateDto(string Key, string Title, string Tokens, string Text, string DefaultText, bool Customized,
    List<OlympiadAttachment> Attachments);

public sealed record TemplateInput(string? Text, List<OlympiadAttachment>? Attachments = null);

public sealed record ConvertLeadsInput(List<Guid> ParticipantIds);

public sealed record ConvertLeadsResult(int Created, int Linked, int Skipped, List<string> Messages);

public sealed record CheckinScanInput(Guid EventId, string Token);

public sealed record CheckinResultDto(string Outcome, string Message, OlympiadRegistrationDto? Registration);

public sealed record CheckinSummaryDto(int Registered, int Attended, int NotYet);
