/**
 * SINFIM · BAHOLAR VA BALLAR (mijoz, 2026-11-04: "ota-onalar ball nimaga qo'yilganini sinf rahbaridan
 * so'raydi") — o'quvchini boshqa ustozlar qo'ygan baholari va ballari, kim qo'ygani bilan. Faqat ko'rish;
 * raqamlar ota-ona kabinetidagi bilan bir xil (`GET /api/teacher/homeroom/pupils/{id}`).
 */
import { useState } from 'react'
import { Badge, Card, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { teacherApi } from '../../lib/teacherApi'
import { gradeTone } from '../parent/shared'

/** `Barchasi` bosilguncha ko'rinadigan qatorlar. */
const PREVIEW = 5

function signed(p) {
  if (p > 0) return `+${p}`
  if (p < 0) return `−${Math.abs(p)}`
  return '0'
}

/** Mavsumiy baho davri: "2026-yil oktabr" o'rniga qisqa — "10/2026", "2-chorak 2026", "2026-yil". */
function seasonalPeriod(m) {
  if (m.periodKind === 'monthly') return `${String(m.month).padStart(2, '0')}/${m.year}`
  if (m.periodKind === 'quarterly') return `${m.quarter}-chorak ${m.year}`
  return `${m.year}-yil`
}

function MoreButton({ expanded, count, onToggle }) {
  if (count <= PREVIEW) return null
  return (
    <button
      type="button"
      onClick={() => {
        haptic('light')
        onToggle()
      }}
      className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
    >
      {expanded ? 'Kamroq' : `Barchasi (${count})`}
    </button>
  )
}

export function PupilProgressCards({ studentId }) {
  // `null` — joriy chorak: server o'zi aniqlaydi va javobda qaytaradi.
  const [picked, setPicked] = useState(null)
  const [allGrades, setAllGrades] = useState(false)
  const [allPoints, setAllPoints] = useState(false)
  const q = useAsync(() => teacherApi.homeroomPupilProgress(studentId, picked ?? undefined), [studentId, picked])
  const d = q.data

  if (q.error) {
    return (
      <Card title="Baholar va ballar">
        <p className="px-4 pb-4 text-[14px] text-red-600">{q.error}</p>
      </Card>
    )
  }
  if (!d) {
    return (
      <Card title="Baholar va ballar">
        <p className="px-4 pb-4 text-[14px] text-slate-400">Yuklanmoqda…</p>
      </Card>
    )
  }

  const idx = d.quarters.indexOf(d.quarter)
  const results = d.subjects.filter((s) => s.average !== null)
  const grades = allGrades ? d.grades : d.grades.slice(0, PREVIEW)
  const points = d.discipline.items
  const shownPoints = allPoints ? points : points.slice(0, PREVIEW)
  const scoreTone = d.discipline.remaining < 100 ? 'text-red-600' : d.discipline.remaining > 100 ? 'text-emerald-600' : 'text-brand-ink'

  return (
    <>
      <Card
        title="Gamifikatsiya ballari"
        action={<MoreButton expanded={allPoints} count={points.length} onToggle={() => setAllPoints((v) => !v)} />}
      >
        <div className="px-4 pb-3 pt-1">
          <p className={'text-[24px] font-extrabold leading-tight ' + scoreTone}>{d.discipline.remaining} ball</p>
          <p className="text-[13px] text-slate-500">
            100 dan boshlanadi · <span className="text-emerald-600">+{d.discipline.plus}</span> ·{' '}
            <span className="text-red-600">−{d.discipline.minus}</span>
          </p>
        </div>
        {points.length === 0 ? (
          <p className="border-t border-slate-100 px-4 py-3 text-[13px] text-slate-400">Hozircha ball qo'yilmagan.</p>
        ) : (
          shownPoints.map((i) => (
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
                {i.note && <p className="mt-0.5 break-words text-[13px] text-slate-600">{i.note}</p>}
                <p className="mt-0.5 text-[12px] text-slate-400">
                  {[dayMonth(i.createdAt), i.source === 'attendance' ? 'davomat' : i.createdBy].filter(Boolean).join(' · ')}
                </p>
              </div>
            </div>
          ))
        )}
      </Card>

      <Card>
        <div className="px-2 py-2">
          <Stepper
            label={`${d.quarter}-chorak`}
            onPrev={() => idx > 0 && setPicked(d.quarters[idx - 1])}
            onNext={() => idx < d.quarters.length - 1 && setPicked(d.quarters[idx + 1])}
            disabledPrev={idx <= 0}
            disabledNext={idx < 0 || idx >= d.quarters.length - 1}
          />
        </div>
      </Card>

      <Card title="Fanlar bo'yicha">
        {results.length === 0 ? (
          <p className="px-4 pb-4 text-[13px] text-slate-400">Bu chorakda baho yo'q.</p>
        ) : (
          results.map((s) => (
            <div key={s.subjectId} className="flex items-center justify-between gap-3 border-t border-slate-100 px-4 py-2.5 first:border-t-0">
              <span className="min-w-0 break-words text-[14px] text-slate-700">{s.subjectName}</span>
              <span className="shrink-0 text-[15px] font-bold">{s.average.toFixed(2)}</span>
            </div>
          ))
        )}
      </Card>

      <Card
        title="Baholar"
        action={<MoreButton expanded={allGrades} count={d.grades.length} onToggle={() => setAllGrades((v) => !v)} />}
      >
        {d.grades.length === 0 ? (
          <p className="px-4 pb-4 text-[13px] text-slate-400">Bu chorakda jurnalga baho qo'yilmagan.</p>
        ) : (
          grades.map((g) => (
            <div key={`${g.date}-${g.period}-${g.subjectName}`} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
              <div className="min-w-0 flex-1">
                <p className="break-words text-[15px] font-semibold leading-snug">{g.subjectName}</p>
                {g.topic && <p className="mt-0.5 break-words text-[13px] text-slate-600">{g.topic}</p>}
                <p className="mt-0.5 text-[12px] text-slate-400">
                  {[dayMonth(g.date), `${g.period}-dars`, g.teacherName].filter(Boolean).join(' · ')}
                </p>
              </div>
              <Badge tone={gradeTone(g.grade)}>{g.grade}</Badge>
            </div>
          ))
        )}
      </Card>

      {d.seasonalMarks.length > 0 && (
        <Card title="Mavsumiy baholash">
          {d.seasonalMarks.map((m) => (
            <div key={`${m.subjectName}-${m.periodKind}-${m.year}-${m.month}-${m.quarter}`} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
              <div className="min-w-0 flex-1">
                <p className="break-words text-[15px] font-semibold leading-snug">{m.subjectName}</p>
                {m.comment && <p className="mt-0.5 break-words text-[13px] text-slate-600">{m.comment}</p>}
                <p className="mt-0.5 text-[12px] text-slate-400">
                  {[seasonalPeriod(m), m.authorName].filter(Boolean).join(' · ')}
                </p>
              </div>
              {m.score !== null && <span className="shrink-0 text-[15px] font-bold">{m.score}</span>}
            </div>
          ))}
        </Card>
      )}
    </>
  )
}
