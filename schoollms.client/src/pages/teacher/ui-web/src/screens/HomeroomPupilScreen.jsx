import { useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { ScreenHeader, SectionLabel } from '../components/ui'
import { AsyncView } from '../components/State'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

/** ISO sana → "04.11". */
const shortDate = (iso) => `${String(iso).slice(8, 10)}.${String(iso).slice(5, 7)}`
const signed = (p) => (p > 0 ? `+${p}` : p < 0 ? `−${Math.abs(p)}` : '0')
const gradeColor = (g) => (g >= 4 ? 'text-success' : g === 3 ? 'text-warning' : 'text-danger')

function seasonalPeriod(m) {
  if (m.periodKind === 'monthly') return `${String(m.month).padStart(2, '0')}/${m.year}`
  if (m.periodKind === 'quarterly') return `${m.quarter}-chorak ${m.year}`
  return `${m.year}-yil`
}

function Line({ title, note, meta, right }) {
  return (
    <div className="p-3 rounded-2xl bg-surface flex items-start gap-3" style={{ border: '1px solid var(--border)' }}>
      <div className="flex-1 min-w-0">
        <p className="text-[14.5px] font-bold text-text break-words">{title}</p>
        {note && <p className="text-[12.5px] text-text break-words mt-0.5">{note}</p>}
        {meta && <p className="text-[12px] text-muted mt-0.5">{meta}</p>}
      </div>
      {right}
    </div>
  )
}

// Sinf rahbari: o'quvchiga boshqa ustozlar qo'ygan baholar va ballar (mijoz, 2026-11-04) — faqat ko'rish.
export default function HomeroomPupilScreen({ params, onBack }) {
  const [picked, setPicked] = useState(null)
  const q = useFetch(() => api.homeroomPupilProgress(params.studentId, picked ?? undefined), [params.studentId, picked])
  const d = q.data
  const idx = d ? d.quarters.indexOf(d.quarter) : -1

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title={d?.fullName || "O'quvchi"} subtitle={d?.group} onBack={onBack} />
      <AsyncView query={q} loadingLabel="Yuklanmoqda…">
        {d && (
          <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6 space-y-2.5">
            <SectionLabel>Gamifikatsiya ballari</SectionLabel>
            <div className="p-3 rounded-2xl bg-surface" style={{ border: '1px solid var(--border)' }}>
              <p className="text-[22px] font-extrabold text-text">{d.discipline.remaining} ball</p>
              <p className="text-[12.5px] text-muted">
                100 dan boshlanadi · <span className="text-success">+{d.discipline.plus}</span> ·{' '}
                <span className="text-danger">−{d.discipline.minus}</span>
              </p>
            </div>
            {d.discipline.items.length === 0 ? (
              <p className="text-[13px] text-muted">Hozircha ball qo'yilmagan.</p>
            ) : (
              d.discipline.items.map((i) => (
                <Line
                  key={`${i.source}-${i.id}`}
                  title={i.reasonName}
                  note={i.note}
                  meta={[shortDate(i.createdAt), i.source === 'attendance' ? 'davomat' : i.createdBy].filter(Boolean).join(' · ')}
                  right={
                    <span className={`shrink-0 text-[15px] font-extrabold ${i.points < 0 ? 'text-danger' : 'text-success'}`}>
                      {signed(i.points)}
                    </span>
                  }
                />
              ))
            )}

            <div className="flex items-center justify-between pt-2">
              <button
                type="button"
                aria-label="Oldingi chorak"
                disabled={idx <= 0}
                onClick={() => setPicked(d.quarters[idx - 1])}
                className="p-2 text-text disabled:opacity-30"
              >
                <ChevronLeft size={20} />
              </button>
              <span className="text-[14px] font-bold text-text">{d.quarter}-chorak</span>
              <button
                type="button"
                aria-label="Keyingi chorak"
                disabled={idx < 0 || idx >= d.quarters.length - 1}
                onClick={() => setPicked(d.quarters[idx + 1])}
                className="p-2 text-text disabled:opacity-30"
              >
                <ChevronRight size={20} />
              </button>
            </div>

            <SectionLabel>Fanlar bo'yicha</SectionLabel>
            {d.subjects.filter((s) => s.average !== null).length === 0 ? (
              <p className="text-[13px] text-muted">Bu chorakda baho yo'q.</p>
            ) : (
              d.subjects
                .filter((s) => s.average !== null)
                .map((s) => (
                  <Line
                    key={s.subjectId}
                    title={s.subjectName}
                    right={<span className="shrink-0 text-[15px] font-extrabold text-text">{s.average.toFixed(2)}</span>}
                  />
                ))
            )}

            <SectionLabel>Baholar</SectionLabel>
            {d.grades.length === 0 ? (
              <p className="text-[13px] text-muted">Bu chorakda jurnalga baho qo'yilmagan.</p>
            ) : (
              d.grades.map((g) => (
                <Line
                  key={`${g.date}-${g.period}-${g.subjectName}`}
                  title={g.subjectName}
                  note={g.topic}
                  meta={[shortDate(g.date), `${g.period}-dars`, g.teacherName].filter(Boolean).join(' · ')}
                  right={<span className={`shrink-0 text-[17px] font-extrabold ${gradeColor(g.grade)}`}>{g.grade}</span>}
                />
              ))
            )}

            {d.seasonalMarks.length > 0 && <SectionLabel>Mavsumiy baholash</SectionLabel>}
            {d.seasonalMarks.map((m) => (
              <Line
                key={`${m.subjectName}-${m.periodKind}-${m.year}-${m.month}-${m.quarter}`}
                title={m.subjectName}
                note={m.comment}
                meta={[seasonalPeriod(m), m.authorName].filter(Boolean).join(' · ')}
                right={m.score !== null && <span className="shrink-0 text-[15px] font-extrabold text-text">{m.score}</span>}
              />
            ))}
          </div>
        )}
      </AsyncView>
    </div>
  )
}
