/**
 * OYLIK O'SISH VA TEST NATIJALARI — "Baholar" tabida (mijoz, 2026-11-03; docs/modules/parent-exam-results.md).
 *
 * Hamma hisobni server qiladi (`/api/student/exam-results`), ekran faqat ko'rsatadi. Natija kiritilishi
 * bilan ko'rinadi; hamma blok kiritilmaguncha jami va foiz chiqmaydi. O'zi yuklanadi, shuning uchun
 * xato bo'lsa ham baholar tabi ishlayveradi.
 *
 * Mijoz, 2026-11-04: 3–8-sinflarda faqat "Oylik o'sish" (oylik testlar mavsumiy baholashda); yo'nalish
 * guruhlarida ikkala karta — blok test "Oylik o'sish"ga ham, "Test natijalari"ga ham tushadi.
 */
import { useState } from 'react'
import { ChevronDown, ChevronUp, ClipboardCheck, TrendingDown, TrendingUp } from 'lucide-react'
import { Badge, Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { fullDate, monthTitle } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getExamResults } from '../../lib/parentApi'

/** "Test natijalari" yopiq turganda ko'rinadigan qatorlar soni. */
const PREVIEW = 4

const num = (v) => String(Number(Number(v).toFixed(2))).replace('.', ',')

function monthLabel(key) {
  const [year, month] = key.split('-').map(Number)
  return monthTitle(year, month - 1)
}

function Change({ value }) {
  if (value === null || value === undefined) return null
  const n = Number(value)
  if (n === 0) return <span className="text-[13px] font-semibold text-slate-400">o'zgarishsiz</span>
  const up = n > 0
  const Icon = up ? TrendingUp : TrendingDown
  return (
    <span className={'inline-flex items-center gap-1 text-[13px] font-semibold ' + (up ? 'text-emerald-600' : 'text-red-600')}>
      <Icon className="h-3.5 w-3.5" />
      {up ? '+' : '−'}
      {num(Math.abs(n))}
    </span>
  )
}

function ToggleAll({ expanded, count, onClick }) {
  return (
    <button
      type="button"
      onClick={() => {
        haptic('light')
        onClick()
      }}
      className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
    >
      {expanded ? 'Kamroq' : `Barchasi (${count})`}
    </button>
  )
}

function Metric({ label, value, change }) {
  return (
    <div className="flex items-center justify-between gap-3 pt-1">
      <span className="text-[13px] text-slate-500">{label}</span>
      <span className="flex items-center gap-2">
        <Change value={change} />
        <span className="text-[15px] font-bold tabular-nums">{value}</span>
      </span>
    </div>
  )
}

function MonthRow({ month }) {
  const [open, setOpen] = useState(false)
  const notes = [
    month.testCount > 0 && `${month.testCount} ta test`,
    month.pendingCount > 0 && `${month.pendingCount} ta test hali kiritilmoqda`,
    month.absentCount > 0 && `${month.absentCount} ta testga kelmagan`,
  ].filter(Boolean)
  const Icon = open ? ChevronUp : ChevronDown

  return (
    <div className="border-t border-slate-100 first:border-t-0">
      <button
        type="button"
        onClick={() => {
          haptic('light')
          setOpen((v) => !v)
        }}
        aria-expanded={open}
        className="w-full px-4 py-3 text-left"
      >
        <span className="flex items-center justify-between gap-3">
          <span className="text-[15px] font-semibold">{monthLabel(month.month)}</span>
          {month.subjects.length > 0 && <Icon className="h-4 w-4 shrink-0 text-slate-400" />}
        </span>
        {month.testPercent !== null && <Metric label="Testlar o'rtachasi" value={`${num(month.testPercent)}%`} change={month.testChange} />}
        {month.markAverage !== null && <Metric label="Oylik baholar o'rtachasi" value={num(month.markAverage)} change={month.markChange} />}
        {notes.length > 0 && <span className="mt-1 block text-[12px] text-slate-400">{notes.join(' · ')}</span>}
      </button>
      {open && month.subjects.length > 0 && (
        <div className="bg-slate-50 px-4 py-2">
          {month.subjects.map((s) => (
            <div key={s.subjectId} className="flex items-start justify-between gap-3 py-1.5 text-[13px]">
              <span className="min-w-0 break-words text-slate-700">{s.name}</span>
              <span className="shrink-0 text-right font-semibold tabular-nums text-slate-800">
                {s.testPercent !== null && (
                  <span className="flex items-center justify-end gap-2">
                    <Change value={s.testChange} />
                    {num(s.testPercent)}%
                  </span>
                )}
                {s.mark !== null && (
                  <span className="flex items-center justify-end gap-2">
                    <Change value={s.markChange} />
                    {num(s.mark)} baho
                  </span>
                )}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function ExamRow({ exam }) {
  const [open, setOpen] = useState(false)
  const canOpen = exam.blocks.length > 0
  const Tag = canOpen ? 'button' : 'div'
  const Icon = open ? ChevronUp : ChevronDown

  return (
    <div className="border-t border-slate-100 first:border-t-0">
      <Tag
        type={canOpen ? 'button' : undefined}
        aria-expanded={canOpen ? open : undefined}
        onClick={
          canOpen
            ? () => {
                haptic('light')
                setOpen((v) => !v)
              }
            : undefined
        }
        className="flex w-full items-center gap-3 px-4 py-3 text-left"
      >
        <span className="min-w-0 flex-1">
          <span className="block break-words text-[15px] font-semibold leading-snug">{exam.title}</span>
          <span className="mt-0.5 block text-[13px] text-slate-500">
            {[exam.typeName, exam.examDate ? fullDate(exam.examDate) : null].filter(Boolean).join(' · ')}
          </span>
        </span>
        {exam.isAbsent ? (
          <Badge tone="danger">Kelmagan</Badge>
        ) : exam.isComplete ? (
          <span className="shrink-0 text-right">
            <span className="block text-[15px] font-bold tabular-nums">{num(exam.percent)}%</span>
            <span className="block text-[12px] tabular-nums text-slate-500">
              {num(exam.totalPoints)} / {num(exam.maxPoints)}
            </span>
          </span>
        ) : (
          <Badge>Kiritilmoqda</Badge>
        )}
        {canOpen && <Icon className="h-4 w-4 shrink-0 text-slate-400" />}
      </Tag>
      {open && (
        <div className="bg-slate-50 px-4 py-2">
          {exam.blocks.map((b) => (
            <div key={b.block} className="flex items-start justify-between gap-3 py-1.5 text-[13px]">
              <span className="min-w-0 break-words text-slate-700">
                {b.block}-blok · {b.subjectName}
                {b.certificate && <span className="block text-[12px] text-violet-600">Sertifikat bilan ozod: {b.certificate}</span>}
              </span>
              <span className="shrink-0 font-semibold tabular-nums text-slate-800">
                {b.points === null ? <span className="font-normal text-slate-400">kiritilmagan</span> : `${num(b.points)} / ${num(b.maxPoints)}`}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

export function ExamResultsCards({ childId }) {
  const q = useAsync(() => getExamResults(childId), [childId])
  const [allExams, setAllExams] = useState(false)

  if (q.error) {
    return (
      <Card title="Oylik o'sish">
        <p className="px-4 pb-4 pt-1 text-[13px] text-slate-500">Yuklab bo'lmadi. Ilovani qayta ochib ko'ring.</p>
      </Card>
    )
  }

  const months = q.data?.months ?? []
  const exams = q.data?.exams ?? []

  return (
    <>
      {/* Hamma oy doim ochiq (mijoz, 2026-11-04): ota-ona oylarni bir-biriga solishtiradi. */}
      <Card title="Oylik o'sish">
        {months.length === 0 ? (
          <EmptyState
            icon={<TrendingUp className="h-8 w-8" />}
            title={q.loading ? 'Yuklanmoqda…' : "Hozircha hisob yo'q"}
            note={q.loading ? null : "Birinchi natijalar kiritilgach, har oy shu yerda hisoblanadi."}
          />
        ) : (
          months.map((m) => <MonthRow key={m.month} month={m} />)
        )}
      </Card>

      {/* Blok test faqat yo'nalish guruhlaridan olinadi (mijoz, 2026-11-04) — server aytadi. */}
      {q.data?.showTests && (
        <Card
          title="Test natijalari"
          action={exams.length > PREVIEW ? <ToggleAll expanded={allExams} count={exams.length} onClick={() => setAllExams((v) => !v)} /> : null}
        >
          {exams.length === 0 ? (
            <EmptyState
              icon={<ClipboardCheck className="h-8 w-8" />}
              title="Hozircha natija yo'q"
              note="Blok test natijasi kiritilishi bilan shu yerda ko'rinadi."
            />
          ) : (
            (allExams ? exams : exams.slice(0, PREVIEW)).map((e) => <ExamRow key={e.examId} exam={e} />)
          )}
        </Card>
      )}
    </>
  )
}
