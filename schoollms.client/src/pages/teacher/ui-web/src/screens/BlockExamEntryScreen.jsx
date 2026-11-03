import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, CheckCircle2, Users } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import {
  analyse,
  blockExamErrorMessage,
  cellText,
  columnHint,
  columnTitle,
  creditOf,
  formatNumber,
  isWeighted,
  rowLocked,
} from '../lib/blockExams'

// Blok test natijasini kiritish — bitta (imtihon, yo'nalish) (mijoz, 2026-11-03).
//  • bir fan ikki blokda bo'lsa, o'quvchi qatorida ikkita katak chiqadi;
//  • DTM blokiga to'g'ri javoblar soni yoziladi, ballni server hisoblaydi;
//  • faqat o'zgargan qatorlar bitta so'rovda ketadi;
//  • "Kelmadi"ni administrator belgilaydi — u boshqa fanlardagi ballarni ham o'chiradi.
export default function BlockExamEntryScreen({ params, onBack }) {
  const item = params?.item
  const entryQ = useFetch(() => api.blockExamEntry(item.examId, item.ownerId), [item.examId, item.ownerId])
  const table = entryQ.data

  const [dirty, setDirty] = useState(false)
  const [leaveOpen, setLeaveOpen] = useState(false)
  const [notice, setNotice] = useState(null)

  const onDirtyChange = useCallback((d) => {
    setDirty(d)
    if (d) setNotice(null)
  }, [])

  const save = async (rows) => {
    const result = await api.saveBlockExamEntry(item.examId, { ownerId: item.ownerId, rows })
    setNotice(`${result.saved} ta o'quvchi natijasi saqlandi`)
    entryQ.reload()
  }

  const back = onBack ? () => (dirty ? setLeaveOpen(true) : onBack()) : null

  let body
  if (entryQ.loading && !table) {
    body = <Loading label="O'quvchilar yuklanmoqda…" />
  } else if (entryQ.error) {
    body = <ErrorState error={{ message: blockExamErrorMessage(entryQ.error) }} onRetry={entryQ.reload} />
  } else if (!table || table.rows.length === 0) {
    body = (
      <EmptyState
        icon={<EmptyIllustration><Users size={30} /></EmptyIllustration>}
        title="O'quvchilar yo'q"
        subtitle="Bu yo'nalishdan imtihonga o'quvchi qo'shilmagan."
      />
    )
  } else {
    body = <EntryGrid table={table} busy={entryQ.loading} notice={notice} onSave={save} onDirtyChange={onDirtyChange} />
  }

  return (
    <div className="relative h-full flex flex-col bg-bg">
      <ScreenHeader title={item.ownerName} subtitle={item.title} onBack={back} titleSize={16} />
      {body}

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

// Saqlangandan keyin ro'yxat qayta kelganda qoralamalar yangidan boshlanadi.
function EntryGrid(props) {
  const signature = useMemo(() => JSON.stringify(props.table.rows.map((r) => [r.participantId, r.status, r.scores])), [props.table])
  return <GridBody key={signature} {...props} />
}

function GridBody({ table, busy, notice, onSave, onDirtyChange }) {
  const [drafts, setDrafts] = useState({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const cells = useRef([])

  const { problems, rows } = useMemo(() => analyse(table, drafts), [table, drafts])
  const locked = saving || Boolean(busy)
  const canSave = rows.length > 0 && problems.size === 0 && !locked
  const dirty = rows.length > 0 || problems.size > 0

  useEffect(() => {
    onDirtyChange(dirty)
  }, [dirty, onDirtyChange])

  useEffect(() => {
    if (!dirty) return undefined
    const warn = (e) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const edit = (row, column, text) =>
    setDrafts((prev) => ({
      ...prev,
      [row.participantId]: { ...(prev[row.participantId] || {}), [column.sectionId]: text },
    }))

  const onKey = (e, el) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    const live = cells.current.filter(Boolean)
    const target = live[live.indexOf(el) + 1]
    if (target) target.focus()
    else el.blur()
  }

  const save = async () => {
    if (!canSave) return
    setSaving(true)
    setError(null)
    try {
      await onSave(rows)
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
          {table.columns.some(isWeighted) ? "To'g'ri javoblar sonini kiriting" : 'Ballni kiriting'}
        </p>
        {table.rows.map((row, index) => (
          <div key={row.participantId} className="mb-2.5 p-3.5 rounded-3xl bg-surface border border-border">
            <div className="flex items-center gap-2.5">
              <span className="w-5 shrink-0 text-[12px] font-bold text-faint">{index + 1}</span>
              <div className="flex-1 min-w-0">
                <p className="text-[15px] font-bold text-text truncate">{row.fullName}</p>
                {row.className && <p className="text-[12px] text-muted truncate">{row.className}</p>}
              </div>
              {row.status === 'absent' && <span className="shrink-0 text-[12px] font-bold text-muted">Kelmadi</span>}
            </div>
            {row.status !== 'absent' &&
              table.columns.map((column) => {
                const credit = creditOf(row, column)
                const problem = problems.get(`${row.participantId}:${column.sectionId}`)
                return (
                  <div key={column.sectionId} className="mt-2.5 pl-7">
                    <div className="flex items-center gap-3">
                      <div className="flex-1 min-w-0">
                        <p className="text-[13px] font-semibold text-text truncate">{columnTitle(column)}</p>
                        <p className="text-[12px] text-muted truncate">{columnHint(column)}</p>
                      </div>
                      {credit ? (
                        <span className="shrink-0 text-right text-violet-600">
                          <span className="block text-[15px] font-extrabold">{formatNumber(credit.points)}</span>
                          <span className="block max-w-[110px] truncate text-[11px]">{credit.label}</span>
                        </span>
                      ) : (
                        <input
                          ref={(el) => {
                            if (el) cells.current.push(el)
                          }}
                          value={cellText(row, column, drafts)}
                          onChange={(e) => edit(row, column, e.target.value)}
                          onKeyDown={(e) => onKey(e, e.currentTarget)}
                          onFocus={(e) => e.currentTarget.select()}
                          disabled={locked || rowLocked(row)}
                          inputMode={isWeighted(column) ? 'numeric' : 'decimal'}
                          enterKeyHint="next"
                          autoComplete="off"
                          placeholder="—"
                          aria-label={`${row.fullName}: ${columnTitle(column)}`}
                          aria-invalid={Boolean(problem)}
                          className={[
                            'w-[76px] h-11 shrink-0 rounded-xl border text-center text-[17px] font-extrabold text-text outline-none placeholder:text-faint disabled:opacity-50',
                            problem ? 'border-danger bg-danger/10' : 'border-border bg-surface2 focus:border-primary',
                          ].join(' ')}
                        />
                      )}
                    </div>
                    {problem && <p className="mt-1 text-[12px] font-semibold text-danger">{problem}</p>}
                  </div>
                )
              })}
          </div>
        ))}
      </div>

      <div className="shrink-0 border-t border-border bg-surface px-4 py-3">
        <p className="mb-2 flex items-center gap-1.5 text-[13px]">
          {error ? (
            <span className="flex items-center gap-1.5 font-semibold text-danger">
              <AlertCircle size={15} className="shrink-0" /> {error}
            </span>
          ) : problems.size > 0 ? (
            <span className="font-semibold text-danger">{problems.size} ta katakda xato — tuzatmaguncha saqlanmaydi</span>
          ) : rows.length > 0 ? (
            <span className="font-semibold text-text">{rows.length} ta o'quvchida saqlanmagan o'zgarish</span>
          ) : notice ? (
            <span className="flex items-center gap-1.5 font-semibold text-success">
              <CheckCircle2 size={15} className="shrink-0" /> {notice}
            </span>
          ) : (
            <span className="text-muted">Saqlanmagan o'zgarish yo'q</span>
          )}
        </p>
        <AppButton label={saving ? 'Saqlanmoqda…' : 'Saqlash'} expand disabled={!canSave} onClick={save} />
      </div>
    </>
  )
}
