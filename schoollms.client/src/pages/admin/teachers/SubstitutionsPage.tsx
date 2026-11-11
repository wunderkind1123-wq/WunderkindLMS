import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowRight, Repeat2, X } from 'lucide-react'
import type { Teacher } from '@/types'
import { getTeachers } from '@/api/services/teachers'
import {
  cancelSubstitution,
  createSubstitutions,
  getExternalSubstitutes,
  getSubstitutionDay,
  getSubstitutionMonth,
  type ExternalSubstitute,
  type SubstitutionDay,
  type SubstitutionLesson,
  type SubstitutionMonth,
} from '@/api/services/substitutions'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { formatPhone } from '@/lib/phone'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'
import { Toast } from '@/components/ui/Toast'
import { fmtDate, todayIso } from '@/components/lesson-analysis/format'
import { ExternalSubstituteModal } from './ExternalSubstituteModal'

/** A pick is an internal teacher's id, or `ext:<id>` for a tashqi o'rindosh. */
const EXT = 'ext:'
const NEW_EXTERNAL = '__new__'

const currentOf = (l: SubstitutionLesson) =>
  l.externalSubstituteId ? EXT + l.externalSubstituteId : (l.substituteTeacherId ?? '')

/**
 * O'rindosh ustoz (client, 2026-10-07): a teacher does not teach on a date — the admin names who
 * covers each of their lessons. The substitute sees the lesson in the Mini App, may fill that
 * journal on that day, is paid for it at their own rate, and the absent teacher is not.
 *
 * 2026-11-11: the cover can be a tashqi o'rindosh (a card without a login), and the history is read
 * by month — every swap moves pay, so the month's counts per person are what payroll needs.
 */
export function SubstitutionsPage() {
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [externals, setExternals] = useState<ExternalSubstitute[]>([])
  const [date, setDate] = useState(todayIso())
  const [teacherId, setTeacherId] = useState('')
  const [reason, setReason] = useState('')
  const [day, setDay] = useState<SubstitutionDay | null>(null)
  const [dayLoading, setDayLoading] = useState(false)
  const [picks, setPicks] = useState<Record<string, string>>({})
  const [modes, setModes] = useState<Record<string, 'internal' | 'external'>>({})
  const [newFor, setNewFor] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [toast, setToast] = useState<string | null>(null)
  const [month, setMonth] = useState(todayIso().slice(0, 7))
  const [monthData, setMonthData] = useState<SubstitutionMonth | null>(null)

  useEffect(() => {
    getTeachers()
      .then((rows) => setTeachers(rows.filter((t) => !t.isArchived).sort((a, b) => a.fullName.localeCompare(b.fullName))))
      .catch(() => setTeachers([]))
    getExternalSubstitutes()
      .then(setExternals)
      .catch(() => setExternals([]))
  }, [])

  const loadMonth = useCallback(() => {
    if (!month) return
    setMonthData(null)
    getSubstitutionMonth(month)
      .then(setMonthData)
      .catch(() => setMonthData({ month, people: [], rows: [] }))
  }, [month])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- oy o'zgarganda qayta yuklanadi (maqsadli)
  useEffect(loadMonth, [loadMonth])

  const loadDay = useCallback(() => {
    if (!teacherId || !date) {
      setDay(null)
      return
    }
    setDayLoading(true)
    setError('')
    getSubstitutionDay(teacherId, date)
      .then((d) => {
        setDay(d)
        setPicks({})
        setModes({})
      })
      .catch((e) => {
        setDay(null)
        setError(hrErrorMessage(e, "Darslarni yuklab bo'lmadi"))
      })
      .finally(() => setDayLoading(false))
  }, [teacherId, date])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- ustoz yoki sana o'zgarganda kun qayta yuklanadi (maqsadli)
  useEffect(loadDay, [loadDay])

  const keyOf = (l: { period: number; ownerId: string; subjectId: string; subGroup: number }) =>
    `${l.period}|${l.ownerId}|${l.subjectId}|${l.subGroup}`

  const modeOf = (l: SubstitutionLesson) => modes[keyOf(l)] ?? (l.externalSubstituteId ? 'external' : 'internal')

  const chosen = useMemo(
    () => (day?.lessons ?? []).filter((l) => picks[keyOf(l)] && picks[keyOf(l)] !== currentOf(l)),
    [day, picks],
  )

  const save = async () => {
    if (!day || chosen.length === 0) return
    setSaving(true)
    setError('')
    try {
      await createSubstitutions({
        date: day.date,
        absentTeacherId: day.teacherId,
        reason: reason.trim() || null,
        items: chosen.map((l) => {
          const pick = picks[keyOf(l)]
          const external = pick.startsWith(EXT)
          return {
            period: l.period,
            ownerId: l.ownerId,
            subjectId: l.subjectId,
            subGroup: l.subGroup,
            substituteTeacherId: external ? null : pick,
            externalSubstituteId: external ? pick.slice(EXT.length) : null,
          }
        }),
      })
      const internal = chosen.filter((l) => !picks[keyOf(l)].startsWith(EXT)).length
      setToast(
        internal > 0
          ? `${chosen.length} ta darsga o'rindosh tayinlandi — ichki ustozlarga botdan xabar ketdi`
          : `${chosen.length} ta darsga o'rindosh tayinlandi`,
      )
      loadDay()
      loadMonth()
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const cancel = async (id: string) => {
    try {
      await cancelSubstitution(id)
      setToast("O'rindosh bekor qilindi")
      loadDay()
      loadMonth()
    } catch (e) {
      setError(hrErrorMessage(e, "Bekor qilib bo'lmadi"))
    }
  }

  const onExternalSaved = (s: ExternalSubstitute, existed: boolean) => {
    setExternals((list) =>
      list.some((x) => x.id === s.id) ? list : [...list, s].sort((a, b) => a.fullName.localeCompare(b.fullName)),
    )
    if (newFor) setPicks((p) => ({ ...p, [newFor]: EXT + s.id }))
    setNewFor(null)
    setToast(existed ? `${s.fullName} tanlandi (oldin kiritilgan)` : `${s.fullName} qo'shildi`)
  }

  const selectCls =
    'w-full min-w-56 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400'

  const totals = useMemo(() => {
    const rows = monthData?.rows ?? []
    return { all: rows.length, external: rows.filter((r) => r.externalSubstituteId).length }
  }, [monthData])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'rindosh ustozlar</h1>
        <p className="text-sm text-slate-400">
          Ustoz darsga kirmasa — uning o'sha kungi darslariga boshqa ustozni tayinlang. O'rindosh jurnalni to'ldiradi va dars
          uning maoshiga qo'shiladi.
        </p>
      </div>

      <Card className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-[180px_1fr_1fr]">
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Sana</span>
            <DatePicker value={date} onChange={(v: string) => setDate(v)} />
          </div>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Darsga kirmaydigan ustoz</span>
            <select
              value={teacherId}
              onChange={(e) => setTeacherId(e.target.value)}
              className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">— tanlang —</option>
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </select>
          </label>
          <Input label="Sabab (ixtiyoriy)" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="Kasal, safarda…" />
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

        {!teacherId ? (
          <p className="py-6 text-center text-sm text-slate-400">Ustozni tanlang — uning shu kungi darslari chiqadi</p>
        ) : dayLoading ? (
          <Loader label="Yuklanmoqda..." />
        ) : day && day.lessons.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Bu kunda ustozning dars jadvalida darsi yo'q</p>
        ) : day ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
                  <th className="py-2 pr-3">Dars</th>
                  <th className="py-2 pr-3">Sinf · fan</th>
                  <th className="py-2 pr-3">O'rindosh</th>
                  <th className="py-2" />
                </tr>
              </thead>
              <tbody>
                {day.lessons.map((l) => {
                  const k = keyOf(l)
                  const mode = modeOf(l)
                  const value = picks[k] ?? currentOf(l)
                  return (
                    <tr key={k} className="border-b border-slate-50 last:border-0">
                      <td className="py-2.5 pr-3 align-middle">
                        <div className="font-medium text-slate-800">{l.period}-dars</div>
                        {l.startTime && (
                          <div className="text-xs text-slate-400">
                            {l.startTime}–{l.endTime}
                          </div>
                        )}
                      </td>
                      <td className="py-2.5 pr-3 align-middle text-slate-700">
                        {l.ownerName} · {l.subjectName}
                        {l.subGroup > 0 && <span className="ml-1 text-xs text-slate-400">({l.subGroup}-guruh)</span>}
                      </td>
                      <td className="py-2.5 pr-3 align-middle">
                        <div className="flex items-center gap-2">
                          <div className="inline-flex shrink-0 rounded-lg bg-slate-100 p-0.5 text-xs">
                            {(['internal', 'external'] as const).map((m) => (
                              <button
                                key={m}
                                type="button"
                                onClick={() => {
                                  setModes((p) => ({ ...p, [k]: m }))
                                  // Rejim almashsa avvalgi tanlov tushib qoladi.
                                  setPicks((p) => ({ ...p, [k]: '' }))
                                }}
                                className={
                                  mode === m
                                    ? 'rounded-md bg-white px-2.5 py-1 font-medium text-slate-800 shadow-sm'
                                    : 'px-2.5 py-1 text-slate-500 hover:text-slate-700'
                                }
                              >
                                {m === 'internal' ? 'Ichki' : 'Tashqi'}
                              </button>
                            ))}
                          </div>
                          {mode === 'internal' ? (
                            <select
                              value={value.startsWith(EXT) ? '' : value}
                              onChange={(e) => setPicks((p) => ({ ...p, [k]: e.target.value }))}
                              className={selectCls}
                            >
                              <option value="">— tayinlanmagan —</option>
                              {day.teachers.map((t) => {
                                const busy = t.busyPeriods.includes(l.period) && t.id !== l.substituteTeacherId
                                return (
                                  <option key={t.id} value={t.id} disabled={busy}>
                                    {t.fullName}
                                    {busy ? ' (band)' : ''}
                                  </option>
                                )
                              })}
                            </select>
                          ) : (
                            <select
                              value={value.startsWith(EXT) ? value : ''}
                              onChange={(e) => {
                                if (e.target.value === NEW_EXTERNAL) setNewFor(k)
                                else setPicks((p) => ({ ...p, [k]: e.target.value }))
                              }}
                              className={selectCls}
                            >
                              <option value="">— tashqi ustozni tanlang —</option>
                              {externals.map((x) => (
                                <option key={x.id} value={EXT + x.id}>
                                  {x.fullName} · {formatPhone(x.phone)}
                                  {x.subjectName ? ` · ${x.subjectName}` : ''}
                                </option>
                              ))}
                              <option value={NEW_EXTERNAL}>＋ Yangi tashqi ustoz…</option>
                            </select>
                          )}
                        </div>
                      </td>
                      <td className="py-2.5 text-right align-middle">
                        {l.substitutionId && (
                          <button
                            type="button"
                            onClick={() => cancel(l.substitutionId!)}
                            className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                          >
                            <X className="h-3.5 w-3.5" /> Bekor qilish
                          </button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            <div className="mt-4 flex justify-end">
              <Button onClick={save} disabled={saving || chosen.length === 0}>
                <Repeat2 className="h-4 w-4" />
                {saving ? 'Saqlanmoqda...' : chosen.length ? `Tayinlash (${chosen.length})` : 'Tayinlash'}
              </Button>
            </div>
          </div>
        ) : null}
      </Card>

      <div className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">Oy kesimida</h2>
            {monthData && (
              <p className="text-sm text-slate-500">
                Jami {totals.all} ta almashinuv
                {totals.external > 0 && <> · shundan tashqi ustozlar: {totals.external}</>}
              </p>
            )}
          </div>
          <Input label="Oy" type="month" value={month} onChange={(e) => setMonth(e.target.value)} className="w-44" />
        </div>

        {monthData === null ? (
          <Loader label="Yuklanmoqda..." />
        ) : monthData.rows.length === 0 ? (
          <Card>
            <p className="py-8 text-center text-sm text-slate-400">Bu oyda o'rindosh tayinlanmagan</p>
          </Card>
        ) : (
          <>
            <Card className="overflow-x-auto p-0">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
                    <th className="px-4 py-2">Ustoz</th>
                    <th className="px-4 py-2 text-right">O'rniga o'tgan</th>
                    <th className="px-4 py-2 text-right">Darsga kirmagan</th>
                  </tr>
                </thead>
                <tbody>
                  {monthData.people.map((p) => (
                    <tr key={`${p.kind}:${p.id}`} className="border-b border-slate-50 last:border-0">
                      <td className="px-4 py-2.5">
                        <div className="font-medium text-slate-800">{p.fullName}</div>
                        {p.kind === 'external' && (
                          <div className="text-xs text-slate-400">
                            Tashqi
                            {p.external && <> · {formatPhone(p.external.phone)}</>}
                            {p.external?.subjectName && <> · {p.external.subjectName}</>}
                          </div>
                        )}
                      </td>
                      <td className="px-4 py-2.5 text-right tabular-nums">
                        {p.covered > 0 ? <span className="font-medium text-emerald-600">+{p.covered}</span> : <span className="text-slate-300">0</span>}
                      </td>
                      <td className="px-4 py-2.5 text-right tabular-nums">
                        {p.missed > 0 ? <span className="font-medium text-red-500">−{p.missed}</span> : <span className="text-slate-300">0</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>

            <Card className="divide-y divide-slate-50 p-0">
              {monthData.rows.map((s) => (
                <div key={s.id} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 text-sm">
                  <div className="w-28 shrink-0">
                    <div className="font-medium text-slate-800">{fmtDate(s.date)}</div>
                    <div className="text-xs text-slate-400">{s.period}-dars</div>
                  </div>
                  <div className="min-w-40 flex-1 text-slate-700">
                    {s.ownerName} · {s.subjectName}
                    {s.reason && <div className="text-xs text-slate-400">{s.reason}</div>}
                  </div>
                  <div className="flex min-w-64 flex-1 items-center gap-2 text-slate-600">
                    <span>{s.absentTeacherName}</span>
                    <ArrowRight className="h-4 w-4 shrink-0 text-slate-300" />
                    <span className="font-medium text-slate-800">{s.substituteTeacherName}</span>
                    {s.externalSubstituteId && (
                      <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-500">tashqi</span>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => cancel(s.id)}
                    className="rounded-lg px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                  >
                    Bekor qilish
                  </button>
                </div>
              ))}
            </Card>
          </>
        )}
      </div>

      {newFor !== null && <ExternalSubstituteModal open onClose={() => setNewFor(null)} onSaved={onExternalSaved} />}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
