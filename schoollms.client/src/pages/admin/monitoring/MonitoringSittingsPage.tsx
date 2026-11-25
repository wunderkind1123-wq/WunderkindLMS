import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowDown, ArrowLeft, ArrowUp, Layers, Plus, Printer, Radio, Trash2, Unlock, X } from 'lucide-react'
import {
  createSitting,
  deleteSitting,
  getBanks,
  getMonitoringOwners,
  quickCreateSitting,
  type MonitoringBank,
  type MonitoringOwnerOption,
  getSitting,
  getSittings,
  getTests,
  monitoringError,
  PARTICIPANT_LABEL,
  resetSittingDevice,
  STATUS_LABEL,
  type MonitoringSittingDetail,
  type MonitoringSittingListItem,
  type MonitoringTestListItem,
} from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn, formatDate } from '@/lib/utils'
import { control } from './MonitoringBanksPage'
import { ReadinessCard } from './ReadinessCard'
import { RoomPin } from './RoomPin'

/**
 * Test sessiyalari (curriculum-monitoring.md §2.5): bir o'tirishda bir nechta fan testi ketma-ket,
 * o'quvchiga bitta kod. Har fan — oddiy onlayn monitoring testi (o'z vaqti va natijasi).
 */
export function MonitoringSittingsPage() {
  const [sittings, setSittings] = useState<MonitoringSittingListItem[] | null>(null)
  const [creating, setCreating] = useState<'quick' | 'existing' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    getSittings().then(setSittings).catch((e) => setError(monitoringError(e, "Sessiyalarni olib bo'lmadi")))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function remove(s: MonitoringSittingListItem): Promise<void> {
    if (!confirm(`«${s.title}» sessiyasi o'chirilsinmi? Testlar va natijalar qoladi, faqat umumiy kodlar o'chadi.`)) return
    try {
      await deleteSitting(s.id)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!sittings) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/admin/monitoring" className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700">
            <ArrowLeft className="h-4 w-4" /> Monitoring testlari
          </Link>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">Test sessiyalari</h1>
          <p className="text-sm text-slate-400">Bir o'tirishda bir nechta fan: o'quvchi bitta kod bilan fanlarni ketma-ket ishlaydi</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => setCreating('existing')}>Tayyor testlardan</Button>
          <Button onClick={() => setCreating('quick')}>
            <Plus className="h-4 w-4" /> Yangi sessiya
          </Button>
        </div>
      </div>

      {sittings.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <Layers className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Hali sessiya yo'q. Avval har fan uchun onlayn test yarating, keyin ularni sessiyaga yig'ing.</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {sittings.map((s) => (
            <Card key={s.id} className="p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <Link to={`/admin/monitoring/sittings/${s.id}`} className="font-semibold text-slate-800 hover:text-brand-700">{s.title}</Link>
                  <p className="text-xs text-slate-500">{formatDate(s.testDate)} · {s.pupils} o'quvchi · {s.finished} tasi hamma fanni tugatdi</p>
                </div>
                <button type="button" title="O'chirish" onClick={() => void remove(s)} className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
              <ol className="mt-3 flex flex-wrap gap-2 text-xs">
                {s.tests.map((t, i) => (
                  <li key={t.id} className="rounded-lg bg-slate-50 px-2.5 py-1.5 text-slate-600">
                    <b className="text-slate-400">{i + 1}.</b> {t.subjectName} · {t.done}/{t.participants}
                    <span className="ml-1 text-slate-400">({STATUS_LABEL[t.status]})</span>
                  </li>
                ))}
              </ol>
            </Card>
          ))}
        </div>
      )}

      {creating === 'existing' && <CreateSittingModal onClose={() => setCreating(null)} onSaved={() => { setCreating(null); load() }} />}
      {creating === 'quick' && <QuickSittingModal onClose={() => setCreating(null)} onSaved={() => { setCreating(null); load() }} />}
    </div>
  )
}

const toLocalInput = (d: Date) => new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)

interface SubjectRow {
  bankId: string
  count: string
  minutes: string
}

/**
 * Sessiyani bitta formada: sinflar + fanlar (har fanga bank, savollar soni, vaqt) — har fan uchun
 * onlayn test yaratiladi, darhol ochiladi va sessiyaga yig'iladi.
 */
function QuickSittingModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [banks, setBanks] = useState<MonitoringBank[]>([])
  const [owners, setOwners] = useState<MonitoringOwnerOption[]>([])
  const [title, setTitle] = useState('')
  const [testDate, setTestDate] = useState(new Date().toLocaleDateString('sv-SE'))
  const [opensAt, setOpensAt] = useState(() => toLocalInput(new Date()))
  const [closesAt, setClosesAt] = useState(() => toLocalInput(new Date(Date.now() + 4 * 3600_000)))
  const [chosen, setChosen] = useState<string[]>([])
  const [rows, setRows] = useState<SubjectRow[]>([{ bankId: '', count: '', minutes: '30' }, { bankId: '', count: '', minutes: '30' }])
  const [shuffle, setShuffle] = useState(true)
  const [maxLeaves, setMaxLeaves] = useState('2')
  const [monthly, setMonthly] = useState(true)
  const [month, setMonth] = useState(() => new Date().toLocaleDateString('sv-SE').slice(0, 7))
  const [publish, setPublish] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getBanks(), getMonitoringOwners()])
      .then(([b, o]) => { setBanks(b.filter((x) => x.questionCount > 0)); setOwners(o) })
      .catch((e) => setError(monitoringError(e)))
  }, [])

  const bankById = useMemo(() => new Map(banks.map((b) => [b.id, b])), [banks])
  const bankLabel = (b: MonitoringBank) =>
    `${b.subjectName} · ${b.grade != null ? `${b.grade}-sinf` : b.groupName ?? ''} — ${b.title} (${b.questionCount} savol)`

  function setRow(i: number, patch: Partial<SubjectRow>): void {
    setRows(rows.map((r, k) => (k === i ? { ...r, ...patch } : r)))
  }

  function moveRow(i: number, d: -1 | 1): void {
    const next = [...rows]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    setRows(next)
  }

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await quickCreateSitting({
        title: title.trim(),
        testDate,
        opensAt: new Date(opensAt).toISOString(),
        closesAt: new Date(closesAt).toISOString(),
        owners: chosen.map((k) => { const [kind, ...rest] = k.split(':'); return { kind, id: rest.join(':') } }),
        subjects: rows.map((r) => ({
          bankId: r.bankId,
          questionCount: r.count.trim() === '' ? null : Number(r.count),
          timeLimitMin: Number(r.minutes) || 30,
        })),
        shuffle,
        maxLeaves: maxLeaves.trim() === '' ? null : Number(maxLeaves),
        seasonalYear: monthly ? Number(month.slice(0, 4)) : null,
        seasonalMonth: monthly ? Number(month.slice(5, 7)) : null,
        publish,
      })
      onSaved()
    } catch (e) {
      setError(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const ready = title.trim().length >= 2 && chosen.length > 0 && rows.length >= 2 && rows.every((r) => r.bankId)

  return (
    <Modal open onClose={onClose} size="lg" title="Yangi test sessiyasi"
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void save()} disabled={busy || !ready}>
            {busy ? 'Yaratilmoqda…' : publish ? 'Yaratish va kodlar berish' : 'Yaratish'}
          </Button>
        </div>
      }>
      <div className="space-y-4 text-sm">
        <div className="grid gap-3 sm:grid-cols-[1fr_10rem]">
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Nomi</span>
            <input className={control} value={title} placeholder="Masalan: 5-sinflar — noyabr oylik testlari" onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Sana</span>
            <input type="date" className={control} value={testDate} onChange={(e) => setTestDate(e.target.value)} />
          </label>
        </div>

        <div className="space-y-1">
          <span className="font-medium text-slate-600">Qaysi sinf / guruhlar</span>
          <div className="flex max-h-32 flex-wrap gap-1.5 overflow-auto rounded-lg border border-slate-200 p-2">
            {owners.map((o) => {
              const k = `${o.kind}:${o.id}`
              const on = chosen.includes(k)
              return (
                <button key={k} type="button" onClick={() => setChosen(on ? chosen.filter((x) => x !== k) : [...chosen, k])}
                  className={cn('rounded-full border px-2.5 py-1 text-xs', on ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600 hover:border-brand-300')}>
                  {o.name}
                </button>
              )
            })}
          </div>
        </div>

        <div className="space-y-2">
          <span className="font-medium text-slate-600">Fanlar — o'quvchi shu tartibda ishlaydi</span>
          {rows.map((r, i) => {
            const bank = bankById.get(r.bankId)
            return (
              <div key={i} className="grid items-center gap-2 rounded-lg border border-slate-200 p-2 sm:grid-cols-[1.25rem_1fr_6.5rem_5.5rem_auto]">
                <b className="text-slate-400">{i + 1}.</b>
                <select className={control} value={r.bankId} onChange={(e) => setRow(i, { bankId: e.target.value })} aria-label="Savollar banki">
                  <option value="">— savollar bankini tanlang —</option>
                  {banks.map((b) => <option key={b.id} value={b.id}>{bankLabel(b)}</option>)}
                </select>
                <input className={control} inputMode="numeric" value={r.count} aria-label="Savollar soni"
                  placeholder={bank ? `hammasi (${bank.questionCount})` : 'savollar'}
                  onChange={(e) => setRow(i, { count: e.target.value.replace(/\D/g, '') })} />
                <label className="flex items-center gap-1">
                  <input className={control} inputMode="numeric" value={r.minutes} aria-label="Vaqt, daqiqa"
                    onChange={(e) => setRow(i, { minutes: e.target.value.replace(/\D/g, '') })} />
                  <span className="text-xs text-slate-400">daq</span>
                </label>
                <div className="flex gap-1">
                  <button type="button" aria-label="Yuqoriga" disabled={i === 0} onClick={() => moveRow(i, -1)} className="text-slate-400 hover:text-slate-700 disabled:opacity-30"><ArrowUp className="h-4 w-4" /></button>
                  <button type="button" aria-label="Pastga" disabled={i === rows.length - 1} onClick={() => moveRow(i, 1)} className="text-slate-400 hover:text-slate-700 disabled:opacity-30"><ArrowDown className="h-4 w-4" /></button>
                  <button type="button" aria-label="Olib tashlash" disabled={rows.length <= 2} onClick={() => setRows(rows.filter((_, k) => k !== i))} className="text-slate-400 hover:text-red-600 disabled:opacity-30"><X className="h-4 w-4" /></button>
                </div>
              </div>
            )
          })}
          {rows.length < 10 && (
            <button type="button" className="text-sm font-medium text-brand-600 hover:underline" onClick={() => setRows([...rows, { bankId: '', count: '', minutes: '30' }])}>
              + Fan qo'shish
            </button>
          )}
          <p className="text-xs text-slate-500">Savollar soni bo'sh — bankdagi hamma savol; son yozilsa, bankdan tasodifiy shuncha savol olinadi (hammaga bir xil to'plam).</p>
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Ochiladi</span>
            <input type="datetime-local" className={control} value={opensAt} onChange={(e) => setOpensAt(e.target.value)} />
          </label>
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Yopiladi</span>
            <input type="datetime-local" className={control} value={closesAt} onChange={(e) => setClosesAt(e.target.value)} />
          </label>
        </div>

        <div className="space-y-2 rounded-lg border border-slate-200 p-3">
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={shuffle} onChange={(e) => setShuffle(e.target.checked)} />
            Har o'quvchiga savollar va variantlar o'z tartibida
          </label>
          <label className="flex flex-wrap items-center gap-2">
            Oynadan chiqishga ruxsat:
            <input className={control.replace('w-full', 'w-16')} inputMode="numeric" value={maxLeaves} placeholder="∞" onChange={(e) => setMaxLeaves(e.target.value.replace(/\D/g, ''))} />
            marta (har fanda)
          </label>
          <label className="flex flex-wrap items-center gap-2">
            <input type="checkbox" checked={monthly} onChange={(e) => setMonthly(e.target.checked)} />
            Oylik test — natija Mavsumiy bahoga
            {monthly && <input type="month" className={control.replace('w-full', 'w-40')} value={month} onChange={(e) => setMonth(e.target.value)} />}
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={publish} onChange={(e) => setPublish(e.target.checked)} />
            Darhol ochish va kodlar berish
          </label>
        </div>
        {error && <p className="whitespace-pre-line text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

function CreateSittingModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [tests, setTests] = useState<MonitoringTestListItem[]>([])
  const [title, setTitle] = useState('')
  const [testDate, setTestDate] = useState(new Date().toLocaleDateString('sv-SE'))
  const [picked, setPicked] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getTests().then((all) => setTests(all.filter((t) => t.delivery === 'online' && t.status !== 'closed')))
      .catch((e) => setError(monitoringError(e)))
  }, [])

  const byId = useMemo(() => new Map(tests.map((t) => [t.id, t])), [tests])
  const free = tests.filter((t) => !picked.includes(t.id))

  function move(i: number, d: -1 | 1): void {
    const next = [...picked]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    setPicked(next)
  }

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await createSitting({ title: title.trim(), testDate, testIds: picked })
      onSaved()
    } catch (e) {
      setError(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} size="lg" title="Yangi test sessiyasi"
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void save()} disabled={busy || picked.length < 2 || title.trim().length < 2}>
            {busy ? 'Saqlanmoqda…' : 'Yaratish va kodlar berish'}
          </Button>
        </div>
      }>
      <div className="space-y-4 text-sm">
        <div className="grid gap-3 sm:grid-cols-[1fr_10rem]">
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Nomi</span>
            <input className={control} value={title} placeholder="Masalan: 5-sinflar — noyabr oylik testlari" onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Sana</span>
            <input type="date" className={control} value={testDate} onChange={(e) => setTestDate(e.target.value)} />
          </label>
        </div>

        <div className="space-y-2">
          <span className="font-medium text-slate-600">Fanlar tartibi — o'quvchi shu tartibda ishlaydi</span>
          {picked.length === 0 ? (
            <p className="rounded-lg border border-dashed border-slate-200 p-3 text-slate-400">Pastdan kamida 2 ta testni tanlang</p>
          ) : (
            <ol className="space-y-1.5">
              {picked.map((id, i) => {
                const t = byId.get(id)
                return (
                  <li key={id} className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2">
                    <b className="w-5 text-slate-400">{i + 1}.</b>
                    <span className="min-w-0 flex-1 truncate">{t?.subjectName} — {t?.title}</span>
                    <button type="button" aria-label="Yuqoriga" disabled={i === 0} onClick={() => move(i, -1)} className="text-slate-400 hover:text-slate-700 disabled:opacity-30"><ArrowUp className="h-4 w-4" /></button>
                    <button type="button" aria-label="Pastga" disabled={i === picked.length - 1} onClick={() => move(i, 1)} className="text-slate-400 hover:text-slate-700 disabled:opacity-30"><ArrowDown className="h-4 w-4" /></button>
                    <button type="button" aria-label="Olib tashlash" onClick={() => setPicked(picked.filter((x) => x !== id))} className="text-slate-400 hover:text-red-600"><X className="h-4 w-4" /></button>
                  </li>
                )
              })}
            </ol>
          )}
        </div>

        <div className="space-y-1">
          <span className="font-medium text-slate-600">Onlayn testlar</span>
          <div className="max-h-56 space-y-1 overflow-auto rounded-lg border border-slate-200 p-2">
            {free.length === 0 && <p className="p-2 text-slate-400">Qo'shiladigan onlayn test yo'q</p>}
            {free.map((t) => (
              <button key={t.id} type="button" onClick={() => setPicked([...picked, t.id])}
                className="flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left hover:bg-slate-50">
                <span className="min-w-0 truncate"><b>{t.subjectName}</b> — {t.title}</span>
                <span className="shrink-0 text-xs text-slate-400">{t.owners.join(', ')} · {t.itemCount} savol</span>
              </button>
            ))}
          </div>
          <p className="text-xs text-slate-500">
            Har fanning vaqti, aralashtirish va chiqish chegarasi o'z testida qoladi. Testlarni «Ochish va kodlar» bilan ochishni unutmang — yopiq fanda o'quvchi kutib qoladi.
          </p>
        </div>
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

const STEP_STYLE: Record<string, string> = {
  done: 'bg-emerald-50 text-emerald-700',
  in_progress: 'bg-brand-50 text-brand-700',
  pending: 'bg-slate-50 text-slate-400',
  absent: 'bg-slate-100 text-slate-500',
}

/** Bitta sessiya: o'quvchilar, kodlar (chop etish), har fan bo'yicha holat. */
export function MonitoringSittingPage() {
  const { id = '' } = useParams()
  const [data, setData] = useState<MonitoringSittingDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    getSitting(id).then(setData).catch((e) => setError(monitoringError(e, "Sessiyani olib bo'lmadi")))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  async function unlock(codeId: string): Promise<void> {
    if (!confirm("O'quvchi sessiyani boshqa kompyuterda davom ettira olsinmi?")) return
    try {
      await resetSittingDevice(codeId)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!data) return <Loader label="Yuklanmoqda..." />
  const link = `${window.location.origin}/monitoring`

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3 print:hidden">
        <div>
          <Link to="/admin/monitoring/sittings" className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700">
            <ArrowLeft className="h-4 w-4" /> Sessiyalar
          </Link>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">{data.title}</h1>
          <p className="text-sm text-slate-500">
            {formatDate(data.testDate)} · o'quvchi <b className="font-mono">{link}</b> sahifasida kodini kiritadi va fanlarni ketma-ket ishlaydi
          </p>
        </div>
        <div className="flex gap-2">
          <Link to={`/admin/monitoring/sittings/${data.id}/live`}>
            <Button variant="secondary"><Radio className="h-4 w-4 text-red-500" /> Jonli kuzatuv</Button>
          </Link>
          <Button variant="secondary" onClick={() => window.print()}>
            <Printer className="h-4 w-4" /> Kodlarni chop etish
          </Button>
        </div>
      </div>

      <RoomPin kind="sitting" id={data.id} pin={data.roomPin} onChanged={load} />

      <ReadinessCard kind="sitting" id={data.id} />

      <ol className="flex flex-wrap gap-2 text-xs print:hidden">
        {data.tests.map((t, i) => (
          <li key={t.id}>
            <Link to={`/admin/monitoring/${t.id}`} className="block rounded-lg bg-white px-3 py-2 shadow-sm hover:bg-slate-50">
              <b className="text-slate-400">{i + 1}.</b> <span className="font-medium text-slate-700">{t.subjectName}</span>
              <span className="ml-1 text-slate-500">{t.itemCount} savol · {t.timeLimitMin} daq · {t.done}/{t.participants}</span>
              <span className={cn('ml-1', t.status === 'published' ? 'text-emerald-600' : 'text-amber-600')}>({STATUS_LABEL[t.status]})</span>
            </Link>
          </li>
        ))}
      </ol>

      <Card className="p-0 print:hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">O'quvchi</th>
                <th className="px-4 py-2">Sinf</th>
                <th className="px-4 py-2">Kod</th>
                {data.tests.map((t, i) => <th key={t.id} className="px-2 py-2">{i + 1}. {t.subjectName}</th>)}
                <th className="px-4 py-2">Chiqish</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.rows.map((r) => (
                <tr key={r.codeId}>
                  <td className="px-4 py-2 text-slate-800">{r.studentName}</td>
                  <td className="px-4 py-2 text-slate-500">{r.ownerName}</td>
                  <td className="px-4 py-2 font-mono tracking-widest">{r.code}</td>
                  {r.steps.map((st, i) => (
                    <td key={i} className="px-2 py-2">
                      {st ? <span className={cn('rounded px-1.5 py-0.5 text-xs', STEP_STYLE[st])}>{PARTICIPANT_LABEL[st]}</span>
                        : <span className="text-xs text-slate-300">—</span>}
                    </td>
                  ))}
                  <td className={cn('px-4 py-2 tabular-nums', r.leaves > 0 ? 'font-medium text-amber-700' : 'text-slate-300')}>{r.leaves}</td>
                  <td className="px-4 py-2 text-right">
                    {r.deviceBound && (
                      <button type="button" onClick={() => void unlock(r.codeId)} className="inline-flex items-center gap-1 text-xs text-brand-600 hover:underline">
                        <Unlock className="h-3.5 w-3.5" /> Kompyuterni almashtirish
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* chop etish: har o'quvchiga kesib beriladigan kartochka */}
      <div className="hidden grid-cols-3 gap-2 print:grid">
        {data.rows.map((r) => (
          <div key={r.codeId} className="break-inside-avoid rounded border border-slate-300 p-3 text-xs">
            <p className="font-semibold">{r.studentName}</p>
            <p className="text-slate-600">{r.ownerName} · {data.title}</p>
            <p className="mt-1 font-mono text-lg tracking-[0.25em]">{r.code}</p>
            <p className="text-slate-500">{link}</p>
          </div>
        ))}
      </div>
    </div>
  )
}
