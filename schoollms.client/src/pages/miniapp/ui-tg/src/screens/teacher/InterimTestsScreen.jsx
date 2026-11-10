/**
 * ORALIQ TESTLAR — yo'nalishlarning haftalik testi (mijoz, 2026-11-10). Oyda uchta oraliq test,
 * to'rtinchisi oylik blok test. O'qituvchi savollar sonini o'zi belgilaydi va har o'quvchining to'g'ri
 * javoblarini kiritadi; natija foizda (to'g'ri / savollar soni). Ro'yxatni server cheklaydi: dars
 * jadvalida shu o'qituvchiga biriktirilgan yo'nalish·fan. Veb paneldagi (/teacher) ekranning nusxasi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronLeft, ChevronRight, ClipboardList, Plus, Users } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, useKeyboardOpen } from '../../lib/telegram'
import { blockExamErrorMessage } from '../../lib/blockExams'
import { AsyncBlock } from './shared'

const request = (promise) =>
  promise.catch((e) => {
    throw new Error(blockExamErrorMessage(e))
  })

const MONTHS = ['yan', 'fev', 'mar', 'apr', 'may', 'iyn', 'iyl', 'avg', 'sen', 'okt', 'noy', 'dek']
const fmt = (v) => String(v).replace('.', ',')
const fmtDay = (iso) => iso.split('-').reverse().join('.')

function today() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function pctTone(v) {
  if (v == null) return 'text-slate-400'
  if (v >= 80) return 'text-emerald-600'
  if (v >= 60) return 'text-amber-600'
  return 'text-red-600'
}

/** "Bugun" dagi karta. */
export function InterimTestsCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <ClipboardList className="h-5 w-5" />
          </span>
        }
        title="Oraliq testlar"
        subtitle="Yo'nalishlarning haftalik testi"
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

export function InterimTestsScreen({ onBack }) {
  const [open, setOpen] = useState(null)
  const [creating, setCreating] = useState(false)
  const list = useAsync(() => request(teacherApi.interimTests()), [])
  const options = useAsync(() => request(teacherApi.interimOptions()), [])
  const canCreate = (options.data || []).length > 0

  if (open) {
    return (
      <EntryScreen
        key={open}
        id={open}
        options={options.data || []}
        onBack={() => {
          setOpen(null)
          list.reload()
        }}
      />
    )
  }

  return (
    <Screen>
      <Hero title="Oraliq testlar" subtitle="Yo'nalishlarning haftalik testi" right={<BackPill label="Bugun" onClick={onBack} />} />

      {creating ? (
        <TestForm
          options={options.data || []}
          onCancel={() => setCreating(false)}
          onSaved={(t) => {
            setCreating(false)
            list.reload()
            setOpen(t.id)
          }}
        />
      ) : (
        canCreate && (
          <div className="px-4 pt-4">
            <button
              type="button"
              onClick={() => {
                haptic('light')
                setCreating(true)
              }}
              className="flex w-full items-center justify-center gap-2 rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink"
            >
              <Plus className="h-5 w-5" /> Yangi oraliq test
            </button>
          </div>
        )
      )}

      <AsyncBlock query={list} loadingLabel="Testlar yuklanmoqda…">
        {(items) =>
          items.length === 0 ? (
            <Card>
              <EmptyState
                icon={<ClipboardList className="h-9 w-9" />}
                title="Hali oraliq test yo'q"
                note={
                  canCreate
                    ? "«Yangi oraliq test» bilan qo'shing: yo'nalish, fan, sana va savollar soni."
                    : "Dars jadvalida sizga yo'nalish darsi biriktirilganda bu yerda test qo'sha olasiz."
                }
              />
            </Card>
          ) : (
            <Card title="Testlar">
              {items.map((t) => (
                <Row
                  key={t.id}
                  onClick={() => {
                    haptic('light')
                    setOpen(t.id)
                  }}
                  lead={
                    <span className="flex h-11 w-11 shrink-0 flex-col items-center justify-center rounded-xl bg-brand-ink text-brand">
                      <span className="text-[15px] font-extrabold leading-none">{t.date.slice(8, 10)}</span>
                      <span className="text-[10px] font-bold uppercase">{MONTHS[Number(t.date.slice(5, 7)) - 1]}</span>
                    </span>
                  }
                  title={`${t.groupName} · ${t.subjectName}`}
                  subtitle={`${t.number}-oraliq test · ${t.questionCount} savol${t.topic ? ` · ${t.topic}` : ''}`}
                  right={
                    <span className="flex items-center gap-1.5">
                      <span className="text-right">
                        <span className={`block text-[14px] font-extrabold ${pctTone(t.averagePercent)}`}>
                          {t.averagePercent == null ? '—' : `${fmt(t.averagePercent)}%`}
                        </span>
                        <Badge tone={t.doneCount >= t.pupilCount && t.pupilCount > 0 ? 'success' : 'neutral'}>
                          {t.doneCount}/{t.pupilCount}
                        </Badge>
                      </span>
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

/* ------------------------------------------------------------- test qo'shish / tahrirlash */

const field =
  'h-12 w-full rounded-xl border border-slate-200 bg-slate-50 px-3 text-[15px] outline-none focus:border-brand-ink disabled:opacity-60'

function TestForm({ options, test, onCancel, onSaved }) {
  const subjectsOf = (id) => options.find((o) => o.groupId === id)?.subjects || []
  const [groupId, setGroupId] = useState(test?.groupId || options[0]?.groupId || '')
  const [subjectId, setSubjectId] = useState(test?.subjectId || (subjectsOf(groupId).length === 1 ? subjectsOf(groupId)[0].id : ''))
  const [date, setDate] = useState(test?.date || today())
  const [count, setCount] = useState(test ? String(test.questionCount) : '')
  const [topic, setTopic] = useState(test?.topic || '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const save = async () => {
    haptic('medium')
    setBusy(true)
    setError(null)
    try {
      const body = { groupId, subjectId, date, questionCount: Number(count), topic: topic.trim() || null }
      const saved = test ? await teacherApi.updateInterimTest(test.id, body) : await teacherApi.createInterimTest(body)
      hapticNotify('success')
      onSaved(saved)
    } catch (err) {
      setError(blockExamErrorMessage(err))
      hapticNotify('error')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card title={test ? 'Testni tahrirlash' : 'Yangi oraliq test'}>
      <div className="space-y-3 px-4 pb-4">
        {!test && (
          <>
            <Field label="Yo'nalish">
              <select
                value={groupId}
                onChange={(e) => {
                  setGroupId(e.target.value)
                  const next = subjectsOf(e.target.value)
                  setSubjectId(next.length === 1 ? next[0].id : '')
                }}
                className={field}
              >
                {options.map((o) => (
                  <option key={o.groupId} value={o.groupId}>
                    {o.groupName}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Fan">
              <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)} className={field}>
                <option value="">— Tanlang —</option>
                {subjectsOf(groupId).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </Field>
          </>
        )}
        <div className="grid grid-cols-2 gap-3">
          <Field label="Sana">
            <input type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} className={field} />
          </Field>
          <Field label="Savollar soni">
            <input
              value={count}
              inputMode="numeric"
              placeholder="Masalan: 20"
              onChange={(e) => setCount(e.target.value.replace(/\D/g, '').slice(0, 3))}
              className={field}
            />
          </Field>
        </div>
        <Field label="Mavzu (ixtiyoriy)">
          <input value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="Masalan: Present Perfect" className={field} />
        </Field>
        {error && <p className="text-[13px] font-semibold text-red-600">{error}</p>}
        <div className="flex gap-2 pt-1">
          <button type="button" onClick={onCancel} className="flex-1 rounded-2xl bg-slate-100 py-3 text-[15px] font-semibold">
            Bekor qilish
          </button>
          <button
            type="button"
            onClick={save}
            disabled={busy || !subjectId || !count || !date}
            className="flex-[2] rounded-2xl bg-brand py-3 text-[15px] font-bold text-brand-ink disabled:opacity-50"
          >
            {busy ? 'Saqlanmoqda…' : test ? 'Saqlash' : 'Yaratish'}
          </button>
        </div>
      </div>
    </Card>
  )
}

function Field({ label, children }) {
  return (
    <label className="block">
      <span className="block pb-1 text-[12px] font-semibold text-slate-500">{label}</span>
      {children}
    </label>
  )
}

/* ------------------------------------------------------------- natija kiritish */

function EntryScreen({ id, options, onBack }) {
  const q = useAsync(() => request(teacherApi.interimEntry(id)), [])
  const [notice, setNotice] = useState(null)
  const [dirty, setDirty] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const [editing, setEditing] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState(null)

  const save = async (rows) => {
    const result = await teacherApi.saveInterimEntry(id, rows)
    setNotice(`${result.saved} ta o'quvchi natijasi saqlandi`)
    hapticNotify('success')
    q.reload()
  }

  const remove = async () => {
    setDeleteError(null)
    try {
      await teacherApi.deleteInterimTest(id)
      hapticNotify('success')
      onBack()
    } catch (err) {
      setDeleteError(blockExamErrorMessage(err))
    }
  }

  const entry = q.data
  const t = entry?.test
  const total = entry?.rows?.length ?? 0
  const done = entry ? entry.rows.filter((r) => r.absent || r.correct != null).length : 0

  return (
    <Screen>
      <Hero
        title={t ? `${t.groupName} · ${t.subjectName}` : 'Oraliq test'}
        subtitle={t ? `${t.number}-oraliq test · ${fmtDay(t.date)} · ${t.questionCount} savol` : ''}
        right={<BackPill label="Testlar" onClick={() => (dirty ? setLeaving(true) : onBack())} />}
      >
        {t && <HeroProgress value={done} max={total} lead={`${done}/${total}`} note="kiritilgan" />}
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

      {editing && t ? (
        <TestForm
          options={options}
          test={t}
          onCancel={() => setEditing(false)}
          onSaved={() => {
            setEditing(false)
            q.reload()
          }}
        />
      ) : deleting ? (
        <Card className="ring-2 ring-red-200">
          <p className="px-4 pt-4 text-[15px] font-semibold">Test o'chirilsinmi?</p>
          <p className="px-4 pt-1 text-[13px] text-slate-500">Test va unga kiritilgan barcha natijalar o'chiriladi.</p>
          {deleteError && <p className="px-4 pt-2 text-[13px] font-semibold text-red-600">{deleteError}</p>}
          <div className="flex gap-2 p-4">
            <button type="button" onClick={() => setDeleting(false)} className="flex-1 rounded-2xl bg-slate-100 py-3 text-[15px] font-semibold">
              Bekor qilish
            </button>
            <button type="button" onClick={remove} className="flex-1 rounded-2xl bg-red-600 py-3 text-[15px] font-semibold text-white">
              O'chirish
            </button>
          </div>
        </Card>
      ) : (
        t && (
          <div className="flex gap-2 px-4 pt-4">
            <button type="button" onClick={() => setEditing(true)} className="flex-1 rounded-2xl bg-white py-2.5 text-[14px] font-semibold text-slate-700 shadow-sm">
              Tahrirlash
            </button>
            <button type="button" onClick={() => setDeleting(true)} className="flex-1 rounded-2xl bg-white py-2.5 text-[14px] font-semibold text-red-600 shadow-sm">
              O'chirish
            </button>
          </div>
        )
      )}

      <AsyncBlock query={q} loadingLabel="O'quvchilar yuklanmoqda…">
        {(data) =>
          data.rows.length === 0 ? (
            <Card>
              <EmptyState icon={<Users className="h-9 w-9" />} title="O'quvchilar yo'q" note="Test kuni bu yo'nalishda o'quvchi bo'lmagan." />
            </Card>
          ) : (
            <EntryGrid
              key={JSON.stringify(data.rows.map((r) => [r.studentId, r.correct, r.absent]))}
              entry={data}
              busy={q.loading}
              notice={notice}
              onSave={save}
              onDirty={setDirty}
            />
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

function EntryGrid({ entry, busy, notice, onSave, onDirty }) {
  const max = entry.test.questionCount
  const [drafts, setDrafts] = useState(() =>
    Object.fromEntries(entry.rows.map((r) => [r.studentId, { correct: r.correct == null ? '' : String(r.correct), absent: r.absent }])),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const cells = useRef([])
  const keyboardOpen = useKeyboardOpen()

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
  const canSave = dirty && invalid === 0 && !locked

  useEffect(() => {
    onDirty(dirty)
  }, [dirty, onDirty])

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
    if (!canSave) return
    haptic('medium')
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
      hapticNotify('error')
    } finally {
      setSaving(false)
    }
  }

  cells.current = []

  return (
    <>
      <Card title="O'quvchilar" action={<span className="text-[12px] font-semibold text-slate-400">to'g'ri javoblar (0–{max})</span>}>
        {entry.rows.map((row, index) => {
          const d = drafts[row.studentId]
          const n = d.correct === '' ? null : Number(d.correct)
          const over = n != null && n > max
          const pct = !d.absent && n != null && !over ? Math.round((n * 1000) / max) / 10 : null
          return (
            <div key={row.studentId} className="border-t border-slate-100 px-4 py-2.5">
              <div className="flex items-center gap-2.5">
                <span className="w-5 shrink-0 text-[12px] font-bold text-slate-400">{index + 1}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[15px] font-semibold">{row.fullName}</p>
                  <p className="truncate text-[12px] text-slate-400">
                    {row.className}
                    {pct != null && <span className={`ml-1.5 font-bold ${pctTone(pct)}`}>· {fmt(pct)}%</span>}
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
                  className={
                    'h-11 w-[64px] shrink-0 rounded-xl border text-center text-[17px] font-extrabold outline-none placeholder:text-slate-300 disabled:opacity-50 ' +
                    (over ? 'border-red-400 bg-red-50' : 'border-slate-200 bg-slate-50 focus:border-brand-ink')
                  }
                />
                <button
                  type="button"
                  onClick={() => {
                    haptic('light')
                    set(row.studentId, { absent: !d.absent })
                  }}
                  disabled={locked}
                  className={
                    'h-11 shrink-0 rounded-xl border px-2.5 text-[12px] font-bold ' +
                    (d.absent ? 'border-red-300 bg-red-50 text-red-600' : 'border-slate-200 text-slate-500')
                  }
                >
                  Kelmadi
                </button>
              </div>
              {over && <p className="mt-1 pl-8 text-[12px] font-semibold text-red-600">{max} tadan oshmasin</p>}
            </div>
          )
        })}
      </Card>

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
          ) : invalid > 0 ? (
            <span className="font-semibold text-red-600">{invalid} ta katakda xato — tuzatmaguncha saqlanmaydi</span>
          ) : dirty ? (
            <span className="font-semibold text-slate-700">{changed.length} ta o'quvchida saqlanmagan o'zgarish</span>
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
