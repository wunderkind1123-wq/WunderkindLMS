/**
 * Oraliq testlar (mijoz, 2026-11-10) — yo'nalish guruhlarining haftalik testi. Oyda uchta oraliq test,
 * to'rtinchisi oylik blok test. Savollar sonini o'qituvchi belgilaydi, natija foizda: to'g'ri javob /
 * savollar soni. O'qituvchilar o'z panelidan kiritadi; bu sahifada hammasi ko'rinadi va tuzatiladi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { ClipboardList, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createInterimTest,
  deleteInterimTest,
  getInterimEntry,
  getInterimOptions,
  getInterimTests,
  saveInterimEntry,
  updateInterimTest,
  type InterimEntry,
  type InterimOption,
  type InterimTestRow,
} from '@/api/services/interimTests'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import type { AxiosError } from 'axios'

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

const fmtDay = (iso: string) => (iso ? iso.split('-').reverse().join('.') : '—')
const fmtPct = (v: number | null) => (v == null ? '—' : `${String(v).replace('.', ',')}%`)

function today(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function pctTone(v: number | null) {
  if (v == null) return 'text-slate-300'
  if (v >= 80) return 'text-emerald-600'
  if (v >= 60) return 'text-amber-600'
  return 'text-red-600'
}

export function InterimTestsPage() {
  const [options, setOptions] = useState<InterimOption[]>([])
  const [groupId, setGroupId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [month, setMonth] = useState(() => today().slice(0, 7))
  const [tests, setTests] = useState<InterimTestRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<InterimTestRow | 'new' | null>(null)
  const [entryFor, setEntryFor] = useState<InterimTestRow | null>(null)

  useEffect(() => {
    getInterimOptions().then(setOptions).catch(() => setOptions([]))
  }, [])

  const load = useCallback(() => {
    getInterimTests({ groupId, subjectId, month })
      .then((d) => {
        setTests(d)
        setError(null)
      })
      .catch((e) => setError(errorText(e, "Oraliq testlarni yuklab bo'lmadi.")))
  }, [groupId, subjectId, month])
  useEffect(load, [load])

  const subjects = useMemo(() => {
    const list = groupId ? options.filter((o) => o.groupId === groupId) : options
    const map = new Map<string, string>()
    list.forEach((o) => o.subjects.forEach((s) => map.set(s.id, s.name)))
    return [...map].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
  }, [options, groupId])

  const remove = async (t: InterimTestRow) => {
    if (!window.confirm(`${t.groupName} · ${t.subjectName}, ${fmtDay(t.date)} — test va uning natijalari o'chirilsinmi?`)) return
    try {
      await deleteInterimTest(t.id)
      load()
    } catch (e) {
      setError(errorText(e, "O'chirib bo'lmadi."))
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Oraliq testlar</h1>
          <p className="text-sm text-slate-400">
            Yo'nalishlarning haftalik testi: oyda uchta oraliq test, to'rtinchisi — oylik blok test. Natija foizda.
          </p>
        </div>
        <Button onClick={() => setEditing('new')} disabled={options.length === 0}>
          <Plus className="h-4 w-4" /> Yangi test
        </Button>
      </div>

      <Card className="flex flex-wrap items-end gap-3 p-4">
        <Select
          label="Yo'nalish"
          value={groupId}
          onChange={(e) => {
            setGroupId(e.target.value)
            setSubjectId('')
          }}
          className="w-48"
        >
          <option value="">Barchasi</option>
          {options.map((o) => (
            <option key={o.groupId} value={o.groupId}>
              {o.groupName}
            </option>
          ))}
        </Select>
        <Select label="Fan" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} className="w-48">
          <option value="">Barchasi</option>
          {subjects.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </Select>
        <Input label="Oy" type="month" value={month} onChange={(e) => setMonth(e.target.value)} className="w-44" />
      </Card>

      {error && <Card className="p-4 text-sm text-red-600">{error}</Card>}

      {tests === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Yo'nalish</th>
                  <th className="px-4 py-3">Fan</th>
                  <th className="px-4 py-3">Test</th>
                  <th className="px-4 py-3 text-right">Savollar</th>
                  <th className="px-4 py-3 text-right">Kiritildi</th>
                  <th className="px-4 py-3 text-right">O'rtacha</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {tests.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-10 text-center text-slate-400">
                      <ClipboardList className="mx-auto mb-2 h-6 w-6" />
                      Bu oyda oraliq test yo'q
                    </td>
                  </tr>
                )}
                {tests.map((t) => (
                  <tr key={t.id} className="cursor-pointer hover:bg-slate-50/60" onClick={() => setEntryFor(t)}>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{fmtDay(t.date)}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{t.groupName}</td>
                    <td className="px-4 py-3 text-slate-700">{t.subjectName}</td>
                    <td className="px-4 py-3">
                      <span className="text-slate-700">{t.number}-oraliq test</span>
                      {t.topic && <span className="block max-w-xs truncate text-xs text-slate-400">{t.topic}</span>}
                    </td>
                    <td className="px-4 py-3 text-right text-slate-600">{t.questionCount}</td>
                    <td
                      className={cn(
                        'px-4 py-3 text-right',
                        t.doneCount >= t.pupilCount && t.pupilCount > 0 ? 'text-emerald-600' : 'text-slate-600',
                      )}
                    >
                      {t.doneCount}/{t.pupilCount}
                    </td>
                    <td className={cn('px-4 py-3 text-right font-semibold', pctTone(t.averagePercent))}>
                      {fmtPct(t.averagePercent)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        title="Tahrirlash"
                        onClick={() => setEditing(t)}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        title="O'chirish"
                        onClick={() => remove(t)}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {editing !== null && (
        <TestModal
          key={editing === 'new' ? 'new' : editing.id}
          test={editing === 'new' ? null : editing}
          options={options}
          defaults={{ groupId, subjectId }}
          onClose={() => setEditing(null)}
          onSaved={(t, created) => {
            setEditing(null)
            load()
            if (created) setEntryFor(t)
          }}
        />
      )}
      {entryFor && <EntryModal key={entryFor.id} test={entryFor} onClose={() => setEntryFor(null)} onSaved={load} />}
    </div>
  )
}

/* ------------------------------------------------------------------ */

function TestModal({
  test,
  options,
  defaults,
  onClose,
  onSaved,
}: {
  test: InterimTestRow | null
  options: InterimOption[]
  defaults: { groupId: string; subjectId: string }
  onClose: () => void
  onSaved: (t: InterimTestRow, created: boolean) => void
}) {
  const [groupId, setGroupId] = useState(test?.groupId ?? (defaults.groupId || options[0]?.groupId || ''))
  const [subjectId, setSubjectId] = useState(test?.subjectId ?? defaults.subjectId)
  const [date, setDate] = useState(test?.date ?? today())
  const [count, setCount] = useState(test ? String(test.questionCount) : '')
  const [topic, setTopic] = useState(test?.topic ?? '')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const subjects = options.find((o) => o.groupId === groupId)?.subjects ?? []

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      const body = { groupId, subjectId, date, questionCount: Number(count), topic: topic.trim() || null }
      const saved = test ? await updateInterimTest(test.id, body) : await createInterimTest(body)
      onSaved(saved, !test)
    } catch (err) {
      setMsg(errorText(err, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={test ? 'Oraliq testni tahrirlash' : 'Yangi oraliq test'} size="sm">
      <form onSubmit={submit} className="space-y-4">
        <Select
          label="Yo'nalish"
          required
          value={groupId}
          disabled={!!test}
          onChange={(e) => {
            setGroupId(e.target.value)
            setSubjectId('')
          }}
        >
          {options.map((o) => (
            <option key={o.groupId} value={o.groupId}>
              {o.groupName}
            </option>
          ))}
        </Select>
        <Select label="Fan" required value={subjectId} disabled={!!test} onChange={(e) => setSubjectId(e.target.value)}>
          <option value="">— Tanlang —</option>
          {subjects.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
          {test && !subjects.some((s) => s.id === test.subjectId) && <option value={test.subjectId}>{test.subjectName}</option>}
        </Select>
        <div className="grid grid-cols-2 gap-3">
          <Input label="Sana" type="date" required value={date} max={today()} onChange={(e) => setDate(e.target.value)} />
          <Input
            label="Savollar soni"
            required
            inputMode="numeric"
            value={count}
            placeholder="Masalan: 20"
            onChange={(e) => setCount(e.target.value.replace(/\D/g, ''))}
          />
        </div>
        <Input label="Mavzu" value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="Ixtiyoriy" />
        {msg && <p className="text-sm text-red-600">{msg}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" disabled={busy || !subjectId || !count}>
            {test ? 'Saqlash' : 'Yaratish'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

/* ------------------------------------------------------------------ */

type Draft = { correct: string; absent: boolean }

function EntryModal({ test, onClose, onSaved }: { test: InterimTestRow; onClose: () => void; onSaved: () => void }) {
  const [entry, setEntry] = useState<InterimEntry | null>(null)
  const [drafts, setDrafts] = useState<Record<string, Draft>>({})
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback((id: string) => {
    getInterimEntry(id)
      .then((e) => {
        setEntry(e)
        setDrafts(
          Object.fromEntries(
            e.rows.map((r) => [r.studentId, { correct: r.correct == null ? '' : String(r.correct), absent: r.absent }]),
          ),
        )
      })
      .catch((err) => setMsg(errorText(err, "Ro'yxatni yuklab bo'lmadi.")))
  }, [])

  useEffect(() => load(test.id), [test.id, load])

  const max = entry?.test.questionCount ?? 0
  const changed = useMemo(() => {
    if (!entry) return []
    return entry.rows.filter((r) => {
      const d = drafts[r.studentId]
      if (!d) return false
      const typed = d.correct === '' ? null : Number(d.correct)
      return d.absent !== r.absent || (!d.absent && typed !== r.correct)
    })
  }, [entry, drafts])
  const invalid = changed.some((r) => {
    const d = drafts[r.studentId]
    return !d.absent && d.correct !== '' && Number(d.correct) > max
  })

  const set = (id: string, patch: Partial<Draft>) => {
    setNotice(null)
    setDrafts((p) => ({ ...p, [id]: { ...p[id], ...patch } }))
  }

  const save = async () => {
    if (!entry || changed.length === 0 || invalid) return
    setBusy(true)
    setMsg(null)
    try {
      const rows = changed.map((r) => {
        const d = drafts[r.studentId]
        return { studentId: r.studentId, absent: d.absent, correct: d.absent || d.correct === '' ? null : Number(d.correct) }
      })
      const res = await saveInterimEntry(entry.test.id, rows)
      setNotice(`${res.saved} ta o'quvchi natijasi saqlandi`)
      load(entry.test.id)
      onSaved()
    } catch (err) {
      setMsg(errorText(err, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  const close = () => {
    if (changed.length > 0 && !window.confirm("Saqlanmagan natijalar bor. Yopilsinmi?")) return
    onClose()
  }

  const t = entry?.test ?? test
  return (
    <Modal
      open
      onClose={close}
      size="lg"
      title={`${t.groupName} · ${t.subjectName} — ${t.number}-oraliq test`}
      footer={
        <div className="flex w-full items-center justify-between gap-3">
          <p className="text-sm">
            {msg ? (
              <span className="text-red-600">{msg}</span>
            ) : invalid ? (
              <span className="text-red-600">To'g'ri javoblar {max} tadan oshmasin</span>
            ) : changed.length > 0 ? (
              <span className="text-slate-700">{changed.length} ta o'quvchida saqlanmagan o'zgarish</span>
            ) : notice ? (
              <span className="text-emerald-600">{notice}</span>
            ) : (
              <span className="text-slate-400">Saqlanmagan o'zgarish yo'q</span>
            )}
          </p>
          <Button onClick={save} disabled={busy || changed.length === 0 || invalid}>
            Saqlash
          </Button>
        </div>
      }
    >
      {!entry ? (
        msg ? null : <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <p className="mb-3 text-sm text-slate-500">
            {fmtDay(entry.test.date)} · {entry.test.questionCount} ta savol
            {entry.test.topic ? ` · ${entry.test.topic}` : ''}. To'g'ri javoblar sonini kiriting — foiz o'zi hisoblanadi.
          </p>
          {entry.rows.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Test kuni bu yo'nalishda o'quvchi bo'lmagan.</p>
          ) : (
            <table className="w-full text-left text-sm">
              <thead className="text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-8 py-2">#</th>
                  <th className="py-2">O'quvchi</th>
                  <th className="py-2 text-center">To'g'ri javob</th>
                  <th className="py-2 text-right">Foiz</th>
                  <th className="py-2 text-right">Kelmadi</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {entry.rows.map((r, i) => {
                  const d = drafts[r.studentId] ?? { correct: '', absent: false }
                  const n = d.correct === '' ? null : Number(d.correct)
                  const over = n != null && n > max
                  const pct = !d.absent && n != null && !over ? Math.round((n * 1000) / max) / 10 : null
                  return (
                    <tr key={r.studentId}>
                      <td className="py-2 text-slate-400">{i + 1}</td>
                      <td className="py-2">
                        <span className="font-medium text-slate-800">{r.fullName}</span>
                        <span className="ml-2 text-xs text-slate-400">
                          {r.className}
                          {!r.member && ' · guruhdan chiqqan'}
                        </span>
                      </td>
                      <td className="py-2 text-center">
                        <input
                          value={d.absent ? '' : d.correct}
                          disabled={d.absent || busy}
                          inputMode="numeric"
                          placeholder="—"
                          onFocus={(e) => e.currentTarget.select()}
                          onChange={(e) => set(r.studentId, { correct: e.target.value.replace(/\D/g, '').slice(0, 3) })}
                          className={cn(
                            'h-9 w-16 rounded-lg border text-center font-semibold outline-none disabled:bg-slate-50',
                            over ? 'border-red-400 bg-red-50' : 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100',
                          )}
                        />
                        <span className="ml-1 text-xs text-slate-400">/ {max}</span>
                      </td>
                      <td className={cn('py-2 text-right font-semibold', pctTone(pct))}>{d.absent ? '—' : fmtPct(pct)}</td>
                      <td className="py-2 text-right">
                        <input
                          type="checkbox"
                          checked={d.absent}
                          disabled={busy}
                          onChange={(e) => set(r.studentId, { absent: e.target.checked })}
                          className="h-4 w-4 accent-brand-600"
                        />
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </>
      )}
    </Modal>
  )
}
