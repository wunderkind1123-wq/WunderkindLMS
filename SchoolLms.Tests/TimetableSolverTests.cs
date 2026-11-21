using SchoolLms.Application.Services;

namespace SchoolLms.Tests;

// ===========================================================================
//  Avtomatik dars jadvali — hisoblash yadrosi (TimetableSolver, 2026-11-21). Bazasiz, sof funksiya.
//  Qattiq qoidalar har testda qayta tekshiriladi: ega bir soatda bitta dars, ustoz bir vaqtda bitta
//  joyda, xona bir vaqtda bitta darsda, ustozning "ishlay olmayman" vaqti.
// ===========================================================================

public class TimetableSolverTests
{
    private static readonly int[] Week = [0, 1, 2, 3, 4];

    private static List<SolverPeriod> Periods(int n) =>
        [.. Enumerable.Range(1, n).Select(p => new SolverPeriod(p, 480 + (p - 1) * 50, 525 + (p - 1) * 50))];

    private static void AssertHardRules(SolverInput input, SolverResult r)
    {
        var placed = r.Placed.Select(p => (Unit: input.Units[p.Unit], p.Day, p.Period)).ToList();
        Assert.Empty(placed.GroupBy(x => (x.Unit.Owner, x.Day, x.Period)).Where(g => g.Count() > 1));
        var teacherSlots = placed.SelectMany(x => x.Unit.Parts.Select(part => (part.TeacherId, x.Day, x.Period))).ToList();
        Assert.Empty(teacherSlots.GroupBy(x => x).Where(g => g.Count() > 1));
        var roomSlots = placed.SelectMany(x => x.Unit.Parts.Where(p => p.RoomId is not null).Select(part => (part.RoomId, x.Day, x.Period))).ToList();
        Assert.Empty(roomSlots.GroupBy(x => x).Where(g => g.Count() > 1));
        foreach (var (unit, day, period) in placed)
            foreach (var part in unit.Parts)
                if (input.Unavailable.TryGetValue(part.TeacherId, out var off))
                    Assert.False(off.Contains((day, 0)) || off.Contains((day, period)), $"{part.TeacherId} {day}/{period}");
    }

    private static List<SolverUnit> Hours(int owner, string subject, string teacher, int hours, Guid? room = null) =>
        [.. Enumerable.Range(0, hours).Select(_ => new SolverUnit(owner, subject, [new SolverPart(0, teacher, room)]))];

    [Fact]
    public void Ikki_sinf_bitta_ustoz_bilan_toqnashmaydi_va_hammasi_joylashadi()
    {
        var input = new SolverInput
        {
            Owners = [new("a", "5-A", Periods(6), null), new("b", "5-B", Periods(6), null)],
            Units =
            [
                .. Hours(0, "math", "T1", 5), .. Hours(1, "math", "T1", 5),
                .. Hours(0, "eng", "T2", 4), .. Hours(1, "eng", "T2", 4),
                .. Hours(0, "bio", "T3", 3), .. Hours(1, "bio", "T4", 3),
            ],
            Days = Week,
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        Assert.Equal(input.Units.Count, r.Placed.Count);
        AssertHardRules(input, r);
        Assert.Equal(0, r.OwnerGaps);
        // Haftasiga 5 soat, 5 kun — har kuni bittadan.
        Assert.All(r.Placed.Where(p => input.Units[p.Unit].SubjectId == "math").GroupBy(p => (input.Units[p.Unit].Owner, p.Day)),
            g => Assert.Single(g));
    }

    [Fact]
    public void Ustozning_ishlay_olmaydigan_kuni_va_soati_hurmat_qilinadi()
    {
        var input = new SolverInput
        {
            Owners = [new("a", "5-A", Periods(5), null)],
            Units = [.. Hours(0, "math", "T1", 4), .. Hours(0, "eng", "T2", 4)],
            Days = Week,
            Unavailable = new(StringComparer.Ordinal) { ["T1"] = [(0, 0), (1, 0)], ["T2"] = [(2, 1), (3, 1)] },
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        AssertHardRules(input, r);
    }

    [Fact]
    public void Bolingan_darsning_ikki_yarmi_bir_vaqtda_turli_ustozlarda()
    {
        var input = new SolverInput
        {
            Owners = [new("t", "Iqtisodiyot", Periods(6), null)],
            Units =
            [
                .. Enumerable.Range(0, 3).Select(_ => new SolverUnit(0, "eng", [new SolverPart(1, "T1", null), new SolverPart(2, "T2", null)])),
                .. Hours(0, "math", "T3", 4),
            ],
            Days = Week,
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        AssertHardRules(input, r);
    }

    [Fact]
    public void Boshqa_egadagi_band_ustoz_va_xona_hamda_blok_soati_hisobga_olinadi()
    {
        var gym = Guid.NewGuid();
        var input = new SolverInput
        {
            Owners = [new("a", "5-A", Periods(4), null)],
            Units = [.. Hours(0, "pe", "T1", 2, gym), .. Hours(0, "math", "T2", 3)],
            Days = [0, 1],
            TeacherBusy = new(StringComparer.Ordinal) { ["T1"] = [new SolverBusy(0, 1, 480, 525), new SolverBusy(0, 2, 530, 575)] },
            RoomBusy = new() { [gym] = [new SolverBusy(1, 1, 480, 525)] },
            OwnerBlocked = new() { [0] = [(1, 4)] },
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        AssertHardRules(input, r);
        foreach (var p in r.Placed.Where(p => input.Units[p.Unit].SubjectId == "pe"))
        {
            Assert.False(p.Day == 0 && p.Period <= 2);
            Assert.False(p.Day == 1 && p.Period == 1);
        }
        Assert.DoesNotContain(r.Placed, p => p.Day == 1 && p.Period == 4);
    }

    [Fact]
    public void Sigmaydigan_darslar_joylashmaganlar_royxatiga_tushadi()
    {
        var input = new SolverInput
        {
            Owners = [new("a", "5-A", Periods(2), null)],
            Units = [.. Hours(0, "math", "T1", 5)],
            Days = [0, 1],
        };
        var r = TimetableSolver.Solve(input);
        Assert.Equal(4, r.Placed.Count);
        Assert.Single(r.Unplaced);
        AssertHardRules(input, r);
    }

    [Fact]
    public void Turli_qongiroq_jadvallarida_vaqt_kesishuvi_bilan_tekshiriladi()
    {
        // 5-A 2-soati 09:00–09:45, 1-A 2-soati 08:50–09:30 — kesishadi; T1 ikkalasida ham.
        var a = new List<SolverPeriod> { new(1, 480, 525), new(2, 540, 585) };
        var b = new List<SolverPeriod> { new(1, 480, 520), new(2, 530, 570) };
        var input = new SolverInput
        {
            Owners = [new("a", "5-A", a, null), new("b", "1-A", b, null)],
            Units = [.. Hours(0, "math", "T1", 2), .. Hours(1, "math", "T1", 2)],
            Days = [0, 1],
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        var placed = r.Placed.Select(p => (input.Units[p.Unit].Owner, p.Day, p.Period)).ToList();
        foreach (var day in new[] { 0, 1 })
        {
            var mine = placed.Where(x => x.Day == day).ToList();
            foreach (var x in mine.Where(x => x.Owner == 0))
                foreach (var y in mine.Where(y => y.Owner == 1))
                {
                    var (pa, pb) = (a[x.Period - 1], b[y.Period - 1]);
                    Assert.False(TimetableSolver.Overlaps(pa.Start, pa.End, pa.Period, pb.Start, pb.End, pb.Period));
                }
        }
    }

    [Fact]
    public void Har_sinf_oz_oquv_kunlarida_1_3_sinf_shanbasiz()
    {
        // 1-A — 5 kun (Du–Ju), 5-A — 6 kun; ustoz ikkalasida ham.
        var input = new SolverInput
        {
            Owners =
            [
                new("a", "1-A", Periods(5), null, [0, 1, 2, 3, 4]),
                new("b", "5-A", Periods(5), null, [0, 1, 2, 3, 4, 5]),
            ],
            Units = [.. Hours(0, "math", "T1", 5), .. Hours(0, "eng", "T2", 5), .. Hours(1, "math", "T1", 6), .. Hours(1, "bio", "T3", 6)],
        };
        var r = TimetableSolver.Solve(input);
        Assert.Empty(r.Unplaced);
        AssertHardRules(input, r);
        Assert.DoesNotContain(r.Placed, p => input.Units[p.Unit].Owner == 0 && p.Day == 5);
        Assert.Contains(r.Placed, p => input.Units[p.Unit].Owner == 1 && p.Day == 5);
    }
}
