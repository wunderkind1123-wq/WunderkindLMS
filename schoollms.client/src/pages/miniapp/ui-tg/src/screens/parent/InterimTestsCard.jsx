/**
 * ORALIQ TESTLAR — "Baholar" tabida (mijoz, 2026-11-10). Yo'nalish o'quvchisiga jurnal bahosi qo'yilmaydi:
 * har hafta oraliq test olinadi, natija foizda (to'g'ri javob / savollar soni). Natija bo'lmasa karta
 * umuman chiqmaydi (3–8-sinflar). O'zi yuklanadi — xato bo'lsa ham baholar tabi ishlayveradi.
 */
import { useState } from 'react'
import { ClipboardList } from 'lucide-react'
import { Badge, Card } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { haptic } from '../../lib/telegram'
import { getInterimTests } from '../../lib/parentApi'

const PREVIEW = 5
const fmt = (v) => String(v).replace('.', ',')
const fmtDay = (iso) => iso.split('-').reverse().join('.')

function tone(pct) {
  if (pct == null) return 'neutral'
  if (pct >= 80) return 'success'
  if (pct >= 60) return 'brand'
  return 'danger'
}

export function InterimTestsCard({ childId }) {
  const state = useAsync(() => getInterimTests(childId), [childId])
  const [expanded, setExpanded] = useState(false)
  const data = state.data
  if (!data || data.items.length === 0) return null

  const items = expanded ? data.items : data.items.slice(0, PREVIEW)
  return (
    <Card
      title="Oraliq testlar"
      action={
        data.average != null && (
          <span className="text-[13px] font-semibold text-slate-500">
            o'rtacha <b className="text-slate-800">{fmt(data.average)}%</b>
          </span>
        )
      }
    >
      {items.map((t, i) => (
        <div key={i} className="flex items-center gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <ClipboardList className="h-[18px] w-[18px]" />
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate text-[15px] font-semibold">{t.subjectName}</p>
            <p className="truncate text-[12px] text-slate-400">
              {fmtDay(t.date)} · {t.absent ? 'kelmagan' : `${t.correct}/${t.questionCount} to'g'ri`}
              {t.topic ? ` · ${t.topic}` : ''}
            </p>
          </div>
          {t.absent ? <Badge>Kelmadi</Badge> : <Badge tone={tone(t.percent)}>{fmt(t.percent)}%</Badge>}
        </div>
      ))}
      {data.items.length > PREVIEW && (
        <button
          type="button"
          onClick={() => {
            haptic('light')
            setExpanded((v) => !v)
          }}
          className="w-full border-t border-slate-100 py-3 text-[14px] font-semibold text-brand-ink"
        >
          {expanded ? 'Kamroq' : `Barchasi (${data.items.length})`}
        </button>
      )}
    </Card>
  )
}
