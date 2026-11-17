import { useEffect, useState } from 'react'
import {
  getArchiveReasonGroups,
  getArchiveReasons,
  reasonsByGroup,
  type ArchiveReason,
  type ArchiveReasonGroup,
} from '@/api/services/archiveReasons'
import { Select } from '@/components/ui/Input'

/**
 * Ketish sababi tanlovi — arxivlash oynalari va arxiv doskasi uchun bitta joy (2026-11-13).
 * Sabablar guruh bo'yicha (<optgroup>) chiqadi: xodim "O'zi ketdi → Narx qimmat" ni ko'rib tanlaydi,
 * guruh sababdan kelib chiqadi. Majburiy: bo'sh tanlov — "Sabab turini tanlang".
 *
 * `onlyGroupId` — doskada kartochka boshqa ustunga sudralganda faqat o'sha guruh sabablari.
 */
export function ArchiveReasonSelect({
  value,
  onChange,
  label = 'Sabab turi',
  onlyGroupId,
  reload,
}: {
  value: string
  onChange: (reasonId: string) => void
  label?: string
  onlyGroupId?: string
  /** O'zgarsa ro'yxat qayta yuklanadi (oyna har ochilganda). */
  reload?: unknown
}) {
  const [reasons, setReasons] = useState<ArchiveReason[]>([])
  const [groups, setGroups] = useState<ArchiveReasonGroup[]>([])

  useEffect(() => {
    let alive = true
    Promise.all([getArchiveReasons(), getArchiveReasonGroups()])
      .then(([r, g]) => {
        if (!alive) return
        setReasons(r)
        setGroups(g)
      })
      .catch(() => {
        if (!alive) return
        setReasons([])
        setGroups([])
      })
    return () => {
      alive = false
    }
  }, [reload])

  const sections = onlyGroupId
    ? [{ label: '', reasons: reasons.filter((r) => r.isActive && r.groupId === onlyGroupId) }]
    : reasonsByGroup(reasons, groups)
  const empty = sections.every((s) => s.reasons.length === 0)

  return (
    <div>
      <Select label={label} required value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">— Tanlang —</option>
        {sections.map((s) =>
          s.label ? (
            <optgroup key={s.label} label={s.label}>
              {s.reasons.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </optgroup>
          ) : (
            s.reasons.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))
          ),
        )}
      </Select>
      <p className="mt-1 text-xs text-slate-400">
        {empty && onlyGroupId
          ? "Bu guruhda faol sabab yo'q — uni \"Sozlamalar → Arxivlash sabablari\"da qo'shing."
          : "Sabablar va guruhlar \"Sozlamalar → Arxivlash sabablari\"da boshqariladi."}
      </p>
    </div>
  )
}
