namespace SchoolLms.Application.Services;

// ===========================================================================
//  Avtomatik dars jadvali — hisoblash yadrosi (mijoz, 2026-11-21, 7-band). Bazaga tegmaydi: kirish
//  (egalar, soatlar, darslar, band vaqtlar) → har darsning (kun, soat) joyi. Ma'lumotni yig'ish va
//  natijani shablonga yozish — <see cref="TimetableGenerator"/>.
//
//  QATTIQ QOIDALAR (hech qachon buzilmaydi):
//    * egada bir soatda bitta dars (bo'lingan dars — ikki yarmi BIR soatda);
//    * o'qituvchi bir vaqtda bitta joyda — vaqtlar ma'lum bo'lsa vaqt KESISHUVI, aks holda soat
//      raqami (qo'ng'iroq jadvallari har xil bo'lishi mumkin, ScheduleConflicts bilan bir xil);
//    * xona bir vaqtda bitta darsda;
//    * o'qituvchining "ishlay olmayman" vaqti (qoida yoqilgan bo'lsa);
//    * egadagi blok soatlari va boshqa (generatsiya qilinmayotgan) egalardagi darslar — band.
//
//  YUMSHOQ QOIDALAR (jarima, kamaytiriladi):
//    * oynasiz (qoida yoqilgan bo'lsa) — sinf kunida darslar orasida bo'sh soat qolmasin;
//    * bir fan kuniga ko'pi bilan ⌈soat/kunlar⌉ marta;
//    * kunlar bo'yicha teng taqsimot, o'qituvchi oynalari kam.
//
//  Usul: qiyinlik tartibida ochko'z joylashtirish + "siqib chiqarish" (bir darsni boshqa joyga
//  ko'chirib o'rin ochish) + oynalarni yopish; tasodifiy tartib bilan bir necha urinish, eng
//  yaxshisi olinadi. Qoidalar keyin qo'shilishi uchun jarimalar <see cref="Score"/> da bir joyda.
// ===========================================================================

/// <summary>Egadagi bitta dars soati: raqami va (ma'lum bo'lsa) vaqti daqiqalarda.</summary>
public readonly record struct SolverPeriod(int Period, int? Start, int? End);

/// <summary>Jadvali tuziladigan ega (sinf yoki guruh).</summary>
/// <param name="Days">Eganing o'quv kunlari (0 = Dushanba); null — <see cref="SolverInput.Days"/>.</param>
public sealed record SolverOwner(
    string Id, string Name, IReadOnlyList<SolverPeriod> Periods, Guid? HomeRoom, IReadOnlyList<int>? Days = null);

/// <summary>Bitta dars qismi: butun sinf (SubGroup 0) yoki yarmi (1/2) — o'z ustozi va xonasi bilan.</summary>
public sealed record SolverPart(int SubGroup, string TeacherId, Guid? RoomId);

/// <summary>Joylashtiriladigan bitta dars (haftadagi bir soat). Bo'lingan darsda ikki qism bir vaqtda.</summary>
public sealed record SolverUnit(int Owner, string SubjectId, IReadOnlyList<SolverPart> Parts);

/// <summary>Boshqa joydagi o'zgarmas bandlik: kun, soat va vaqt.</summary>
public readonly record struct SolverBusy(int Day, int Period, int? Start, int? End);

public sealed class SolverInput
{
    public required IReadOnlyList<SolverOwner> Owners { get; init; }
    public required IReadOnlyList<SolverUnit> Units { get; init; }
    /// <summary>Sukut o'quv kunlari — egada o'zi berilmagan bo'lsa.</summary>
    public IReadOnlyList<int> Days { get; init; } = [0, 1, 2, 3, 4, 5];
    /// <summary>O'qituvchi → boshqa egalardagi (generatsiyaga kirmagan) darslari.</summary>
    public Dictionary<string, List<SolverBusy>> TeacherBusy { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Xona → boshqa egalardagi bandligi.</summary>
    public Dictionary<Guid, List<SolverBusy>> RoomBusy { get; init; } = [];
    /// <summary>Ega indeksi → band (kun, soat), masalan blok soatlari.</summary>
    public Dictionary<int, HashSet<(int Day, int Period)>> OwnerBlocked { get; init; } = [];
    /// <summary>O'qituvchi → ishlay olmaydigan (kun, soat); soat 0 — butun kun.</summary>
    public Dictionary<string, HashSet<(int Day, int Period)>> Unavailable { get; init; } = new(StringComparer.Ordinal);
    public bool NoGaps { get; init; } = true;
    public int Seed { get; init; } = 1;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(8);
    public int MaxAttempts { get; init; } = 60;
}

/// <summary>Joylashgan dars: qaysi unit, qaysi kun va soat.</summary>
public readonly record struct SolverPlacement(int Unit, int Day, int Period);

public sealed record SolverResult(
    List<SolverPlacement> Placed, List<int> Unplaced, int OwnerGaps, int TeacherGaps, int SubjectRepeats, int Attempts);

public static class TimetableSolver
{
    public static SolverResult Solve(SolverInput input)
    {
        var started = DateTime.UtcNow;
        SolverResult? best = null;
        var attempts = 0;
        for (var i = 0; i < input.MaxAttempts; i++)
        {
            if (i > 0 && DateTime.UtcNow - started > input.TimeLimit) break;
            attempts++;
            var r = new Attempt(input, new Random(input.Seed * 7919 + i)).Run();
            if (best is null || Better(r, best)) best = r;
            if (best.Unplaced.Count == 0 && best.OwnerGaps == 0 && best.SubjectRepeats == 0 && best.TeacherGaps == 0) break;
        }
        return best! with { Attempts = attempts };
    }

    private static bool Better(SolverResult a, SolverResult b) =>
        (a.Unplaced.Count, a.OwnerGaps, a.SubjectRepeats, a.TeacherGaps).CompareTo(
            (b.Unplaced.Count, b.OwnerGaps, b.SubjectRepeats, b.TeacherGaps)) < 0;

    /// <summary>Ikki vaqt kesishadimi: ikkalasining vaqti ma'lum bo'lsa — oraliq, aks holda soat raqami.</summary>
    public static bool Overlaps(int? s1, int? e1, int p1, int? s2, int? e2, int p2) =>
        s1 is { } a && e1 is { } b && s2 is { } c && e2 is { } d ? a < d && c < b : p1 == p2;

    private sealed class Attempt
    {
        private readonly SolverInput _in;
        private readonly Random _rng;
        /// <summary>[ega][kun] → soat → unit.</summary>
        private readonly Dictionary<int, int>[][] _grid;
        private readonly Dictionary<string, List<(SolverBusy Busy, int Unit)>> _teacher = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, List<(SolverBusy Busy, int Unit)>> _room = [];
        private readonly (int Day, int Period)?[] _at;
        /// <summary>(ega, fan) → haftalik soat — "kuniga ko'pi bilan" chegarasi uchun.</summary>
        private readonly Dictionary<(int, string), int> _weekly = [];

        public Attempt(SolverInput input, Random rng)
        {
            _in = input;
            _rng = rng;
            _grid = [.. input.Owners.Select(_ => Enumerable.Range(0, 7).Select(_ => new Dictionary<int, int>()).ToArray())];
            _at = new (int, int)?[input.Units.Count];
            foreach (var (t, list) in input.TeacherBusy) _teacher[t] = [.. list.Select(b => (b, -1))];
            foreach (var (r, list) in input.RoomBusy) _room[r] = [.. list.Select(b => (b, -1))];
            foreach (var u in input.Units)
                _weekly[(u.Owner, u.SubjectId)] = _weekly.GetValueOrDefault((u.Owner, u.SubjectId)) + 1;
        }

        public SolverResult Run()
        {
            var teacherLoad = _in.Units.SelectMany(u => u.Parts.Select(p => p.TeacherId))
                .GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var ownerLoad = _in.Units.GroupBy(u => u.Owner).ToDictionary(g => g.Key, g => g.Count());
            var order = Enumerable.Range(0, _in.Units.Count)
                .Select(i => (i, key: Difficulty(_in.Units[i], teacherLoad, ownerLoad) + _rng.NextDouble() * 6))
                .OrderByDescending(x => x.key).Select(x => x.i).ToList();

            var unplaced = new List<int>();
            foreach (var u in order)
                if (!PlaceBest(u) && !Kick(u)) unplaced.Add(u);

            // Ikkinchi imkon: siqib chiqarish bilan ochilgan joylarga.
            unplaced = [.. unplaced.Where(u => !PlaceBest(u) && !Kick(u))];

            if (_in.NoGaps) Compact();

            var placed = new List<SolverPlacement>();
            for (var i = 0; i < _at.Length; i++)
                if (_at[i] is { } at) placed.Add(new SolverPlacement(i, at.Day, at.Period));
            return new SolverResult(placed, unplaced, TotalOwnerGaps(), TotalTeacherGaps(), TotalRepeats(), 1);
        }

        private double Difficulty(SolverUnit u, Dictionary<string, int> teacherLoad, Dictionary<int, int> ownerLoad)
        {
            var slots = _in.Owners[u.Owner].Periods.Count * DaysOf(u.Owner).Count;
            var tight = slots == 0 ? 100 : 30.0 * ownerLoad[u.Owner] / slots;
            var teachers = u.Parts.Sum(p => teacherLoad.GetValueOrDefault(p.TeacherId)
                + (_in.Unavailable.GetValueOrDefault(p.TeacherId)?.Count ?? 0) * 2);
            return teachers + tight + (u.Parts.Count > 1 ? 15 : 0) + (u.Parts.Any(p => p.RoomId is not null) ? 8 : 0);
        }

        // ---------------------------------------------------------------- feasibility

        private SolverPeriod PeriodOf(int owner, int period) =>
            _in.Owners[owner].Periods.First(p => p.Period == period);

        private Guid? RoomOf(SolverUnit u, SolverPart p) => p.RoomId ?? (u.Parts.Count == 1 ? _in.Owners[u.Owner].HomeRoom : null);

        /// <summary>Unit shu joyga sig'adimi (o'zi hisobga olinmaydi). <paramref name="ignore"/> — vaqtincha olib tashlangan unit.</summary>
        private bool Fits(int u, int day, int period, int ignore = -2)
        {
            var unit = _in.Units[u];
            var di = day;
            if (_grid[unit.Owner][di].TryGetValue(period, out var occ) && occ != u && occ != ignore) return false;
            if (_in.OwnerBlocked.TryGetValue(unit.Owner, out var blocked) && blocked.Contains((day, period))) return false;
            var sp = PeriodOf(unit.Owner, period);
            foreach (var part in unit.Parts)
            {
                if (_in.Unavailable.TryGetValue(part.TeacherId, out var off)
                    && (off.Contains((day, 0)) || off.Contains((day, period)))) return false;
                if (Clashes(_teacher.GetValueOrDefault(part.TeacherId), day, sp, u, ignore)) return false;
                if (RoomOf(unit, part) is { } room && Clashes(_room.GetValueOrDefault(room), day, sp, u, ignore)) return false;
            }
            // Bo'lingan darsning ikki yarmi bitta ustoz yoki bitta xonada bo'lolmaydi.
            if (unit.Parts.Count > 1)
            {
                if (unit.Parts.Select(p => p.TeacherId).Distinct(StringComparer.Ordinal).Count() < unit.Parts.Count) return false;
                var rooms = unit.Parts.Select(p => p.RoomId).OfType<Guid>().ToList();
                if (rooms.Distinct().Count() < rooms.Count) return false;
            }
            return true;
        }

        private static bool Clashes(List<(SolverBusy Busy, int Unit)>? list, int day, SolverPeriod sp, int self, int ignore)
        {
            if (list is null) return false;
            foreach (var (b, unit) in list)
                if (unit != self && unit != ignore && b.Day == day && Overlaps(b.Start, b.End, b.Period, sp.Start, sp.End, sp.Period))
                    return true;
            return false;
        }

        private void Put(int u, int day, int period)
        {
            var unit = _in.Units[u];
            _grid[unit.Owner][day][period] = u;
            _at[u] = (day, period);
            var sp = PeriodOf(unit.Owner, period);
            var busy = new SolverBusy(day, period, sp.Start, sp.End);
            foreach (var part in unit.Parts)
            {
                if (!_teacher.TryGetValue(part.TeacherId, out var tl)) _teacher[part.TeacherId] = tl = [];
                tl.Add((busy, u));
                if (RoomOf(unit, part) is { } room)
                {
                    if (!_room.TryGetValue(room, out var rl)) _room[room] = rl = [];
                    rl.Add((busy, u));
                }
            }
        }

        private void Remove(int u)
        {
            if (_at[u] is not { } at) return;
            var unit = _in.Units[u];
            _grid[unit.Owner][at.Day].Remove(at.Period);
            _at[u] = null;
            foreach (var part in unit.Parts)
            {
                _teacher.GetValueOrDefault(part.TeacherId)?.RemoveAll(x => x.Unit == u);
                if (RoomOf(unit, part) is { } room) _room.GetValueOrDefault(room)?.RemoveAll(x => x.Unit == u);
            }
        }

        // ---------------------------------------------------------------- scoring

        /// <summary>Eganing o'quv kunlari (1–3-sinf 5 kun, qolganlari 6 kun — sozlamadan).</summary>
        private IReadOnlyList<int> DaysOf(int owner) => _in.Owners[owner].Days ?? _in.Days;

        private IEnumerable<(int Day, int Period)> Slots(int owner) =>
            DaysOf(owner).SelectMany(d => _in.Owners[owner].Periods.Select(p => (d, p.Period)));

        /// <summary>Unitni (kun, soat) ga qo'yishning jarimasi — kichigi yaxshi. Qoidalar shu yerda.</summary>
        private double Score(int u, int day, int period)
        {
            var unit = _in.Units[u];
            var di = day;
            var cells = _grid[unit.Owner][di];
            double s = 0;

            // Bir fan kuniga ko'pi bilan ⌈soat / kunlar⌉ marta.
            var cap = (int)Math.Ceiling(_weekly[(unit.Owner, unit.SubjectId)] / (double)DaysOf(unit.Owner).Count);
            var same = cells.Values.Count(x => x != u && _in.Units[x].SubjectId == unit.SubjectId);
            s += same >= cap ? 400 : same * 25;

            // Kunlar bo'yicha teng taqsimot.
            s += cells.Count * 6;

            // Oynasiz: shu joydan keyin egada nechta bo'sh soat qoladi; ertaroq soat afzal.
            var periods = _in.Owners[unit.Owner].Periods;
            if (_in.NoGaps)
            {
                s += Gaps(periods, cells.Keys.Append(period)) * 80;
                s += periods.ToList().FindIndex(p => p.Period == period) * 3;
            }

            // O'qituvchi oynalari.
            foreach (var part in unit.Parts)
                s += TeacherGaps(part.TeacherId, day, period) * 4;

            return s + _rng.NextDouble();
        }

        private static int Gaps(IReadOnlyList<SolverPeriod> periods, IEnumerable<int> used)
        {
            var idx = used.Select(p => periods.ToList().FindIndex(x => x.Period == p)).Where(i => i >= 0).Distinct().ToList();
            return idx.Count < 2 ? 0 : idx.Max() - idx.Min() + 1 - idx.Count;
        }

        private int TeacherGaps(string teacher, int day, int? extra = null)
        {
            var ps = (_teacher.GetValueOrDefault(teacher) ?? []).Where(x => x.Busy.Day == day).Select(x => x.Busy.Period).ToList();
            if (extra is { } e) ps.Add(e);
            var set = ps.Distinct().ToList();
            return set.Count < 2 ? 0 : set.Max() - set.Min() + 1 - set.Count;
        }

        // ---------------------------------------------------------------- placement

        private bool PlaceBest(int u)
        {
            if (_at[u] is not null) return true;
            (int Day, int Period)? best = null;
            var bestScore = double.MaxValue;
            foreach (var (d, p) in Slots(_in.Units[u].Owner))
            {
                if (!Fits(u, d, p)) continue;
                var sc = Score(u, d, p);
                if (sc < bestScore) { bestScore = sc; best = (d, p); }
            }
            if (best is not { } b) return false;
            Put(u, b.Day, b.Period);
            return true;
        }

        /// <summary>
        /// Joy topilmadi: egadagi band joylardan birini bo'shatib (u yerdagi darsni boshqa joyga
        /// ko'chirib) qo'yishga urinish. Ustoz yoki xonani band qilgan boshqa egadagi darsni ham ko'chiradi.
        /// </summary>
        private bool Kick(int u)
        {
            var unit = _in.Units[u];
            var candidates = Slots(unit.Owner).OrderBy(_ => _rng.Next()).ToList();
            foreach (var (d, p) in candidates)
            {
                if (_in.OwnerBlocked.TryGetValue(unit.Owner, out var blocked) && blocked.Contains((d, p))) continue;
                foreach (var victim in Blockers(u, d, p))
                {
                    if (!Fits(u, d, p, ignore: victim)) continue;
                    var (vd, vp) = _at[victim]!.Value;
                    Remove(victim);
                    Put(u, d, p);
                    if (PlaceBest(victim)) return true;
                    Remove(u);
                    Put(victim, vd, vp);
                }
            }
            return false;
        }

        /// <summary>Unitning (kun, soat) ga tushishiga to'sqinlik qilayotgan joylashgan unitlar.</summary>
        private IEnumerable<int> Blockers(int u, int day, int period)
        {
            var unit = _in.Units[u];
            var found = new HashSet<int>();
            if (_grid[unit.Owner][day].TryGetValue(period, out var occ)) found.Add(occ);
            var sp = PeriodOf(unit.Owner, period);
            foreach (var part in unit.Parts)
            {
                foreach (var (b, x) in _teacher.GetValueOrDefault(part.TeacherId) ?? [])
                    if (x >= 0 && b.Day == day && Overlaps(b.Start, b.End, b.Period, sp.Start, sp.End, sp.Period)) found.Add(x);
                if (RoomOf(unit, part) is { } room)
                    foreach (var (b, x) in _room.GetValueOrDefault(room) ?? [])
                        if (x >= 0 && b.Day == day && Overlaps(b.Start, b.End, b.Period, sp.Start, sp.End, sp.Period)) found.Add(x);
            }
            return found.Count == 1 ? found : [];
        }

        /// <summary>
        /// Oynalarni yopish: kundagi oxirgi darsni bo'sh (oyna) soatga ko'chirish, bo'lmasa boshqa
        /// kunning oxiriga; jarima kamaysagina.
        /// </summary>
        private void Compact()
        {
            for (var pass = 0; pass < 6; pass++)
            {
                var moved = false;
                for (var o = 0; o < _in.Owners.Count; o++)
                {
                    var periods = _in.Owners[o].Periods;
                    foreach (var day in DaysOf(o))
                    {
                        var cells = _grid[o][day];
                        if (Gaps(periods, cells.Keys) == 0) continue;
                        var used = cells.Keys.Select(p => periods.ToList().FindIndex(x => x.Period == p)).OrderBy(i => i).ToList();
                        var holes = Enumerable.Range(used[0], used[^1] - used[0] + 1).Except(used).Select(i => periods[i].Period).ToList();
                        // Chetdagi darslar (birinchi va oxirgi) — oynaga ko'chirish uchun nomzod.
                        foreach (var edge in new[] { periods[used[^1]].Period, periods[used[0]].Period })
                        {
                            if (!cells.TryGetValue(edge, out var u)) continue;
                            var target = holes.FirstOrDefault(h => Fits(u, day, h));
                            if (target == 0) continue;
                            Remove(u);
                            Put(u, day, target);
                            moved = true;
                            break;
                        }
                    }
                }
                if (!moved) break;
            }
        }

        // ---------------------------------------------------------------- totals

        private int TotalOwnerGaps()
        {
            var total = 0;
            for (var o = 0; o < _in.Owners.Count; o++)
                foreach (var di in DaysOf(o))
                    total += Gaps(_in.Owners[o].Periods, _grid[o][di].Keys);
            return total;
        }

        private int TotalTeacherGaps() =>
            _in.Units.SelectMany(u => u.Parts.Select(p => p.TeacherId)).Distinct(StringComparer.Ordinal)
                .Sum(t => Enumerable.Range(0, 7).Sum(d => TeacherGaps(t, d)));

        private int TotalRepeats()
        {
            var total = 0;
            for (var o = 0; o < _in.Owners.Count; o++)
                foreach (var di in DaysOf(o))
                    foreach (var g in _grid[o][di].Values.GroupBy(u => _in.Units[u].SubjectId, StringComparer.Ordinal))
                    {
                        var cap = (int)Math.Ceiling(_weekly[(o, g.Key)] / (double)DaysOf(o).Count);
                        total += Math.Max(0, g.Count() - cap);
                    }
            return total;
        }
    }
}
