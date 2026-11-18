using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Bloklar (birlashgan va daraja guruhlari) — <c>docs/modules/blocks.md</c>, migratsiya
/// <c>Blocks</c>. To'rtta yangi jadval va mavjud jadvallardagi NULLABLE
/// <c>block_group_id</c> / <c>source_class_id</c> / <c>block_id</c> ustunlari.
///
/// <para>
/// <b>Mavjud ma'lumot o'zgarmaydi:</b> har bir yangi ustun nullable va DEFAULT'siz —
/// PostgreSQL uni faqat katalogga yozadi, mavjud qatorlar NULL o'qiydi, ya'ni oddiy
/// (blok bo'lmagan) darslarning birorta qatori tegilmaydi.
/// </para>
/// </summary>
internal static class BlockModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureBlocks(b);
        ConfigureBlockClasses(b);
        ConfigureBlockGroups(b);
        ConfigureBlockGroupTeachers(b);
        ConfigureLessonColumns(b);
    }

    private static void ConfigureBlocks(ModelBuilder b) =>
        b.Entity<Block>(e =>
        {
            e.ToTable("blocks", t =>
            {
                t.HasCheckConstraint("ck_blocks_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_blocks_block_type", "block_type in ('level','base_class')");
                t.HasCheckConstraint("ck_blocks_day_pattern", "day_pattern in ('odd','even','custom')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.AcademicYear).HasMaxLength(9);
            e.Property(x => x.BlockType).HasMaxLength(20);
            e.Property(x => x.DayPattern).HasMaxLength(10);
            e.Property(x => x.IsActive).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Fan blokda ishlatilgan bo'lsa o'chirilmaydi.
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.AcademicYear, x.SubjectId }).HasDatabaseName("ix_blocks_year_subject");
        });

    private static void ConfigureBlockClasses(ModelBuilder b) =>
        b.Entity<BlockClass>(e =>
        {
            e.ToTable("block_classes", t =>
                t.HasCheckConstraint("ck_block_classes_owner_kind", "owner_kind in ('class','group')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OwnerKind).HasMaxLength(10);
            e.HasOne<Block>().WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Cascade);

            // TZ: UNIQUE(block_id, class_id).
            e.HasIndex(x => new { x.BlockId, x.OwnerKind, x.OwnerId }).IsUnique()
                .HasDatabaseName("ux_block_classes_block_owner");
            // "Shu sinf qaysi bloklarda" — jadval va konflikt tekshiruvi.
            e.HasIndex(x => x.OwnerId).HasDatabaseName("ix_block_classes_owner");
        });

    private static void ConfigureBlockGroups(ModelBuilder b) =>
        b.Entity<BlockGroup>(e =>
        {
            e.ToTable("block_groups", t =>
            {
                t.HasCheckConstraint("ck_block_groups_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_block_groups_code", "code in ('KATTA','KICHIK','custom')");
                // TZ 7-bo'lim, 7-qoida: blokda ko'pi bilan 2 ta guruh — tartib 1 yoki 2,
                // (block_id, sort_order) esa unikal. "Aniq 2 ta" ni xizmat tekshiradi.
                t.HasCheckConstraint("ck_block_groups_sort_order", "sort_order in (1, 2)");
                t.HasCheckConstraint("ck_block_groups_base_owner",
                    "(base_owner_kind is null) = (base_owner_id is null) "
                    + "and (base_owner_kind is null or base_owner_kind in ('class','group'))");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.BaseOwnerKind).HasMaxLength(10);

            e.HasOne<Block>().WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Cascade);
            // O'quv guruhida jurnal va a'zolik bor — blok guruhi turganda o'chirilmaydi.
            e.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.StudyGroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => new { x.BlockId, x.SortOrder }).IsUnique()
                .HasDatabaseName("ux_block_groups_block_sort");
            e.HasIndex(x => x.StudyGroupId).IsUnique().HasDatabaseName("ux_block_groups_study_group");
        });

    private static void ConfigureBlockGroupTeachers(ModelBuilder b) =>
        b.Entity<BlockGroupTeacher>(e =>
        {
            e.ToTable("block_group_teachers", t =>
                t.HasCheckConstraint("ck_block_group_teachers_period", "valid_to is null or valid_to >= valid_from"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            // Guruhda bir vaqtda bitta joriy ustoz.
            e.HasIndex(x => x.BlockGroupId).IsUnique().HasFilter("valid_to is null")
                .HasDatabaseName("ux_block_group_teachers_one_current");
            // "Ustozning guruhlari" (Guruhlarim).
            e.HasIndex(x => x.TeacherId).HasDatabaseName("ix_block_group_teachers_teacher");
        });

    /// <summary>Mavjud dars jadvallaridagi yangi ustunlar — hammasi nullable.</summary>
    private static void ConfigureLessonColumns(ModelBuilder b)
    {
        b.Entity<ScheduleLesson>(e =>
        {
            e.HasOne<Block>().WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.BlockId).HasFilter("block_id is not null").HasDatabaseName("ix_schedule_lesson_block");
        });

        // Blok guruhi yozuvi faqat guruh egasining (owner_kind = 'group') qatorida bo'ladi.
        b.Entity<JournalEntry>(e =>
        {
            BlockGroupFk(e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId));
            e.HasIndex(x => x.BlockGroupId).HasFilter("block_group_id is not null")
                .HasDatabaseName("ix_journal_entries_block_group");
            e.HasIndex(x => x.SourceClassId).HasFilter("source_class_id is not null")
                .HasDatabaseName("ix_journal_entries_source_class");
            e.ToTable(t => t.HasCheckConstraint("ck_journal_entries_block_group",
                "block_group_id is null or owner_kind = 'group'"));
        });

        b.Entity<LessonNote>(e =>
        {
            BlockGroupFk(e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId));
            e.ToTable(t => t.HasCheckConstraint("ck_lesson_notes_block_group",
                "block_group_id is null or owner_kind = 'group'"));
        });

        b.Entity<QuarterGrade>(e =>
        {
            BlockGroupFk(e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId));
            e.ToTable(t => t.HasCheckConstraint("ck_quarter_grades_block_group",
                "block_group_id is null or owner_kind = 'group'"));
        });

        b.Entity<DisciplinePoint>(e =>
        {
            BlockGroupFk(e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId));
            e.HasIndex(x => x.BlockGroupId).HasFilter("block_group_id is not null")
                .HasDatabaseName("ix_discipline_points_block_group");
        });

        b.Entity<InterimTest>(e =>
            BlockGroupFk(e.HasOne<BlockGroup>().WithMany().HasForeignKey(x => x.BlockGroupId)));
    }

    /// <summary>RESTRICT: baho/davomat yozilgan blok guruhi o'chirilmaydi — tarix yo'qolmasin.</summary>
    private static void BlockGroupFk<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.ReferenceCollectionBuilder<BlockGroup, T> fk)
        where T : class =>
        fk.OnDelete(DeleteBehavior.Restrict);
}
