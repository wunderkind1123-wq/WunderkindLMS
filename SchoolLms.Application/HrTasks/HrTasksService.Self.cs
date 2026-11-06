using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

/// <summary>
/// Recurring tasks an employee sets for THEMSELF (client, 2026-11-06: "o'zimga o'zim ham takroriy
/// topshiriq qo'sha olay" — curriculum-monitoring.md §4). The same rules and generator as the
/// panel's templates; the employee only ever sees and touches rules they made for themself —
/// a rule a manager aimed at them stays the manager's.
/// </summary>
public sealed partial class HrTasksService
{
    /// <summary>A panel limited to the employee alone — the template code checks scope against it.</summary>
    private static HrPanel SelfPanel(HrEmployee me) =>
        new(new HrActor(me.UserId, "self", []), new HrScope(new HashSet<string> { me.Id }), me, false);

    private IQueryable<HrTaskTemplate> OwnTemplates(HrEmployee me) =>
        db.HrTaskTemplates.Where(t => t.TargetType == HrTargetTypes.Employee
            && t.TargetEmployeeId == me.Id && t.CreatedBy == me.UserId);

    public async Task<List<HrTemplateDto>> MyTemplatesAsync(HrEmployee me, CancellationToken ct = default)
    {
        var rows = await OwnTemplates(me).AsNoTracking().OrderBy(t => t.Title).ThenBy(t => t.Id).ToListAsync(ct);
        return await TemplateDtosAsync(rows, ct);
    }

    public Task<HrTemplateDto> CreateMyTemplateAsync(HrEmployee me, HrTemplateCreateInput input, CancellationToken ct = default) =>
        CreateTemplateAsync(SelfPanel(me), input with
        {
            TargetType = HrTargetTypes.Employee,
            TargetEmployeeId = me.Id,
            TargetPositionId = null,
        }, ct);

    /// <summary>The target cannot be moved off oneself — those fields are ignored.</summary>
    public async Task<HrTemplateDto> UpdateMyTemplateAsync(HrEmployee me, string id, JsonElement body, CancellationToken ct = default)
    {
        await EnsureOwnTemplateAsync(me, id, ct);
        var fields = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Where(p => p.Name is not ("targetType" or "targetEmployeeId" or "targetPositionId"))
                .ToDictionary(p => p.Name, p => p.Value)
            : [];
        var cleaned = JsonSerializer.SerializeToElement(fields);
        return await UpdateTemplateAsync(SelfPanel(me), id, cleaned, ct);
    }

    public async Task<HrTemplateDto> ToggleMyTemplateAsync(HrEmployee me, string id, bool? isActive, CancellationToken ct = default)
    {
        await EnsureOwnTemplateAsync(me, id, ct);
        return await ToggleTemplateAsync(SelfPanel(me), id, isActive, ct);
    }

    public async Task DeleteMyTemplateAsync(HrEmployee me, string id, CancellationToken ct = default)
    {
        await EnsureOwnTemplateAsync(me, id, ct);
        await DeleteTemplateAsync(SelfPanel(me), id, ct);
    }

    /// <summary>Someone else's rule is "not found", never "forbidden" (hr-tasks.md §3).</summary>
    private async Task EnsureOwnTemplateAsync(HrEmployee me, string id, CancellationToken ct)
    {
        if (!await OwnTemplates(me).AnyAsync(t => t.Id == id, ct)) throw HrErrors.TemplateNotFound();
    }
}
