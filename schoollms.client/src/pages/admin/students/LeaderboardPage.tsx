/**
 * O'quvchilar leaderboardi (mijoz, 2026-11-08) — butun o'quv yili bo'yicha: jurnal baholari, davomat,
 * oylik test natijalari, gamifikatsiya bali va qo'lda qo'shilgan ballar (sababi bilan). Umumiy, sinf
 * bo'yicha va yo'nalish bo'yicha. Hisob serverda — LeaderboardService.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Crown, Medal, Pencil, Plus, Search, Trash2, Trophy } from 'lucide-react'
import {
  addLeaderboardAward,
  deleteLeaderboardAward,
  deleteLeaderboardReason,
  getLeaderboard,
  getLeaderboardAwards,
  getLeaderboardReasons,
  saveLeaderboardReason,
  type LeaderboardAward,
  type LeaderboardData,
  type LeaderboardReason,
  type LeaderboardRow,
  type LeaderboardScopeKind,
} from '@/api/services/leaderboard'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import type { AxiosError } from 'axios'

const SCOPES: { key: LeaderboardScopeKind; label: string }[] = [
  { key: 'all', label: 'Umumiy' },
  { key: 'class', label: "Sinf bo'yicha" },
  { key: 'track', label: "Yo'nalish bo'yicha" },
]

const VIEWS = [
  { key: 'board', label: 'Reyting' },
  { key: 'awards', label: "Qo'shimcha ballar" },
] as const

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

const fmt = (v: number | null, digits = 1) => (v == null ? '—' : v.toFixed(digits).replace('.', ','))
const fmtDay = (iso: string) => (iso ? iso.split('-').reverse().join('.') : '—')
const signed = (p: number) => (p > 0 ? `+${p}` : String(p))

export function LeaderboardPage() {
  const [view, setView] = useState<(typeof VIEWS)[number]['key']>('board')
  const [scope, setScope] = useState<LeaderboardScopeKind>('all')
  const [scopeId, setScopeId] = useState('')
  const [data, setData] = useState<LeaderboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [reasons, setReasons] = useState<LeaderboardReason[]>([])
  const [awardFor, setAwardFor] = useState<LeaderboardRow | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const options = scope === 'class' ? (data?.classes ?? []) : scope === 'track' ? (data?.tracks ?? []) : []

  useEffect(() => {
    let alive = true
    // Sinf/yo'nalish tanlanmagan bo'lsa — ro'yxatdagi birinchisi (ro'yxat birinchi javobdan keladi).
    const id = scope === 'all' ? undefined : scopeId || undefined
    getLeaderboard(scope, id)
      .then((d) => {
        if (!alive) return
        const list = scope === 'class' ? d.classes : scope === 'track' ? d.tracks : []
        if (scope !== 'all' && !id && list.length > 0) {
          setScopeId(list[0].id)
          return
        }
        setData(d)
        setError(null)
      })
      .catch((e) => alive && setError(errorText(e, "Leaderboardni yuklab bo'lmadi.")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [scope, scopeId, reloadKey])

  useEffect(() => {
    getLeaderboardReasons().then(setReasons).catch(() => setReasons([]))
  }, [reloadKey])

  const changeScope = (next: LeaderboardScopeKind) => {
    setLoading(true)
    setScopeId('')
    setScope(next)
  }

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    const list = data?.rows ?? []
    return q ? list.filter((r) => r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q)) : list
  }, [data, search])

  const top = (data?.rows ?? []).slice(0, 3)
  const reload = useCallback(() => setReloadKey((k) => k + 1), [])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Leaderboard</h1>
          <p className="text-sm text-slate-400">
            O'quv yili: {fmtDay(data?.from ?? '')} — {fmtDay(data?.to ?? '')}. Baholar, davomat, oylik test, gamifikatsiya
            bali va qo'shimcha ballar asosida.
          </p>
        </div>
        <div className="flex rounded-lg bg-slate-100 p-1">
          {VIEWS.map((v) => (
            <button
              key={v.key}
              type="button"
              onClick={() => setView(v.key)}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                view === v.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {v.label}
            </button>
          ))}
        </div>
      </div>

      {view === 'awards' ? (
        <AwardsView reasons={reasons} onChanged={reload} />
      ) : (
        <>
          <Card className="flex flex-wrap items-end gap-3 p-4">
            <div className="flex rounded-lg bg-slate-100 p-1">
              {SCOPES.map((s) => (
                <button
                  key={s.key}
                  type="button"
                  onClick={() => changeScope(s.key)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    scope === s.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {s.label}
                </button>
              ))}
            </div>
            {scope !== 'all' && (
              <Select
                value={scopeId}
                onChange={(e) => {
                  setLoading(true)
                  setScopeId(e.target.value)
                }}
                className="w-48"
              >
                {options.length === 0 && <option value="">{scope === 'class' ? 'Sinf yo\'q' : "Yo'nalish yo'q"}</option>}
                {options.map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.name}
                  </option>
                ))}
              </Select>
            )}
            <div className="relative ml-auto w-full sm:w-64">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="O'quvchi yoki sinf"
                className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
            </div>
          </Card>

          {loading ? (
            <Loader label="Hisoblanmoqda..." />
          ) : error ? (
            <Card className="p-6 text-sm text-red-600">{error}</Card>
          ) : (
            <>
              {top.length > 0 && <Podium rows={top} />}
              {data && <Formula data={data} />}
              <BoardTable rows={rows} onAward={setAwardFor} />
            </>
          )}
        </>
      )}

      <AwardModal
        open={awardFor !== null}
        pupil={awardFor}
        reasons={reasons}
        onClose={() => setAwardFor(null)}
        onSaved={() => {
          setAwardFor(null)
          reload()
        }}
      />
    </div>
  )
}

/* ------------------------------------------------------------------ */

function Podium({ rows }: { rows: LeaderboardRow[] }) {
  const styles = [
    { icon: Crown, ring: 'ring-amber-300 bg-amber-50', badge: 'bg-amber-400 text-white', label: '1-o\'rin' },
    { icon: Medal, ring: 'ring-slate-300 bg-slate-50', badge: 'bg-slate-400 text-white', label: '2-o\'rin' },
    { icon: Medal, ring: 'ring-orange-200 bg-orange-50', badge: 'bg-orange-400 text-white', label: '3-o\'rin' },
  ]
  return (
    <div className="grid gap-3 sm:grid-cols-3">
      {rows.map((r, i) => {
        const s = styles[i]
        const Icon = s.icon
        return (
          <Card key={r.studentId} className={cn('flex items-center gap-4 p-4 ring-1', s.ring)}>
            <div className={cn('flex h-11 w-11 shrink-0 items-center justify-center rounded-full', s.badge)}>
              <Icon className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <p className="text-xs font-medium text-slate-500">
                {r.rank}-o'rin · {r.className || '—'}
              </p>
              <p className="truncate font-semibold text-slate-800" title={r.fullName}>
                {r.fullName}
              </p>
              <p className="text-sm text-slate-600">
                <b className="text-slate-800">{fmt(r.total)}</b> ball
              </p>
            </div>
          </Card>
        )
      })}
    </div>
  )
}

function Formula({ data }: { data: LeaderboardData }) {
  const w = data.weights
  return (
    <p className="flex items-start gap-2 text-xs text-slate-400">
      <Trophy className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span>
        Ball (0–100) = baholar {w.grades}% (yo'nalish o'quvchisida — oraliq testlar o'rtacha foizi) · davomat {w.attendance}% · oylik test {w.monthly}% (blok testlar ham) · gamifikatsiya{' '}
        {w.behaviour}% ({w.behaviourFull} ball — to'liq). Ma'lumoti yo'q qism hisobga olinmaydi, uning ulushi qolganlariga
        bo'linadi. Jami = ball + qo'shimcha ballar.
      </span>
    </p>
  )
}

function BoardTable({ rows, onAward }: { rows: LeaderboardRow[]; onAward: (r: LeaderboardRow) => void }) {
  return (
    <Card className="p-0">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="w-12 px-4 py-3">#</th>
              <th className="px-4 py-3">O'quvchi</th>
              <th className="px-4 py-3">Sinf</th>
              <th className="px-4 py-3 text-right">Baholar / oraliq</th>
              <th className="px-4 py-3 text-right">Davomat</th>
              <th className="px-4 py-3 text-right">Oylik test</th>
              <th className="px-4 py-3 text-right">Gamifikatsiya</th>
              <th className="px-4 py-3 text-right">Qo'shimcha</th>
              <th className="px-4 py-3 text-right">Jami</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {rows.length === 0 && (
              <tr>
                <td colSpan={10} className="px-4 py-8 text-center text-slate-400">
                  O'quvchi topilmadi
                </td>
              </tr>
            )}
            {rows.map((r) => (
              <tr key={r.studentId} className="hover:bg-slate-50/60">
                <td className="px-4 py-3">
                  <span
                    className={cn(
                      'inline-flex h-7 min-w-7 items-center justify-center rounded-full px-2 text-xs font-semibold',
                      r.rank === 1
                        ? 'bg-amber-400 text-white'
                        : r.rank === 2
                          ? 'bg-slate-400 text-white'
                          : r.rank === 3
                            ? 'bg-orange-400 text-white'
                            : 'bg-slate-100 text-slate-500',
                    )}
                  >
                    {r.rank}
                  </span>
                </td>
                <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                <td className="px-4 py-3 text-slate-500">{r.className || '—'}</td>
                {r.isTrack ? (
                  <td className="px-4 py-3 text-right" title={`Oraliq testlar: ${r.interimCount} ta`}>
                    {r.interim == null ? '—' : `${fmt(r.interim)}%`}
                    <span className="ml-1 text-[10px] font-medium uppercase text-slate-400">oraliq</span>
                  </td>
                ) : (
                  <td className="px-4 py-3 text-right" title={`${r.gradeCount} ta baho`}>
                    {fmt(r.grades, 2)}
                  </td>
                )}
                <td className="px-4 py-3 text-right" title={`${r.lessons} darsdan ${r.absent} tasida yo'q`}>
                  {r.attendance == null ? '—' : `${fmt(r.attendance)}%`}
                </td>
                <td className="px-4 py-3 text-right" title={`${r.monthlyCount} ta natija`}>
                  {fmt(r.monthlyTest)}
                </td>
                <td className="px-4 py-3 text-right">{r.behaviour}</td>
                <td
                  className={cn(
                    'px-4 py-3 text-right font-medium',
                    r.manualPoints > 0 ? 'text-emerald-600' : r.manualPoints < 0 ? 'text-red-600' : 'text-slate-300',
                  )}
                >
                  {r.manualPoints === 0 ? '—' : signed(r.manualPoints)}
                </td>
                <td className="px-4 py-3 text-right font-semibold text-slate-800" title={`Ball: ${fmt(r.base)}`}>
                  {fmt(r.total)}
                </td>
                <td className="px-4 py-3 text-right">
                  <button
                    type="button"
                    title="Qo'shimcha ball berish"
                    onClick={() => onAward(r)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-700"
                  >
                    <Plus className="h-4 w-4" />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ */

function AwardModal({
  open,
  pupil,
  reasons,
  onClose,
  onSaved,
}: {
  open: boolean
  pupil: LeaderboardRow | null
  reasons: LeaderboardReason[]
  onClose: () => void
  onSaved: () => void
}) {
  const active = reasons.filter((r) => r.isActive)
  const [reasonId, setReasonId] = useState('')
  const [customName, setCustomName] = useState('')
  const [points, setPoints] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const reason = active.find((r) => r.id === reasonId)
  const pickReason = (id: string) => {
    setReasonId(id)
    const r = active.find((x) => x.id === id)
    setPoints(r ? String(r.points) : '')
  }

  const close = () => {
    setReasonId('')
    setCustomName('')
    setPoints('')
    setNote('')
    setMsg(null)
    onClose()
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!pupil) return
    setBusy(true)
    setMsg(null)
    try {
      await addLeaderboardAward({
        studentId: pupil.studentId,
        reasonId: reason ? reason.id : null,
        reasonName: reason ? null : customName.trim(),
        points: points.trim() === '' ? null : Number(points),
        note: note.trim() || null,
      })
      close()
      onSaved()
    } catch (err) {
      setMsg(errorText(err, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={open} onClose={close} title={pupil ? `Qo'shimcha ball — ${pupil.fullName}` : "Qo'shimcha ball"}>
      <form onSubmit={submit} className="space-y-4">
        <Select label="Sabab" value={reasonId} onChange={(e) => pickReason(e.target.value)}>
          <option value="">— O'zim yozaman —</option>
          {active.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name} ({signed(r.points)})
            </option>
          ))}
        </Select>
        {!reason && (
          <Input
            label="Sabab nomi"
            required
            value={customName}
            onChange={(e) => setCustomName(e.target.value)}
            placeholder="Masalan: Shahar olimpiadasi g'olibi"
          />
        )}
        <Input
          label="Ball (manfiy ham bo'lishi mumkin)"
          required
          inputMode="numeric"
          value={points}
          onChange={(e) => setPoints(e.target.value.replace(/[^\d-]/g, ''))}
        />
        <Textarea label="Izoh" value={note} onChange={(e) => setNote(e.target.value)} rows={2} />
        {msg && <p className="text-sm text-red-600">{msg}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={close}>
            Bekor qilish
          </Button>
          <Button type="submit" disabled={busy}>
            Saqlash
          </Button>
        </div>
      </form>
    </Modal>
  )
}

/* ------------------------------------------------------------------ */

function AwardsView({ reasons, onChanged }: { reasons: LeaderboardReason[]; onChanged: () => void }) {
  const [awards, setAwards] = useState<LeaderboardAward[] | null>(null)
  // Butun maktab o'quvchilari — qidiruv uchun (reytingdagi tanlangan sinfdan qat'i nazar).
  const [rows, setRows] = useState<LeaderboardRow[]>([])
  useEffect(() => {
    getLeaderboard('all').then((d) => setRows(d.rows)).catch(() => setRows([]))
  }, [])
  const [editing, setEditing] = useState<LeaderboardReason | 'new' | null>(null)
  const [pupilSearch, setPupilSearch] = useState('')
  const [awardFor, setAwardFor] = useState<LeaderboardRow | null>(null)

  const load = useCallback(() => {
    getLeaderboardAwards().then(setAwards).catch(() => setAwards([]))
  }, [])
  useEffect(load, [load])

  const matches = useMemo(() => {
    const q = pupilSearch.trim().toLowerCase()
    return q.length < 2 ? [] : rows.filter((r) => r.fullName.toLowerCase().includes(q)).slice(0, 8)
  }, [pupilSearch, rows])

  const removeAward = async (a: LeaderboardAward) => {
    if (!window.confirm(`${a.studentName}: "${a.reasonName}" (${signed(a.points)}) bekor qilinsinmi?`)) return
    await deleteLeaderboardAward(a.id)
    load()
    onChanged()
  }

  const removeReason = async (r: LeaderboardReason) => {
    if (!window.confirm(`"${r.name}" sababi o'chirilsinmi? Berilgan ballar saqlanib qoladi.`)) return
    await deleteLeaderboardReason(r.id)
    onChanged()
  }

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div className="space-y-4">
        <Card className="space-y-2 p-4">
          <p className="text-sm font-medium text-slate-600">O'quvchiga ball berish</p>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={pupilSearch}
              onChange={(e) => setPupilSearch(e.target.value)}
              placeholder="O'quvchi ismini yozing"
              className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
          </div>
          {matches.length === 0 && pupilSearch.trim().length >= 2 && (
            <p className="text-xs text-slate-400">O'quvchi topilmadi</p>
          )}
          {matches.length > 0 && (
            <div className="divide-y divide-slate-100 rounded-lg border border-slate-100">
              {matches.map((r) => (
                <button
                  key={r.studentId}
                  type="button"
                  onClick={() => setAwardFor(r)}
                  className="flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-slate-50"
                >
                  <span className="font-medium text-slate-700">{r.fullName}</span>
                  <span className="text-xs text-slate-400">{r.className}</span>
                </button>
              ))}
            </div>
          )}
        </Card>

        <Card className="p-0">
          <div className="border-b border-slate-100 p-4">
            <p className="font-semibold text-slate-800">Berilgan qo'shimcha ballar</p>
            <p className="text-xs text-slate-400">Shu o'quv yili, oxirgilari birinchi</p>
          </div>
          {awards === null ? (
            <Loader label="Yuklanmoqda..." />
          ) : awards.length === 0 ? (
            <p className="p-6 text-center text-sm text-slate-400">Hali qo'shimcha ball berilmagan</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Sana</th>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sabab</th>
                    <th className="px-4 py-3 text-right">Ball</th>
                    <th className="px-4 py-3">Kim</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {awards.map((a) => (
                    <tr key={a.id} className="hover:bg-slate-50/60">
                      <td className="whitespace-nowrap px-4 py-3 text-slate-500">{fmtDay(a.date)}</td>
                      <td className="px-4 py-3">
                        <span className="font-medium text-slate-800">{a.studentName}</span>
                        <span className="ml-1 text-xs text-slate-400">{a.className}</span>
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {a.reasonName}
                        {a.note && <span className="block text-xs text-slate-400">{a.note}</span>}
                      </td>
                      <td
                        className={cn(
                          'px-4 py-3 text-right font-semibold',
                          a.points > 0 ? 'text-emerald-600' : 'text-red-600',
                        )}
                      >
                        {signed(a.points)}
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-400">{a.createdBy || '—'}</td>
                      <td className="px-4 py-3 text-right">
                        <button
                          type="button"
                          title="Bekor qilish"
                          onClick={() => void removeAward(a)}
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
          )}
        </Card>
      </div>

      <Card className="h-fit p-0">
        <div className="flex items-center justify-between border-b border-slate-100 p-4">
          <div>
            <p className="font-semibold text-slate-800">Sabablar</p>
            <p className="text-xs text-slate-400">Qo'shimcha ball beriladigan sabablar ro'yxati</p>
          </div>
          <Button variant="secondary" onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> Sabab
          </Button>
        </div>
        {reasons.length === 0 ? (
          <p className="p-6 text-center text-sm text-slate-400">Sabab qo'shilmagan</p>
        ) : (
          <ul className="divide-y divide-slate-100">
            {reasons.map((r) => (
              <li key={r.id} className="flex items-center gap-2 px-4 py-2.5">
                <span className={cn('min-w-0 flex-1 truncate text-sm', r.isActive ? 'text-slate-700' : 'text-slate-400 line-through')}>
                  {r.name}
                </span>
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-semibold',
                    r.points > 0 ? 'bg-emerald-50 text-emerald-600' : 'bg-red-50 text-red-600',
                  )}
                >
                  {signed(r.points)}
                </span>
                <button
                  type="button"
                  title="Tahrirlash"
                  onClick={() => setEditing(r)}
                  className="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                >
                  <Pencil className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  title="O'chirish"
                  onClick={() => void removeReason(r)}
                  className="rounded-lg p-1 text-slate-400 hover:bg-red-50 hover:text-red-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <ReasonModal
        reason={editing}
        onClose={() => setEditing(null)}
        onSaved={() => {
          setEditing(null)
          onChanged()
        }}
      />
      <AwardModal
        open={awardFor !== null}
        pupil={awardFor}
        reasons={reasons}
        onClose={() => setAwardFor(null)}
        onSaved={() => {
          setAwardFor(null)
          setPupilSearch('')
          load()
          onChanged()
        }}
      />
    </div>
  )
}

function ReasonModal({
  reason,
  onClose,
  onSaved,
}: {
  reason: LeaderboardReason | 'new' | null
  onClose: () => void
  onSaved: () => void
}) {
  const editing = reason && reason !== 'new' ? reason : null
  const [name, setName] = useState('')
  const [points, setPoints] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [loadedFor, setLoadedFor] = useState<LeaderboardReason | 'new' | null>(null)

  // Oyna ochilganda maydonlar tanlangan sababdan to'ldiriladi (render paytida, effektsiz).
  if (reason !== loadedFor) {
    setLoadedFor(reason)
    setName(editing?.name ?? '')
    setPoints(editing ? String(editing.points) : '')
    setIsActive(editing?.isActive ?? true)
    setMsg(null)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      await saveLeaderboardReason({ name: name.trim(), points: Number(points || 0), isActive }, editing?.id)
      onSaved()
    } catch (err) {
      setMsg(errorText(err, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={reason !== null} onClose={onClose} title={editing ? 'Sababni tahrirlash' : 'Yangi sabab'} size="sm">
      <form onSubmit={submit} className="space-y-4">
        <Input label="Nomi" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Olimpiada g'olibi" />
        <Input
          label="Ball (manfiy — jarima)"
          required
          inputMode="numeric"
          value={points}
          onChange={(e) => setPoints(e.target.value.replace(/[^\d-]/g, ''))}
        />
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Faol (yangi ball berishda tanlanadi)
        </label>
        {msg && <p className="text-sm text-red-600">{msg}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" disabled={busy}>
            Saqlash
          </Button>
        </div>
      </form>
    </Modal>
  )
}
