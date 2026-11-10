namespace SchoolLms.Domain;

// ===========================================================================
//  O'quvchilar leaderboardi (client, 2026-11-08): one ranking for the whole academic year, built from
//  the journal marks, attendance, the monthly tests, the gamification (behaviour) score — and points
//  staff add by hand with a reason. Shown for the whole school, per class and per track group.
//
//  Only the hand-added points are stored here; everything else is read from where it already lives
//  (LeaderboardService). The reasons are a list staff keep themselves ("sabablarni qo'lda qo'shish").
// ===========================================================================

/// <summary>A reason staff can give leaderboard points for ("Olimpiada g'olibi", +10).</summary>
public class LeaderboardReason
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>The points an award with this reason gets by default; may be negative.</summary>
    public int Points { get; set; }

    /// <summary>false — not offered for new awards; the awards already given stay.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Points one pupil got by hand. The reason's name and points are copied, so editing it never rewrites history.</summary>
public class LeaderboardAward
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="Student.Id"/>; <c>on delete cascade</c>.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>The reason it was given for; null — typed in place, or the reason was deleted.</summary>
    public Guid? ReasonId { get; set; }

    public string ReasonName { get; set; } = string.Empty;

    public int Points { get; set; }

    public string? Note { get; set; }

    /// <summary>The day it counts on — the academic year it falls in is the one it adds to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>users.id of the author.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>The author's name as shown.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
