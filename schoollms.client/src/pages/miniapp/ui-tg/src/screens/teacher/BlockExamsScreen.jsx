/**
 * BLOK TEST NATIJALARI — o'qituvchi o'zi kiradigan yo'nalish va fan bo'yicha natija kiritadi
 * (mijoz, 2026-11-03; docs/modules/track-groups-as-classes.md).
 *
 * Tartib: imtihon·yo'nalish → o'quvchilar. Ro'yxatni server cheklaydi (dars jadvalida shu o'qituvchiga
 * biriktirilgan yo'nalish·fan) — ekran hech narsani filtrlamaydi. Bir fan ikki blokda bo'lsa, o'quvchi
 * qatorida ikkita katak chiqadi. DTM blokiga to'g'ri javoblar soni yoziladi, ballni server hisoblaydi.
 * "Kelmadi"ni administrator belgilaydi — u o'quvchining boshqa fanlardagi ballarini ham o'chiradi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { Award, Check, ChevronLeft, ChevronRight, Users } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, useKeyboardOpen } from '../../lib/telegram'
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
} from '../../lib/blockExams'
import { AsyncBlock } from './shared'

const request = (promise) =>
  promise.catch((e) => {
    throw new Error(blockExamErrorMessage(e))
  })

/** "Bugun" dagi karta — har o'qituvchida bir xil; ro'yxat dars jadvalidan chiqadi. */
export function BlockExamsCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <Award className="h-5 w-5" />
          </span>
        }
        title="Blok test natijalari"
        subtitle="O'z fan va yo'nalishingiz bo'yicha"
        right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
      />
    </Card>
  )
}

function BackPill({ label, onClick }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Orqaga"
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
    >
      <ChevronLeft className="h-4 w-4" />
      {label}
    </button>
  )
}

export function BlockExamsScreen({ onBack }) {
  const [open, setOpen] = useState(null)
  const list = useAsync(() => request(teacherApi.blockExams()), [])

  if (open) {
    return (
      <EntryScreen
        key={`${open.examId}|${open.ownerId}`}
        item={open}
        onBack={() => {
          setOpen(null)
          list.reload()
        }}
      />
    )
  }

  return (
    <Screen>
      <Hero title="Blok test natijalari" subtitle="O'z fan va yo'nalishingiz bo'yicha" right={<BackPill label="Bugun" onClick={onBack} />} />
      <AsyncBlock query={list} loadingLabel="Imtihonlar yuklanmoqda…">
        {(items) =>
          items.length === 0 ? (
            <Card>
              <EmptyState
                icon={<Award className="h-9 w-9" />}
                title="Natija kiritiladigan imtihon yo'q"
                note="Imtihon e'lon qilinganda va dars jadvalida shu yo'nalish·fan sizga biriktirilganda bu yerda chiqadi."
              />
            </Card>
          ) : (
            <Card title="Imtihonlar">
              {items.map((item) => (
                <Row
                  key={`${item.examId}|${item.ownerId}`}
                  onClick={() => {
                    haptic('light')
                    setOpen(item)
                  }}
                  lead={
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-ink text-[13px] font-extrabold text-brand">
                      {(item.ownerName || '').slice(0, 4)}
                    </span>
                  }
                  title={`${item.ownerName} · ${item.subjects.map((s) => s.name).join(', ')}`}
                  subtitle={`${item.title}${item.examDate ? ` · ${item.examDate}` : ''}`}
                  right={
                    <span className="flex items-center gap-1.5">
                      <Badge tone={item.doneCount >= item.pupilCount ? 'success' : 'neutral'}>
                        {item.doneCount}/{item.pupilCount}
                      </Badge>
                      <ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />
                    </span>
                  }
                />
              ))}
            </Card>
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

/* ------------------------------------------------------------- natija kiritish */

function EntryScreen({ item, onBack }) {
  const q = useAsync(() => request(teacherApi.blockExamEntry(item.examId, item.ownerId)), [])
  const [notice, setNotice] = useState(null)
  const [dirty, setDirty] = useState(false)
  const [leaving, setLeaving] = useState(false)

  const save = async (rows) => {
    const result = await teacherApi.saveBlockExamEntry(item.examId, { ownerId: item.ownerId, rows })
    setNotice(`${result.saved} ta o'quvchi natijasi saqlandi`)
    hapticNotify('success')
    q.reload()
  }

  const table = q.data
  const total = table?.rows?.length ?? 0
  const done = table
    ? table.rows.filter((r) => r.status === 'absent' || table.columns.every((c) => r.scores?.[c.sectionId] != null)).length
    : 0

  return (
    <Screen>
      <Hero
        title={item.ownerName}
        subtitle={item.title}
        right={<BackPill label="Imtihonlar" onClick={() => (dirty ? setLeaving(true) : onBack())} />}
      >
        <HeroProgress value={done} max={total} lead={`${done}/${total}`} note="kiritilgan" />
      </Hero>

      {leaving && (
        <Card className="ring-2 ring-red-200">
          <p className="px-4 pt-4 text-[15px] font-semibold">Saqlanmagan o'zgarishlar bor</p>
          <p className="px-4 pt-1 text-[13px] text-slate-500">Orqaga qaytsangiz, kiritilgan natijalar yo'qoladi.</p>
          <div className="flex gap-2 p-4">
            <button type="button" onClick={() => setLeaving(false)} className="flex-1 rounded-2xl bg-slate-100 py-3 text-[15px] font-semibold">
              Qolish
            </button>
            <button type="button" onClick={onBack} className="flex-1 rounded-2xl bg-red-600 py-3 text-[15px] font-semibold text-white">
              Tashlab ketish
            </button>
          </div>
        </Card>
      )}

      <AsyncBlock query={q} loadingLabel="O'quvchilar yuklanmoqda…">
        {(data) =>
          data.rows.length === 0 ? (
            <Card>
              <EmptyState icon={<Users className="h-9 w-9" />} title="O'quvchilar yo'q" note="Bu yo'nalishdan imtihonga o'quvchi qo'shilmagan." />
            </Card>
          ) : (
            <EntryGrid table={data} busy={q.loading} notice={notice} onSave={save} onDirty={setDirty} />
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

// Saqlangandan keyin ro'yxat qayta kelganda qoralamalar yangidan boshlanadi.
function EntryGrid(props) {
  const signature = useMemo(() => JSON.stringify(props.table.rows.map((r) => [r.participantId, r.status, r.scores])), [props.table])
  return <GridBody key={signature} {...props} />
}

function GridBody({ table, busy, notice, onSave, onDirty }) {
  const [drafts, setDrafts] = useState({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const cells = useRef([])
  const keyboardOpen = useKeyboardOpen()

  const { problems, rows } = useMemo(() => analyse(table, drafts), [table, drafts])
  const locked = saving || Boolean(busy)
  const canSave = rows.length > 0 && problems.size === 0 && !locked
  const dirty = rows.length > 0 || problems.size > 0

  useEffect(() => {
    onDirty(dirty)
  }, [dirty, onDirty])

  const edit = (row, column, text) =>
    setDrafts((prev) => ({
      ...prev,
      [row.participantId]: { ...(prev[row.participantId] || {}), [column.sectionId]: text },
    }))

  // "Keyingi" (Enter) — keyingi katakka: qog'ozlar ustida ketma-ket yozish uchun.
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
    haptic('medium')
    setSaving(true)
    setError(null)
    try {
      await onSave(rows)
    } catch (err) {
      setError(blockExamErrorMessage(err))
      hapticNotify('error')
    } finally {
      setSaving(false)
    }
  }

  cells.current = []

  return (
    <>
      <Card title="O'quvchilar" action={<span className="text-[12px] font-semibold text-slate-400">{table.columns.some(isWeighted) ? "to'g'ri javoblar" : 'ball'}</span>}>
        {table.rows.map((row, index) => (
          <div key={row.participantId} className="border-t border-slate-100 px-4 py-2.5">
            <div className="flex items-center gap-3">
              <span className="w-5 shrink-0 text-[12px] font-bold text-slate-400">{index + 1}</span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-[15px] font-semibold">{row.fullName}</p>
                {row.className && <p className="truncate text-[12px] text-slate-400">{row.className}</p>}
              </div>
              {row.status === 'absent' && <Badge>Kelmadi</Badge>}
            </div>
            {row.status !== 'absent' && (
              <div className="mt-2 space-y-2 pl-8">
                {table.columns.map((column) => {
                  const credit = creditOf(row, column)
                  const key = `${row.participantId}:${column.sectionId}`
                  const problem = problems.get(key)
                  return (
                    <div key={column.sectionId}>
                      <div className="flex items-center gap-3">
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-[13px] font-semibold text-slate-700">{columnTitle(column)}</p>
                          <p className="truncate text-[12px] text-slate-400">{columnHint(column)}</p>
                        </div>
                        {credit ? (
                          <span className="shrink-0 text-right">
                            <span className="block text-[15px] font-extrabold text-violet-700">{formatNumber(credit.points)}</span>
                            <span className="block max-w-[110px] truncate text-[11px] text-violet-600">{credit.label}</span>
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
                            className={
                              'h-11 w-[76px] shrink-0 rounded-xl border text-center text-[17px] font-extrabold outline-none placeholder:text-slate-300 disabled:opacity-50 ' +
                              (problem ? 'border-red-400 bg-red-50' : 'border-slate-200 bg-slate-50 focus:border-brand-ink')
                            }
                          />
                        )}
                      </div>
                      {problem && <p className="mt-1 text-[12px] font-semibold text-red-600">{problem}</p>}
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        ))}
      </Card>

      {/* Yopishqoq panel — SeasonalScreen'dagidek: ro'yxat uzun bo'lsa ham "Saqlash" ko'rinadi. */}
      <div className="h-36" />
      <div
        className={
          'fixed inset-x-0 z-10 border-t border-slate-200 bg-white px-4 py-3 ' +
          (keyboardOpen ? 'bottom-0' : 'bottom-[calc(76px+var(--tg-safe-bottom))]')
        }
      >
        <p className="mb-2 text-[13px]">
          {error ? (
            <span className="font-semibold text-red-600">{error}</span>
          ) : problems.size > 0 ? (
            <span className="font-semibold text-red-600">{problems.size} ta katakda xato — tuzatmaguncha saqlanmaydi</span>
          ) : rows.length > 0 ? (
            <span className="font-semibold text-slate-700">{rows.length} ta o'quvchida saqlanmagan o'zgarish</span>
          ) : notice ? (
            <span className="flex items-center gap-1.5 font-semibold text-emerald-700">
              <Check className="h-4 w-4" /> {notice}
            </span>
          ) : (
            <span className="text-slate-400">Saqlanmagan o'zgarish yo'q</span>
          )}
        </p>
        <button
          type="button"
          onClick={save}
          disabled={!canSave}
          className="w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
        >
          {saving ? 'Saqlanmoqda…' : 'Saqlash'}
        </button>
      </div>
    </>
  )
}
