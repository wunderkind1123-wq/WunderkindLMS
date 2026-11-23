/**
 * XULQ-ATVOR — the child's behaviour in the family's cabinet (client, 2026-10-15; docs/modules/behaviour.md).
 *
 * The top line is the score: every pupil starts at 100, positive incidents add, negative ones and
 * attendance reasons with points take away — the same number the school sees. Below — the latest
 * entries: type, reason, subject, points, date. `Barchasi` shows the rest of what the server returned.
 *
 * FAN (parents' request, 2026-11-23): every entry a teacher gives in a lesson carries that lesson's
 * subject. The chips under the score sum each subject's plus / minus, and a tap shows only that
 * subject's entries. Entries without a subject (lateness, uniform, attendance…) are "Umumiy".
 * The author's name is deliberately NOT shown to parents — only the subject.
 *
 * It loads itself (`/api/student/discipline`), so a failure never takes the home screen down, and it
 * does NOT hide when empty (client, 2026-10-07: profiles look the same for everyone).
 */
import { useMemo, useState } from 'react'
import { ShieldCheck } from 'lucide-react'
import { Badge, Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getDiscipline } from '../../lib/parentApi'

/** Rows shown before `Barchasi` is tapped. */
const PREVIEW = 5
/** Chip key of entries without a subject. */
const GENERAL = '__general__'
const ALL = '__all__'

function signed(p) {
  if (p > 0) return `+${p}`
  if (p < 0) return `−${Math.abs(p)}`
  return '0'
}

const subjectKey = (i) => i.subjectName || GENERAL

/** Homework auto-entries repeat the subject at the start of the note ("Matematika, 16.11, …") — drop it. */
function noteOf(i) {
  if (i.source !== 'manual' || !i.note) return null
  const prefix = i.subjectName ? `${i.subjectName}, ` : null
  return prefix && i.note.startsWith(prefix) ? i.note.slice(prefix.length) : i.note
}

/** Per-subject plus / minus, subjects by name, "Umumiy" last. */
function summarise(items) {
  const map = new Map()
  for (const i of items) {
    const key = subjectKey(i)
    const row = map.get(key) ?? { key, name: key === GENERAL ? 'Umumiy' : key, plus: 0, minus: 0 }
    if (i.points > 0) row.plus += i.points
    else row.minus += -i.points
    map.set(key, row)
  }
  return [...map.values()].sort((a, b) =>
    a.key === GENERAL ? 1 : b.key === GENERAL ? -1 : a.name.localeCompare(b.name),
  )
}

export function BehaviourCard({ childId }) {
  const q = useAsync(() => getDiscipline(childId), [childId])
  const [expanded, setExpanded] = useState(false)
  const [subject, setSubject] = useState(ALL)

  const data = q.data
  const items = useMemo(() => data?.items ?? [], [data])
  const subjects = useMemo(() => summarise(items), [items])
  // Chips only when at least one entry has a subject — otherwise there is nothing to split.
  const showChips = subjects.some((s) => s.key !== GENERAL)

  if (q.error || !data) {
    return (
      <Card title="Gamifikatsiya">
        {q.error ? (
          <p className="px-4 pb-4 pt-1 text-[13px] text-slate-500">Yuklab bo'lmadi. Ilovani qayta ochib ko'ring.</p>
        ) : (
          <EmptyState icon={<ShieldCheck className="h-8 w-8" />} title="Yuklanmoqda…" />
        )}
      </Card>
    )
  }

  const filtered = subject === ALL ? items : items.filter((i) => subjectKey(i) === subject)
  const shown = expanded ? filtered : filtered.slice(0, PREVIEW)
  const tone = data.remaining < 100 ? 'text-red-600' : data.remaining > 100 ? 'text-emerald-600' : 'text-brand-ink'

  const pick = (key) => {
    haptic('light')
    setSubject((cur) => (cur === key ? ALL : key))
    setExpanded(false)
  }

  return (
    <Card
      title="Gamifikatsiya"
      action={
        filtered.length > PREVIEW ? (
          <button
            type="button"
            onClick={() => {
              haptic('light')
              setExpanded((v) => !v)
            }}
            className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
          >
            {expanded ? 'Kamroq' : `Barchasi (${filtered.length})`}
          </button>
        ) : null
      }
    >
      <div className="flex items-center gap-3 px-4 pb-3 pt-1">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-brand/25 text-brand-ink">
          <ShieldCheck className="h-6 w-6" />
        </div>
        <div className="min-w-0 flex-1">
          <p className={'text-[24px] font-extrabold leading-tight ' + tone}>{data.remaining} ball</p>
          <p className="text-[13px] text-slate-500">
            100 dan boshlanadi · <span className="text-emerald-600">+{data.plus}</span> ·{' '}
            <span className="text-red-600">−{data.minus}</span>
          </p>
        </div>
      </div>

      {showChips && (
        <div className="-mt-1 flex gap-2 overflow-x-auto px-4 pb-3 [scrollbar-width:none]">
          <SubjectChip on={subject === ALL} onClick={() => pick(ALL)} name="Hammasi" />
          {subjects.map((s) => (
            <SubjectChip
              key={s.key}
              on={subject === s.key}
              onClick={() => pick(s.key)}
              name={s.name}
              plus={s.plus}
              minus={s.minus}
            />
          ))}
        </div>
      )}

      {filtered.length === 0 ? (
        <p className="border-t border-slate-100 px-4 py-3 text-[13px] text-slate-400">
          Hozircha yozuv yo'q — o'qituvchi yoki maktab yozsa, shu yerda ko'rinadi.
        </p>
      ) : (
        shown.map((i) => (
          <div key={`${i.source}-${i.id}`} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3">
            <span
              className={
                'mt-0.5 w-12 shrink-0 rounded-lg py-1 text-center text-[14px] font-bold ' +
                (i.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-700')
              }
            >
              {signed(i.points)}
            </span>
            <div className="min-w-0 flex-1">
              <p className="break-words text-[15px] font-semibold leading-snug">{i.reasonName}</p>
              {i.subjectName && (
                <p className="mt-0.5 text-[13px] font-semibold text-brand-ink">{i.subjectName}</p>
              )}
              <p className="mt-0.5 text-[13px] text-slate-500">
                {[dayMonth(i.createdAt), i.source === 'attendance' ? 'davomat' : null, noteOf(i)]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
            </div>
            <Badge tone={i.points < 0 ? 'danger' : 'success'}>{i.points < 0 ? 'Salbiy' : 'Ijobiy'}</Badge>
          </div>
        ))
      )}
    </Card>
  )
}

function SubjectChip({ on, onClick, name, plus, minus }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'shrink-0 rounded-full px-3 py-1.5 text-[13px] font-semibold transition-colors ' +
        (on ? 'bg-brand-ink text-white' : 'bg-slate-100 text-slate-700')
      }
    >
      {name}
      {plus > 0 && <span className={'ml-1.5 ' + (on ? 'text-emerald-200' : 'text-emerald-600')}>+{plus}</span>}
      {minus > 0 && <span className={'ml-1 ' + (on ? 'text-red-200' : 'text-red-600')}>−{minus}</span>}
    </button>
  )
}
