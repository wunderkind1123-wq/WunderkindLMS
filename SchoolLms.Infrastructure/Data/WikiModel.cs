using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Bilimlar bazasi — shaxsiy eslatmalar (Wiki.cs, docs/modules/wiki.md).</summary>
internal static class WikiModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<WikiPage>(e =>
        {
            e.ToTable("wiki_pages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.OwnerUserId).HasMaxLength(200);
            e.Property(x => x.ParentId).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Icon).HasMaxLength(16);
            // ContentJson / ContentText — `text` (≤ 1 MB, WikiRules.MaxContentChars).
            e.Property(x => x.LockedBy).HasMaxLength(200);
            e.Property(x => x.LockedAt).HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasMaxLength(32);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.UpdatedAt).HasMaxLength(32);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.OwnerUserId, x.ParentId, x.Order });
        });
    }
}
