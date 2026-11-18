using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Migrations;

// ===========================================================================
//  `Blocks` — docs/modules/blocks.md (TZ: Birlashgan va daraja guruhlari).
//  Four new tables + nullable block_group_id / source_class_id / block_id on the
//  lesson tables. COVERAGE: 1) Up() on existing data — no row is lost or changed,
//  every new column reads NULL; 2) the database guards (2 groups per block, one
//  current teacher, block rows only on group-owned journal rows, enums);
//  3) Down() keeps the old data, and Up() runs again.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class BlocksMigrationTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string PreviousMigration = "20261117082524_CurriculumPlanTeacher";

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("blocksmig");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.AppRwConnectionString));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Up_mavjud_darslarni_ozgartirmaydi_yangi_ustunlar_bosh()
    {
        await MigrateToAsync(PreviousMigration);
        await using (var conn = await OpenOwnerAsync())
            await SeedLessonsAsync(conn, "up");

        await MigrateToAsync(null);

        await using var db = await OpenOwnerAsync();
        Assert.Equal(1L, await ScalarAsync(db, "select count(*) from schedule_lesson where block_id is null"));
        Assert.Equal(2L, await ScalarAsync(db,
            "select count(*) from journal_entries where block_group_id is null and source_class_id is null"));
        Assert.Equal(5, await ScalarAsync(db, "select grade from journal_entries where id = 'je-up-1'"));
        Assert.Equal(1L, await ScalarAsync(db, "select count(*) from lesson_notes where block_group_id is null"));
        Assert.Equal(1L, await ScalarAsync(db,
            "select count(*) from discipline_points where block_group_id is null and source_class_id is null"));
        Assert.Equal(2L, await ScalarAsync(db,
            "select count(*) from study_group_members where source_class_id is null and join_reason is null"));
    }

    [Fact]
    public async Task Cheklovlar_blokni_bazada_qoriqlaydi()
    {
        await using var conn = await OpenOwnerAsync();
        await SeedLessonsAsync(conn, "ck");
        var block = await InsertBlockAsync(conn, "ck");

        // Blok turi va kun naqshi — faqat ro'yxatdagilar.
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"update blocks set block_type = 'mixed' where id = '{block}'"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"update blocks set day_pattern = 'weekend' where id = '{block}'"))).SqlState);

        // Blokda ko'pi bilan 2 ta guruh: 3-tartib — check, takror tartib — unique.
        var katta = await InsertBlockGroupAsync(conn, block, "ck", 1, "KATTA");
        await InsertBlockGroupAsync(conn, block, "ck", 2, "KICHIK");
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertBlockGroupAsync(conn, block, "ck", 3, "custom"))).SqlState);
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertBlockGroupAsync(conn, block, "ck", 2, "custom"))).SqlState);

        // Manba sinf takrorlanmaydi.
        await ExecAsync(conn,
            $"insert into block_classes (block_id, owner_kind, owner_id) values ('{block}', 'class', 'c-ck-1')");
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"insert into block_classes (block_id, owner_kind, owner_id) values ('{block}', 'class', 'c-ck-1')"))).SqlState);

        // Guruhda bitta joriy ustoz; yopilgan qator tarix bo'lib qoladi.
        await InsertTeacherRowAsync(conn, katta, "t-ck-1", "'2026-09-01'", "'2026-12-14'");
        await InsertTeacherRowAsync(conn, katta, "t-ck-1", "'2026-12-15'", "null");
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTeacherRowAsync(conn, katta, "t-ck-1", "'2026-12-20'", "null"))).SqlState);

        // Blok guruhi faqat guruh egasining (owner_kind = 'group') jurnal qatorida.
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(conn,
            $"update journal_entries set block_group_id = '{katta}' where id = 'je-ck-1'"))).SqlState);
        await ExecAsync(conn,
            $"update journal_entries set owner_kind = 'group', block_group_id = '{katta}', source_class_id = 'c-ck-1' where id = 'je-ck-1'");

        // Baho yozilgan blok guruhi o'chirilmaydi.
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(conn, $"delete from block_groups where id = '{katta}'"))).SqlState);
    }

    [Fact]
    public async Task Down_eski_malumotni_saqlaydi_va_qayta_Up_ishlaydi()
    {
        await using (var conn = await OpenOwnerAsync())
        {
            await SeedLessonsAsync(conn, "dn");
            var block = await InsertBlockAsync(conn, "dn");
            await InsertBlockGroupAsync(conn, block, "dn", 1, "KATTA");
        }

        await MigrateToAsync(PreviousMigration);

        await using (var conn = await OpenOwnerAsync())
        {
            var columns = await AllColumnsAsync(conn);
            Assert.DoesNotContain("journal_entries.block_group_id", columns);
            Assert.DoesNotContain("schedule_lesson.block_id", columns);
            Assert.DoesNotContain("study_group_members.source_class_id", columns);
            Assert.Null(await ScalarAsync(conn, "select to_regclass('public.blocks')::text"));
            Assert.Equal(2L, await ScalarAsync(conn, "select count(*) from journal_entries"));
            Assert.Equal(1L, await ScalarAsync(conn, "select count(*) from schedule_lesson"));
            Assert.Equal(2L, await ScalarAsync(conn, "select count(*) from study_group_members"));
        }

        await MigrateToAsync(null);

        await using (var conn = await OpenOwnerAsync())
        {
            Assert.Contains("journal_entries.block_group_id", await AllColumnsAsync(conn));
            Assert.Equal(0L, await ScalarAsync(conn, "select count(*) from blocks"));
            Assert.Equal(2L, await ScalarAsync(conn, "select count(*) from journal_entries"));
        }
    }

    [Theory]
    [InlineData(BlockDayPattern.Odd, new[] { 0, 2, 4 })]
    [InlineData(BlockDayPattern.Even, new[] { 1, 3, 5 })]
    [InlineData(BlockDayPattern.Custom, new[] { 0, 1, 2, 3, 4, 5 })]
    public void Kun_naqshi_toq_va_juft_kunlarni_ajratadi(string pattern, int[] allowed)
    {
        for (var day = 0; day <= 5; day++)
            Assert.Equal(allowed.Contains(day), BlockDayPattern.Allows(pattern, day));
    }

    [Fact]
    public async Task EF_modeli_yangi_jadvallarni_oqiydi()
    {
        await using (var conn = await OpenOwnerAsync())
        {
            await SeedLessonsAsync(conn, "ef");
            var block = await InsertBlockAsync(conn, "ef");
            await InsertBlockGroupAsync(conn, block, "ef", 1, "KATTA");
        }

        await using var db = NewDb();
        var group = await db.BlockGroups.SingleAsync();
        Assert.Equal("KATTA", group.Code);
        Assert.Equal((short)1, group.SortOrder);
        Assert.False((await db.Blocks.SingleAsync()).IsActive);
        Assert.All(await db.JournalEntries.ToListAsync(), e => Assert.Null(e.BlockGroupId));
    }

    // =====================================================================
    //  Seed
    // =====================================================================

    /// <summary>Oddiy (blok bo'lmagan) darslar: jadval katagi, ikki jurnal qatori, mavzu, ball va guruh a'zosi.</summary>
    private static async Task SeedLessonsAsync(NpgsqlConnection conn, string tag)
    {
        await InsertMinimalRowAsync(conn, "users", new()
        {
            ["id"] = $"'u-{tag}'", ["email"] = $"'{tag}.blocks'", ["role"] = "'admin'",
        });
        await InsertMinimalRowAsync(conn, "subjects", new() { ["id"] = $"'s-{tag}'", ["name"] = $"'Ingliz {tag}'" });
        await InsertMinimalRowAsync(conn, "classes", new() { ["id"] = $"'c-{tag}-1'", ["name"] = $"'4-A {tag}'" });
        await InsertMinimalRowAsync(conn, "teachers", new() { ["id"] = $"'t-{tag}-1'", ["full_name"] = "'Ustoz'" });
        for (var i = 1; i <= 2; i++)
            await InsertMinimalRowAsync(conn, "students", new() { ["id"] = $"'st-{tag}-{i}'", ["full_name"] = $"'Bola {i}'" });

        await InsertMinimalRowAsync(conn, "schedule_templates", new()
        {
            ["id"] = $"'tpl-{tag}'", ["class_id"] = $"'c-{tag}-1'", ["name"] = "'Asosiy'",
        });
        await InsertMinimalRowAsync(conn, "schedule_lesson", new()
        {
            ["id"] = $"'sl-{tag}'", ["template_id"] = $"'tpl-{tag}'", ["subject_id"] = $"'s-{tag}'",
            ["teacher_id"] = $"'t-{tag}-1'", ["day"] = "0", ["period"] = "2",
        });
        for (var i = 1; i <= 2; i++)
            await InsertMinimalRowAsync(conn, "journal_entries", new()
            {
                ["id"] = $"'je-{tag}-{i}'", ["class_id"] = $"'c-{tag}-1'", ["subject_id"] = $"'s-{tag}'",
                ["student_id"] = $"'st-{tag}-{i}'", ["date"] = "'2026-11-16'", ["grade"] = "5",
            });
        await InsertMinimalRowAsync(conn, "lesson_notes", new()
        {
            ["id"] = $"'ln-{tag}'", ["class_id"] = $"'c-{tag}-1'", ["subject_id"] = $"'s-{tag}'", ["topic"] = "'Present Simple'",
        });
        await InsertMinimalRowAsync(conn, "discipline_points", new()
        {
            ["id"] = $"'dp-{tag}'", ["student_id"] = $"'st-{tag}-1'", ["points"] = "3",
        });

        // Oddiy o'quv guruhi va a'zosi — blok guruhi shunga bog'lanadi.
        await InsertMinimalRowAsync(conn, "study_groups", new()
        {
            ["id"] = $"'{GroupId(tag, 1)}'", ["name"] = $"'Katta {tag}'", ["subject_id"] = $"'s-{tag}'",
            ["created_by"] = $"'u-{tag}'",
        });
        await InsertMinimalRowAsync(conn, "study_groups", new()
        {
            ["id"] = $"'{GroupId(tag, 2)}'", ["name"] = $"'Kichik {tag}'", ["subject_id"] = $"'s-{tag}'",
            ["created_by"] = $"'u-{tag}'",
        });
        await InsertMinimalRowAsync(conn, "study_groups", new()
        {
            ["id"] = $"'{GroupId(tag, 3)}'", ["name"] = $"'Uchinchi {tag}'", ["subject_id"] = $"'s-{tag}'",
            ["created_by"] = $"'u-{tag}'",
        });
        await InsertMinimalRowAsync(conn, "study_group_members", new()
        {
            ["group_id"] = $"'{GroupId(tag, 1)}'", ["subject_id"] = $"'s-{tag}'", ["student_id"] = $"'st-{tag}-1'",
            ["joined_on"] = "'2026-09-01'", ["created_by"] = $"'u-{tag}'",
        });
        await InsertMinimalRowAsync(conn, "study_group_members", new()
        {
            ["group_id"] = $"'{GroupId(tag, 2)}'", ["subject_id"] = $"'s-{tag}'", ["student_id"] = $"'st-{tag}-2'",
            ["joined_on"] = "'2026-09-01'", ["created_by"] = $"'u-{tag}'",
        });
    }

    /// <summary>Teg bo'yicha barqaror o'quv guruhi id'si (1, 2, 3).</summary>
    private static string GroupId(string tag, int n) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{tag}-{n}"))).ToString();

    private static async Task<string> InsertBlockAsync(NpgsqlConnection conn, string tag)
    {
        var id = Guid.NewGuid().ToString();
        await ExecAsync(conn,
            "insert into blocks (id, name, subject_id, academic_year, block_type, day_pattern, created_by) "
            + $"values ('{id}', 'Fil+Ijt Ingliz', 's-{tag}', '2026-2027', 'level', 'odd', 'u-{tag}')");
        return id;
    }

    private static async Task<string> InsertBlockGroupAsync(
        NpgsqlConnection conn, string blockId, string tag, int order, string code)
    {
        var id = Guid.NewGuid().ToString();
        await ExecAsync(conn,
            "insert into block_groups (id, block_id, study_group_id, name, code, sort_order) "
            + $"values ('{id}', '{blockId}', '{GroupId(tag, Math.Min(order, 3))}', 'Guruh {order}', '{code}', {order})");
        return id;
    }

    private static Task InsertTeacherRowAsync(
        NpgsqlConnection conn, string groupId, string teacherId, string from, string to) =>
        ExecAsync(conn,
            "insert into block_group_teachers (block_group_id, teacher_id, valid_from, valid_to, created_by) "
            + $"values ('{groupId}', '{teacherId}', {from}, {to}, "
            + "(select id from users order by id limit 1))");

    // =====================================================================
    //  Helpers (same pattern as TrackGroupsAsClassesMigrationTests)
    // =====================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private async Task MigrateToAsync(string? target)
    {
        await using var db = NewDb();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var conn = new NpgsqlConnection(_database.OwnerConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<HashSet<string>> AllColumnsAsync(NpgsqlConnection conn)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            "select table_name, column_name from information_schema.columns where table_schema = 'public'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return columns;
    }

    /// <summary>
    /// Minimal row: every NOT NULL column without a DEFAULT gets an "empty" value of its type,
    /// so a later column does not break the test.
    /// </summary>
    private static async Task InsertMinimalRowAsync(
        NpgsqlConnection conn, string table, Dictionary<string, string> values)
    {
        await using (var cmd = new NpgsqlCommand(
                         "select column_name, data_type from information_schema.columns "
                         + "where table_schema = 'public' and table_name = @t "
                         + "and is_nullable = 'NO' and column_default is null "
                         + "and is_generated = 'NEVER' and identity_generation is null", conn))
        {
            cmd.Parameters.AddWithValue("t", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = reader.GetString(0);
                if (values.ContainsKey(column)) continue;
                values[column] = reader.GetString(1) switch
                {
                    "text" or "character varying" or "character" => "''",
                    "boolean" => "false",
                    "integer" or "bigint" or "smallint" => "0",
                    "numeric" or "double precision" or "real" => "0",
                    "uuid" => "gen_random_uuid()",
                    "date" => "current_date",
                    "jsonb" or "json" => "'{}'",
                    "ARRAY" => "'{}'",
                    _ => "now()",
                };
            }
        }

        var columns = string.Join(", ", values.Keys.Select(c => $"\"{c}\""));
        await ExecAsync(conn, $"insert into {table} ({columns}) values ({string.Join(", ", values.Values)})");
    }
}
