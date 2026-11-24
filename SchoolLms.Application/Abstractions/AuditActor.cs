namespace SchoolLms.Application.Abstractions;

/// <summary>
/// The signed-in user of the current request, for audit rows written where no controller passes the
/// actor down (attendance changes are audited inside SaveChanges — AttendanceAudit). Set by a
/// middleware after authentication; background jobs leave it empty and are recorded as "Tizim".
/// </summary>
public static class AuditActor
{
    private static readonly AsyncLocal<(string? Id, string? Name)?> current = new();

    public static (string? Id, string? Name)? Current => current.Value;

    public static void Set(string? id, string? name) => current.Value = id is null ? null : (id, name);
}
