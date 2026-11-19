using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

// ===========================================================================
//  BLOKLARGA KO'CHIRISH — TZ 8-bo'lim, 7-bosqich (docs/modules/blocks.md).
// ===========================================================================
//
//  KIRISH — admin yozgan REJA (taxmin yo'q): qaysi bloklar, manbalari, guruhlari; ustozlarning
//  "asosiy ← ikkinchi" profillari. Skript rejani bajaradi:
//
//    1. Ustoz profillarini birlashtirish — ikkinchi profilga bog'langan HAR BIR qator (teacher_id,
//       absent_teacher_id, substitute_teacher_id ustunlari — katalogdan topiladi) asosiyga o'tadi,
//       takrorlanib qoladigan qatorlar (masalan bitta sinfga ikki marta rahbar) olib tashlanadi,
//       ikkinchi profil ARXIVLANADI (o'chirilmaydi). Audit jurnali — tarix, tegilmaydi.
//       Ikki profilning darslari bir soatga tushsa — HAMMASI to'xtatiladi.
//    2. Bloklarni yaratish. Guruh ustozi rejada bo'lmasa — manba sinflardagi Katta (1) / Kichik (2)
//       darslarining ustozi. Manba sinflardagi blok fani darslari (Katta/Kichik va oddiy "Chet
//       tili") blok kataklariga aylanadi: manbadan olib tashlanadi (o'tgan haftalar tarixi saqlanadi),
//       blok guruhlariga qo'yiladi.
//    3. O'quvchilarni taqsimlash: bugungi Katta/Kichik (`students.sub_group` 1/2) → 1-/2-guruh;
//       manba sinf bo'yicha blokda — avtomatik. Bo'linmaganlar — ogohlantirish (Excel import bilan).
//    4. Eski qatorlar: yo'nalish (guruh) jurnalidagi blok fani qatorlari blok guruhi va manba bilan
//       belgilanadi (o'quvchining joriy guruhi bo'yicha). SINF jurnalidagi qatorlar o'zgarmaydi —
//       baza qoidasi: blok guruhi faqat guruh qatorida (`ck_journal_entries_block_group`); ular
//       o'sha paytdagidek sinf darsi bo'lib qoladi va soni hisobotda aytiladi.
//    5. TZ 7-bo'lim qoidalari butun ma'lumot ustida (BlockAuditService).
//
//  HAMMASI BITTA TRANZAKSIYADA. `dryRun` (standart) — hammasi bajariladi, hisobot yoziladi va
//  oxirida ORQAGA QAYTARILADI. Birorta xato bo'lsa — hech narsa saqlanmaydi.
// ===========================================================================

public sealed record TeacherMergeItem(string KeepId, string MergeId);

/// <param name="FromTimetable">Manbalardagi shu fan darslarini blok kataklariga aylantirish.</param>
/// <param name="Distribute"><c>subGroup</c> — Katta/Kichik bo'yicha; <c>none</c> — taqsimlanmaydi.</param>
/// <param name="Activate">Hamma taqsimlangan bo'lsa faollashtirish.</param>
public sealed record BlockPlanItem(SaveBlockRequest Block, bool FromTimetable = true, string Distribute = "subGroup", bool Activate = false);

public sealed record BlockMigrationPlan(List<TeacherMergeItem>? TeacherMerges, List<BlockPlanItem>? Blocks, bool Backfill = true);

public sealed record BlockMigrationReport(
    bool DryRun, bool Applied, List<string> Steps, List<string> Warnings, List<string> Errors, List<BlockAuditIssue> Audit);

public static class BlockMigration
{
    /// <summary>Ustoz id'si turadigan ustunlar (katalogdan topiladi, shu nomlar bo'yicha).</summary>
    private static readonly string[] TeacherColumns = ["teacher_id", "absent_teacher_id", "substitute_teacher_id"];

    /// <summary>Tarix jadvallari — birlashtirishda tegilmaydi.</summary>
    private static readonly string[] HistoryTables = ["audit_logs"];

    public static async Task<BlockMigrationReport> RunAsync(
        AppDbContext db, BlockMigrationPlan plan, bool dryRun, string userId, CancellationToken ct = default)
    {
        var steps = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var merge in plan.TeacherMerges ?? [])
                await MergeTeachersAsync(db, merge, steps, errors, ct);
            if (errors.Count > 0) return await FinishAsync(false);

            foreach (var item in plan.Blocks ?? [])
            {
                await CreateBlockAsync(db, item, plan.Backfill, userId, steps, warnings, errors, ct);
                if (errors.Count > 0) return await FinishAsync(false);
            }

            var audit = await BlockAuditService.RunAsync(db, ct);
            if (dryRun)
            {
                await tx.RollbackAsync(ct);
                return new BlockMigrationReport(true, false, steps, warnings, errors, audit);
            }
            await tx.CommitAsync(ct);
            return new BlockMigrationReport(false, true, steps, warnings, errors, audit);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Npgsql.PostgresException)
        {
            errors.Add($"Kutilmagan xato — hech narsa saqlanmadi: {ex.GetBaseException().Message}");
            return await FinishAsync(false);
        }

        async Task<BlockMigrationReport> FinishAsync(bool applied)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new BlockMigrationReport(dryRun, applied, steps, warnings, errors, []);
        }
    }

    /* -------------------------------------------------------------------
     *  1. Ustoz profillarini birlashtirish
     * ---------------------------------------------------------------- */

    private static async Task MergeTeachersAsync(
        AppDbContext db, TeacherMergeItem m, List<string> steps, List<string> errors, CancellationToken ct)
    {
        var keep = await db.Teachers.FirstOrDefaultAsync(t => t.Id == m.KeepId, ct);
        var merge = await db.Teachers.FirstOrDefaultAsync(t => t.Id == m.MergeId, ct);
        if (keep is null || merge is null || keep.Id == merge.Id)
        {
            errors.Add($"Ustoz birlashtirish: profil topilmadi yoki ikkalasi bir xil ({m.KeepId} ← {m.MergeId})");
            return;
        }
        if (merge.IsArchived)
        {
            errors.Add($"Ustoz birlashtirish: «{merge.FullName}» allaqachon arxivda");
            return;
        }

        // Vaqt to'qnashuvi: ikki profilning joriy/keyingi haftalardagi darslari bir soatda bo'lsa — to'xtatiladi.
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        var lessons = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons).Where(t => !t.IsArchived).ToListAsync(ct))
            .Where(t => rank(t.Id) >= 2)
            .SelectMany(t => t.Lessons.Where(l => l.TeacherId == keep.Id || l.TeacherId == merge.Id)
                .Select(l => (t.ClassId, l.Day, l.Period, l.TeacherId)))
            .ToList();
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        var clashes = lessons.GroupBy(l => (l.Day, l.Period))
            .Where(g => g.Any(x => x.TeacherId == keep.Id) && g.Any(x => x.TeacherId == merge.Id)
                        && g.Select(x => x.ClassId).Distinct().Count() > 1)
            .ToList();
        if (clashes.Count > 0)
        {
            foreach (var c in clashes)
                errors.Add($"Ustoz birlashtirish to'xtatildi: «{keep.FullName}» va «{merge.FullName}» {c.Key.Day + 1}-kun "
                           + $"{c.Key.Period}-darsda ikki joyda ({string.Join(", ", c.Select(x => owners.TryGetValue(x.ClassId, out var o) ? o.Name : x.ClassId).Distinct())})");
            return;
        }

        var refs = await TeacherReferencesAsync(db, ct);
        var moved = 0;
        foreach (var (table, column, uniques) in refs)
        {
            // Asosiyda allaqachon bor bo'lsa — ikkinchi profilning qatori takror bo'lib qoladi: olib tashlanadi.
            foreach (var other in uniques)
            {
                var same = string.Join(" and ", other.Select(c => $"a.\"{c}\" is not distinct from b.\"{c}\""));
                var sql = $"delete from \"{table}\" a where a.\"{column}\" = {{0}} and exists (select 1 from \"{table}\" b "
                          + $"where b.\"{column}\" = {{1}}{(same == "" ? "" : " and " + same)})";
                await db.Database.ExecuteSqlRawAsync(sql, [merge.Id, keep.Id], ct);
            }
            moved += await db.Database.ExecuteSqlRawAsync(
                $"update \"{table}\" set \"{column}\" = {{0}} where \"{column}\" = {{1}}", [keep.Id, merge.Id], ct);
        }
        merge.IsArchived = true;
        merge.ArchivedAt = AppClock.Now.ToString("yyyy-MM-dd");
        await db.SaveChangesAsync(ct);
        steps.Add($"Ustoz birlashtirildi: «{keep.FullName}» ← «{merge.FullName}» ({moved} ta bog'lanish ko'chdi, ikkinchi profil arxivlandi)");
    }

    /// <summary>
    /// Ustoz ustunlari (jadval, ustun) va shu ustunni o'z ichiga olgan unique indekslarning BOSHQA
    /// ustunlari — takror qatorni topish uchun.
    /// </summary>
    private static async Task<List<(string Table, string Column, List<List<string>> Uniques)>> TeacherReferencesAsync(
        AppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var result = new List<(string, string, List<List<string>>)>();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = """
            select c.table_name, c.column_name,
                   coalesce((select json_agg(cols) from (
                       select array(select a.attname from unnest(i.indkey) k join pg_attribute a
                                    on a.attrelid = i.indrelid and a.attnum = k
                                    where a.attname <> c.column_name) cols
                       from pg_index i
                       where i.indrelid = format('public.%I', c.table_name)::regclass and i.indisunique
                         and exists (select 1 from unnest(i.indkey) k join pg_attribute a
                                     on a.attrelid = i.indrelid and a.attnum = k where a.attname = c.column_name)
                   ) x), '[]'::json)::text
            from information_schema.columns c
            join information_schema.tables t on t.table_schema = c.table_schema and t.table_name = c.table_name
            where c.table_schema = 'public' and t.table_type = 'BASE TABLE'
              and c.column_name = any(@cols) and c.table_name <> 'teachers' and not (c.table_name = any(@history))
            order by c.table_name, c.column_name
            """;
        var p1 = cmd.CreateParameter(); p1.ParameterName = "cols"; p1.Value = TeacherColumns; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "history"; p2.Value = HistoryTables; cmd.Parameters.Add(p2);
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var uniques = System.Text.Json.JsonSerializer.Deserialize<List<List<string>>>(reader.GetString(2)) ?? [];
            result.Add((reader.GetString(0), reader.GetString(1), uniques));
        }
        return result;
    }

    /* -------------------------------------------------------------------
     *  2–4. Blok: yaratish, jadval, taqsimlash, eski qatorlar
     * ---------------------------------------------------------------- */

    private static async Task CreateBlockAsync(
        AppDbContext db, BlockPlanItem item, bool backfill, string userId,
        List<string> steps, List<string> warnings, List<string> errors, CancellationToken ct)
    {
        var req = item.Block;
        var label = $"«{req.Name}»";
        var sourceIds = (req.Sources ?? []).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        // Manbalardagi shu fan darslari (joriy va keyingi haftalar).
        var rank = await LiveScheduleEdit.TemplateRankAsync(db, ct);
        var sourceTemplates = (await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => sourceIds.Contains(t.ClassId) && !t.IsArchived).ToListAsync(ct))
            .Where(t => rank(t.Id) >= 2).ToList();
        var subjectLessons = sourceTemplates.SelectMany(t => t.Lessons.Where(l => l.SubjectId == req.SubjectId)).ToList();

        // Ustoz rejada yo'q bo'lsa — Katta (1) / Kichik (2) darslarining eng ko'p uchragan ustozi.
        var groups = req.Groups?.ToList() ?? [];
        for (var i = 0; i < groups.Count && i < 2; i++)
        {
            if (!string.IsNullOrWhiteSpace(groups[i].TeacherId)) continue;
            var guess = subjectLessons.Where(l => l.SubGroup == i + 1 && l.TeacherId != "")
                .GroupBy(l => l.TeacherId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (guess is not null)
            {
                groups[i] = groups[i] with { TeacherId = guess };
                var name = await db.Teachers.Where(t => t.Id == guess).Select(t => t.FullName).FirstAsync(ct);
                warnings.Add($"{label}: {groups[i].Name} guruh ustozi jadvaldan olindi — {name}");
            }
        }
        req = req with { Groups = groups };

        var service = new BlockService(db);
        if (await service.ValidateAsync(req, null, ct) is { } invalid)
        {
            errors.Add($"{label}: {invalid}");
            return;
        }
        var block = await service.CreateAsync(req, userId, ct);
        await db.SaveChangesAsync(ct);
        steps.Add($"{label} yaratildi ({req.Sources!.Count} ta manba)");

        // Jadval: manbalardagi darslar → blok kataklari.
        if (item.FromTimetable && subjectLessons.Count > 0)
        {
            var cells = subjectLessons.Select(l => new BlockSlot(l.Day, l.Period)).Distinct()
                .OrderBy(s => s.Day).ThenBy(s => s.Period).ToList();
            var wrong = cells.Where(c => !BlockDayPattern.Allows(block.DayPattern, c.Day)).ToList();
            if (wrong.Count > 0)
            {
                errors.Add($"{label}: jadvaldagi darslar kun naqshiga mos emas ({string.Join(", ", wrong.Select(w => $"{w.Day + 1}-kun {w.Period}-dars"))}) "
                           + "— naqshni \"custom\" qiling yoki jadvalni avval tuzating");
                return;
            }
            var removed = 0;
            foreach (var tpl in sourceTemplates)
            {
                var hit = tpl.Lessons.Where(l => l.SubjectId == req.SubjectId).ToList();
                if (hit.Count == 0) continue;
                var target = await LiveScheduleEdit.PrepareAsync(db, tpl, ct);
                if (target.Error is not null)
                {
                    errors.Add($"{label}: {target.Error}");
                    return;
                }
                foreach (var l in target.Template!.Lessons.Where(l => l.SubjectId == req.SubjectId).ToList())
                {
                    target.Template.Lessons.Remove(l);
                    removed++;
                }
                await db.SaveChangesAsync(ct);
            }
            var schedule = new BlockScheduleService(db);
            if (await schedule.ValidateAsync(block, cells, ct: ct) is { } clash)
            {
                errors.Add($"{label}: {clash}");
                return;
            }
            if (await schedule.ApplyAsync(block, cells, [], userId, ct) is { } applyError)
            {
                errors.Add($"{label}: {applyError}");
                return;
            }
            steps.Add($"{label}: manbalardan {removed} ta dars olindi, blokka {cells.Count} ta katak qo'yildi");
        }
        else if (item.FromTimetable)
        {
            warnings.Add($"{label}: manbalarda bu fan darsi yo'q — blok jadvali bo'sh");
        }

        // Taqsimlash.
        var today = AppClock.Today;
        if (block.BlockType == BlockType.BaseClass)
        {
            var auto = await service.AutoAssignAsync(block, today, userId, ct);
            await db.SaveChangesAsync(ct);
            steps.Add($"{label}: manba sinf bo'yicha {auto} ta o'quvchi o'z guruhiga");
        }
        if (item.Distribute == "subGroup")
        {
            var bgs = await service.GroupsAsync(block.Id, ct);
            var pupils = await service.SourceStudentsAsync(await service.SourcesAsync(block.Id, ct), today, ct);
            var assigned = 0;
            for (var half = 1; half <= 2 && half <= bgs.Count; half++)
            {
                var ids = pupils.Where(p => p.Student.SubGroup == half).Select(p => p.Student.Id).ToList();
                // Manba sinf bo'yicha blokda o'z guruhiga tushganlar tegilmaydi.
                var free = new List<string>();
                foreach (var id in ids)
                    if (await service.GroupOnAsync(block.Id, id, today, ct) is null) free.Add(id);
                if (await service.AssignAsync(block, free, bgs[half - 1].Id, today, userId, ct) is { } assignError)
                {
                    errors.Add($"{label}: {assignError}");
                    return;
                }
                await db.SaveChangesAsync(ct);
                assigned += free.Count;
            }
            var left = pupils.Count(p => p.Student.SubGroup == 0);
            steps.Add($"{label}: Katta/Kichik bo'yicha {assigned} ta o'quvchi taqsimlandi");
            if (left > 0) warnings.Add($"{label}: {left} ta o'quvchining Katta/Kichik belgisi yo'q — Excel yoki taqsimlash ekranida");
        }

        // Eski qatorlar.
        if (backfill) await BackfillAsync(db, block, req, label, steps, warnings, ct);

        if (item.Activate)
        {
            var problems = await service.ActivationErrorsAsync(block, today, ct);
            if (problems.Count == 0)
            {
                block.IsActive = true;
                await db.SaveChangesAsync(ct);
                steps.Add($"{label} faollashtirildi");
            }
            else warnings.Add($"{label} faollashmadi: {string.Join("; ", problems)}");
        }
    }

    /// <summary>
    /// Yo'nalish (guruh) jurnalidagi blok fani qatorlari — o'quvchining joriy guruhi va manbasi bilan.
    /// Sinf jurnali qatorlari o'zgarmaydi (fayl boshidagi izoh, 4-band).
    /// </summary>
    private static async Task BackfillAsync(
        AppDbContext db, Block block, SaveBlockRequest req, string label, List<string> steps, List<string> warnings, CancellationToken ct)
    {
        var groups = await db.BlockGroups.AsNoTracking().Where(g => g.BlockId == block.Id).ToListAsync(ct);
        var sgIds = groups.Select(g => g.StudyGroupId).ToList();
        var current = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => sgIds.Contains(m.GroupId) && m.LeftOn == null).ToListAsync(ct))
            .ToDictionary(m => m.StudentId, m => groups.First(g => g.StudyGroupId == m.GroupId).Id, StringComparer.Ordinal);

        var stamped = 0;
        foreach (var src in req.Sources!)
        {
            if (src.Kind == LessonOwnerKind.Class)
            {
                var kept = await db.JournalEntries.CountAsync(e => e.ClassId == src.Id && e.SubjectId == req.SubjectId
                                                                   && e.OwnerKind == LessonOwnerKind.Class, ct);
                if (kept > 0)
                    warnings.Add($"{label}: sinf jurnalidagi {kept} ta eski qator sinf darsi bo'lib qoladi (blok guruhi faqat guruh qatorida)");
                continue;
            }
            var rows = await db.JournalEntries.Where(e => e.ClassId == src.Id && e.SubjectId == req.SubjectId
                                                          && e.OwnerKind == LessonOwnerKind.Group && e.BlockGroupId == null).ToListAsync(ct);
            foreach (var e in rows.Where(e => current.ContainsKey(e.StudentId)))
            {
                e.BlockGroupId = current[e.StudentId];
                e.SourceClassId = src.Id;
                stamped++;
            }
            var orphans = rows.Count(e => !current.ContainsKey(e.StudentId));
            if (orphans > 0) warnings.Add($"{label}: {orphans} ta eski qatorning o'quvchisi blokda emas — belgilanmadi");
        }
        await db.SaveChangesAsync(ct);
        steps.Add($"{label}: {stamped} ta eski jurnal qatori blok guruhi bilan belgilandi");
    }
}
