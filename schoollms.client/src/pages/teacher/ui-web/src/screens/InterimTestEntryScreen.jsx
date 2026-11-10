import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, CheckCircle2, Pencil, Trash2, Users } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { blockExamErrorMessage } from '../lib/blockExams'
import { TestForm, fmt } from './InterimTestsScreen'

// Oraliq test natijasini kiritish (mijoz, 2026-11-10): har o'quvchiga to'g'ri javoblar soni yoki
// "Kelmadi". Foiz o'zi hisoblanadi (to'g'ri / savollar soni). Faqat o'zgargan qatorlar ketadi.
export default function InterimTestEntryScreen({ params, onBack }) {
  const id = params?.id
  const entryQ = useFetch(() => api.interimEntry(id), [id])
  const options = useFetch(() => api.interimOptions(), [])
  const entry = entryQ.data

  const [dirty, setDirty] = useState(false)
  const [leaveOpen, setLeaveOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [notice, setNotice] = useState(null)
  const [deleteError, setDeleteError] = useState(null)

  const back = onBack ? () => (dirty ? setLeaveOpen(true) : onBack()) : null

  const onDirtyChange = useCallback((d) => {
    setDirty(d)
    if (d) setNotice(null)
  }, [])

  const remove = async () => {
    setDeleteError(null)
    try {
      await api.deleteInterimTest(id)
      onBack?.()
    } catch (err) {
      setDeleteError(blockExamErrorMessage(err))
    }
  }

  let body
  if (entryQ.loading && !entry) {
    body = <Loading label="O'quvchilar yuklanmoqda…" />
  } else if (entryQ.error) {
    body = <ErrorState error={{ message: blockExamErrorMessage(entryQ.error) }} onRetry={entryQ.reload} />
  } else if (!entry || entry.rows.length === 0) {
    body = (
      <EmptyState
        icon={<EmptyIllustration><Users size={30} /></EmptyIllustration>}
        title="O'quvchilar yo'q"
        subtitle="Test kuni bu yo'nalishda o'quvchi bo'lmagan."
      />
    )
  } else {
    const signature = JSON.stringify(entry.rows.map((r) => [r.studentId, r.correct, r.absent]))
    body = (
      <EntryGrid
        key={signature}
        entry={entry}
        busy={entryQ.loading}
        notice={notice}
        onDirtyChange={onDirtyChange}
        onSave={async (rows) => {
          const result = await api.saveInterimEntry(id, rows)
          setNotice(`${result.saved} ta o'quvchi natijasi saqlandi`)
          entryQ.reload()
        }}
      />
    )
  }

  const t = entry?.test
  return (
    <div className="relative h-full flex flex-col bg-bg">
      <ScreenHeader
        title={t ? `${t.groupName} · ${t.subjectName}` : 'Oraliq test'}
        subtitle={t ? `${t.number}-oraliq test · ${t.date.split('-').reverse().join('.')} · ${t.questionCount} savol` : ''}
        onBack={back}
        titleSize={16}
        trailing={
          t && (
            <div className="flex gap-1">
              <button onClick={() => setEditOpen(true)} className="w-10 h-10 rounded-full flex items-center justify-center text-muted" aria-label="Tahrirlash">
                <Pencil size={18} />
              </button>
              <button onClick={() => setDeleteOpen(true)} className="w-10 h-10 rounded-full flex items-center justify-center text-danger" aria-label="O'chirish">
                <Trash2 size={18} />
              </button>
            </div>
          )
        }
      />
      {body}

      <AppSheet open={editOpen} onClose={() => setEditOpen(false)} title="Testni tahrirlash">
        {editOpen && t && (
          <TestForm
            options={options.data || []}
            test={t}
            onCancel={() => setEditOpen(false)}
            onSaved={() => {
              setEditOpen(false)
              entryQ.reload()
            }}
          />
        )}
      </AppSheet>

      <AppSheet open={deleteOpen} onClose={() => setDeleteOpen(false)} title="Testni o'chirish">
        <div className="px-5 pb-6">
          <p className="text-[14px] text-muted leading-relaxed">Test va unga kiritilgan barcha natijalar o'chiriladi.</p>
          {deleteError && <p className="mt-2 text-[13px] font-semibold text-danger">{deleteError}</p>}
          <div className="mt-5 flex gap-2.5">
            <AppButton label="Bekor qilish" style="ghost" expand onClick={() => setDeleteOpen(false)} />
            <div className="flex-[2]">
              <AppButton label="O'chirish" style="danger" expand onClick={remove} />
            </div>
          </div>
        </div>
      </AppSheet>

      <AppSheet open={leaveOpen} onClose={() => setLeaveOpen(false)} title="Saqlanmagan o'zgarishlar">
        <div className="px-5 pb-6">
          <p className="text-[14px] text-muted leading-relaxed">
            Kiritilgan natijalar hali saqlanmagan. Orqaga qaytsangiz, ular yo'qoladi.
          </p>
          <div className="mt-5 flex gap-2.5">
            <AppButton label="Qolish" style="ghost" expand onClick={() => setLeaveOpen(false)} />
            <div className="flex-[2]">
              <AppButton label="Tashlab ketish" style="danger" expand onClick={() => { setLeaveOpen(false); onBack?.() }} />
            </div>
          </div>
        </div>
      </AppSheet>
    </div>
  )
}

function EntryGrid({ entry, busy, notice, onSave, onDirtyChange }) {
  const max = entry.test.questionCount
  const [drafts, setDrafts] = useState(() =>
    Object.fromEntries(entry.rows.map((r) => [r.studentId, { correct: r.correct == null ? '' : String(r.correct), absent: r.absent }])),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const cells = useRef([])

  const changed = useMemo(
    () =>
      entry.rows.filter((r) => {
        const d = drafts[r.studentId]
        const typed = d.correct === '' ? null : Number(d.correct)
        return d.absent !== r.absent || (!d.absent && typed !== r.correct)
      }),
    [entry, drafts],
  )
  const invalid = changed.filter((r) => !drafts[r.studentId].absent && Number(drafts[r.studentId].correct) > max).length
  const locked = saving || Boolean(busy)
  const dirty = changed.length > 0

  useEffect(() => {
    onDirtyChange(dirty)
  }, [dirty, onDirtyChange])

  useEffect(() => {
    if (!dirty) return undefined
    const warn = (e) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const set = (id, patch) => setDrafts((p) => ({ ...p, [id]: { ...p[id], ...patch } }))

  const onKey = (e, el) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    const live = cells.current.filter(Boolean)
    const target = live[live.indexOf(el) + 1]
    if (target) target.focus()
    else el.blur()
  }

  const save = async () => {
    if (!dirty || invalid > 0 || locked) return
    setSaving(true)
    setError(null)
    try {
      await onSave(
        changed.map((r) => {
          const d = drafts[r.studentId]
          return { studentId: r.studentId, absent: d.absent, correct: d.absent || d.correct === '' ? null : Number(d.correct) }
        }),
      )
    } catch (err) {
      setError(blockExamErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  cells.current = []

  return (
    <>
      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-4">
        <p className="px-1 pb-2 text-[12px] font-bold text-muted tracking-wide">
          To'g'ri javoblar sonini kiriting (0–{max}) — foiz o'zi hisoblanadi
        </p>
        {entry.rows.map((row, index) => {
          const d = drafts[row.studentId]
          const n = d.correct === '' ? null : Number(d.correct)
          const over = n != null && n > max
          const pct = !d.absent && n != null && !over ? Math.round((n * 1000) / max) / 10 : null
          return (
            <div key={row.studentId} className="mb-2.5 p-3.5 rounded-3xl bg-surface border border-border">
              <div className="flex items-center gap-2.5">
                <span className="w-5 shrink-0 text-[12px] font-bold text-faint">{index + 1}</span>
                <div className="flex-1 min-w-0">
                  <p className="text-[15px] font-bold text-text truncate">{row.fullName}</p>
                  <p className="text-[12px] text-muted truncate">
                    {row.className}
                    {pct != null && <span className={`ml-1.5 font-bold ${tone(pct)}`}>· {fmt(pct)}%</span>}
                  </p>
                </div>
                <input
                  ref={(el) => {
                    if (el) cells.current.push(el)
                  }}
                  value={d.absent ? '' : d.correct}
                  onChange={(e) => set(row.studentId, { correct: e.target.value.replace(/\D/g, '').slice(0, 3) })}
                  onKeyDown={(e) => onKey(e, e.currentTarget)}
                  onFocus={(e) => e.currentTarget.select()}
                  disabled={locked || d.absent}
                  inputMode="numeric"
                  enterKeyHint="next"
                  autoComplete="off"
                  placeholder="—"
                  aria-label={`${row.fullName}: to'g'ri javoblar`}
                  aria-invalid={over}
                  className={[
                    'w-[64px] h-11 shrink-0 rounded-xl border text-center text-[17px] font-extrabold text-text outline-none placeholder:text-faint disabled:opacity-50',
                    over ? 'border-danger bg-danger/10' : 'border-border bg-surface2 focus:border-primary',
                  ].join(' ')}
                />
                <button
                  onClick={() => set(row.studentId, { absent: !d.absent })}
                  disabled={locked}
                  className={[
                    'h-11 shrink-0 px-2.5 rounded-xl border text-[12px] font-bold',
                    d.absent ? 'border-danger bg-danger/10 text-danger' : 'border-border text-muted',
                  ].join(' ')}
                >
                  Kelmadi
                </button>
              </div>
              {over && <p className="mt-1 pl-7 text-[12px] font-semibold text-danger">{max} tadan oshmasin</p>}
            </div>
          )
        })}
      </div>

      <div className="shrink-0 border-t border-border bg-surface px-4 py-3">
        <p className="mb-2 flex items-center gap-1.5 text-[13px]">
          {error ? (
            <span className="flex items-center gap-1.5 font-semibold text-danger">
              <AlertCircle size={15} className="shrink-0" /> {error}
            </span>
          ) : invalid > 0 ? (
            <span className="font-semibold text-danger">{invalid} ta katakda xato — tuzatmaguncha saqlanmaydi</span>
          ) : dirty ? (
            <span className="font-semibold text-text">{changed.length} ta o'quvchida saqlanmagan o'zgarish</span>
          ) : notice ? (
            <span className="flex items-center gap-1.5 font-semibold text-success">
              <CheckCircle2 size={15} className="shrink-0" /> {notice}
            </span>
          ) : (
            <span className="text-muted">Saqlanmagan o'zgarish yo'q</span>
          )}
        </p>
        <AppButton label={saving ? 'Saqlanmoqda…' : 'Saqlash'} expand disabled={!dirty || invalid > 0 || locked} onClick={save} />
      </div>
    </>
  )
}

function tone(pct) {
  if (pct >= 80) return 'text-success'
  if (pct >= 60) return 'text-amber-600'
  return 'text-danger'
}
