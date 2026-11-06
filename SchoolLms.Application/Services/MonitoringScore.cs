using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Monitoring test scoring (docs/modules/curriculum-monitoring.md §2) — pure, no database.
///
/// <list type="bullet">
///   <item><b>Simple</b>: correct count and percent.</item>
///   <item><b>National, weights</b>: 75 × (weights of correct items) / (all weights).</item>
///   <item><b>National, Rasch</b> (client's choice, 2026-11-06): abilities θ by joint maximum
///   likelihood on this test's answers, standardised among its takers (Z), T = 50 + 10·Z,
///   clamped to 0–75. One taker, or all takers equal → 50.</item>
/// </list>
/// Grade bands (0–75): A+ ≥ 70, A ≥ 65, B+ ≥ 60, B ≥ 55, C+ ≥ 50, C ≥ 46, below — none.
/// </summary>
public static class MonitoringScore
{
    public const decimal MaxPoints = 75m;

    public static readonly (decimal Min, string Level)[] Bands =
        [(70m, "A+"), (65m, "A"), (60m, "B+"), (55m, "B"), (50m, "C+"), (46m, "C")];

    public static string? LevelOf(decimal points) => Bands.FirstOrDefault(b => points >= b.Min).Level;

    public static char Letter(int index) => (char)('A' + index);

    /// <summary>"ABD-…" → per item: true (correct), false (wrong or blank).</summary>
    public static bool[] Marks(string? answers, IReadOnlyList<MonitoringItem> items)
    {
        var result = new bool[items.Count];
        if (answers is null) return result;
        for (var i = 0; i < items.Count && i < answers.Length; i++)
            result[i] = char.ToUpperInvariant(answers[i]) == Letter(items[i].CorrectIndex);
        return result;
    }

    /// <summary>
    /// Normalises an entered answer string: letters within each item's options, anything else
    /// (space, '-', '.', '0') a blank '-'. Null when it is longer than the test.
    /// </summary>
    public static string? Normalize(string? raw, IReadOnlyList<MonitoringItem> items)
    {
        var text = (raw ?? "").Trim().ToUpperInvariant();
        if (text.Length > items.Count) return null;
        var chars = new char[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var c = i < text.Length ? text[i] : '-';
            chars[i] = c >= 'A' && c < Letter(items[i].OptionCount) ? c : '-';
        }
        return new string(chars);
    }

    /// <summary>Scores every DONE participant of the test in place.</summary>
    public static void Apply(MonitoringTest test, IReadOnlyList<MonitoringItem> items, IReadOnlyList<MonitoringParticipant> participants)
    {
        var done = participants.Where(p => p.Status == MonitoringParticipantStatus.Done).ToList();
        foreach (var p in participants.Where(p => p.Status != MonitoringParticipantStatus.Done))
        {
            p.Correct = 0;
            p.Percent = 0;
            p.Points = null;
            p.Level = null;
        }
        if (items.Count == 0) return;

        var marks = done.Select(p => Marks(p.Answers, items)).ToList();
        for (var i = 0; i < done.Count; i++)
        {
            done[i].Correct = marks[i].Count(x => x);
            done[i].Percent = Math.Round(done[i].Correct * 100m / items.Count, 2);
        }

        if (test.Scoring != MonitoringScoring.National)
        {
            foreach (var p in done) { p.Points = null; p.Level = null; }
            return;
        }

        decimal[] points;
        if (test.NationalMethod == MonitoringNationalMethod.Weights)
        {
            var total = items.Sum(i => i.Weight);
            points = [.. marks.Select(m => total <= 0 ? 0m
                : MaxPoints * items.Where((_, k) => m[k]).Sum(i => i.Weight) / total)];
        }
        else
        {
            points = [.. RaschT(marks).Select(t => (decimal)t)];
        }
        for (var i = 0; i < done.Count; i++)
        {
            var value = Math.Round(Math.Clamp(points[i], 0m, MaxPoints), 1, MidpointRounding.AwayFromZero);
            done[i].Points = value;
            done[i].Level = LevelOf(value);
        }
    }

    /// <summary>
    /// T = 50 + 10·Z of each person's Rasch ability. Joint maximum likelihood (JMLE): extreme
    /// persons (all right / all wrong) get their raw score pulled 0.3 inside; items everyone
    /// answered alike carry no information and are left out.
    /// </summary>
    public static double[] RaschT(IReadOnlyList<bool[]> responses)
    {
        var n = responses.Count;
        if (n == 0) return [];
        if (n == 1) return [50];
        var itemCount = responses[0].Length;
        var useful = Enumerable.Range(0, itemCount)
            .Where(k => { var s = responses.Count(r => r[k]); return s > 0 && s < n; }).ToArray();
        var l = useful.Length;
        if (l == 0) return [.. Enumerable.Repeat(50.0, n)];

        var raw = responses.Select(r => useful.Count(k => r[k])).ToArray();
        var adjusted = raw.Select(r => r == 0 ? 0.3 : r == l ? l - 0.3 : r).ToArray();
        var itemScore = useful.Select(k => responses.Count(r => r[k])).ToArray();

        var theta = adjusted.Select(r => Math.Log(r / (l - r))).ToArray();
        var b = itemScore.Select(s => Math.Log((double)(n - s) / s)).ToArray();
        Center(b);

        for (var iter = 0; iter < 200; iter++)
        {
            var maxChange = 0.0;
            for (var p = 0; p < n; p++)
            {
                double expected = 0, info = 0;
                for (var k = 0; k < l; k++)
                {
                    var pr = 1 / (1 + Math.Exp(b[k] - theta[p]));
                    expected += pr;
                    info += pr * (1 - pr);
                }
                var step = Math.Clamp((adjusted[p] - expected) / Math.Max(info, 1e-9), -1, 1);
                theta[p] += step;
                maxChange = Math.Max(maxChange, Math.Abs(step));
            }
            for (var k = 0; k < l; k++)
            {
                double expected = 0, info = 0;
                for (var p = 0; p < n; p++)
                {
                    var pr = 1 / (1 + Math.Exp(b[k] - theta[p]));
                    expected += pr;
                    info += pr * (1 - pr);
                }
                var step = Math.Clamp((itemScore[k] - expected) / Math.Max(info, 1e-9), -1, 1);
                b[k] -= step;
                maxChange = Math.Max(maxChange, Math.Abs(step));
            }
            Center(b);
            if (maxChange < 1e-4) break;
        }

        var mean = theta.Average();
        var sd = Math.Sqrt(theta.Sum(t => (t - mean) * (t - mean)) / n);
        return sd < 1e-9 ? [.. Enumerable.Repeat(50.0, n)] : [.. theta.Select(t => 50 + 10 * (t - mean) / sd)];
    }

    private static void Center(double[] values)
    {
        var mean = values.Average();
        for (var i = 0; i < values.Length; i++) values[i] -= mean;
    }
}
