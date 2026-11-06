import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AlertTriangle, ArrowLeft, Download, Lock, Printer, Send, Unlock } from 'lucide-react'
import {
  getTest,
  letter,
  LEVELS,
  monitoringError,
  PARTICIPANT_LABEL,
  resetDevice,
  saveAnswers,
  saveTestItems,
  setTestStatus,
  STATUS_LABEL,
  type MonitoringTestDetail,
} from '@/api/services/monitoring'
import { downloadFile } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { control } from './MonitoringBanksPage'

type Tab = 'entry' | 'results' | 'topics' | 'key'

const rateColor = (r: number) => (r >= 70 ? 'text-emerald-600' : r >= 50 ? 'text-amber-600' : 'text-red-600')

/** Bitta monitoring testi: javoblar/kodlar, natijalar, mavzu tahlili, kalit. */
export function MonitoringTestPage() {
  const { id = '' } = useParams()
  const [test, setTest] = useState<MonitoringTestDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('entry')

  const load = useCallback(() => {
    getTest(id).then(setTest).catch((e) => setError(monitoringError(e, "Testni olib bo'lmadi")))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  async function status(next: 'published' | 'closed'): Promise<void> {
    const text = next === 'published'
      ? test?.delivery === 'online' ? "Test ochilsinmi? O'quvchilarga shaxsiy kodlar beriladi." : 'Test o‘tkazilmoqda deb belgilansinmi?'
      : "Test yakunlansinmi? Onlayn yechayotganlar yakunlanadi, boshlamaganlar «kelmadi» bo'ladi."
    if (!confirm(text)) return
    try {
      await setTestStatus(id, next)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!test) return <Loader label="Yuklanmoqda..." />

  const national = test.scoring === 'national'
  const done = test.participants.filter((p) => p.status === 'done').length
  const tabs: [Tab, string][] = [
    ['entry', test.delivery === 'paper' ? 'Javoblarni kiritish' : 'Kodlar va holat'],
    ['results', 'Natijalar'],
    ['topics', 'Mavzu tahlili'],
    ['key', 'Savollar va kalit'],
  ]

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <Link to="/admin/monitoring" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100" aria-label="Orqaga">
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">{test.title}</h1>
            <p className="text-sm text-slate-400">
              {test.subjectName} · {test.groupName ?? `${test.grade}-sinf`} · {formatDate(test.testDate)} · {test.items.length} savol ·{' '}
              {test.delivery === 'online' ? 'onlayn' : "qog'ozda"} · {national ? `milliy uslub (${test.nationalMethod === 'rasch' ? 'Rasch' : 'vazn'})` : 'oddiy'} ·{' '}
              <b>{STATUS_LABEL[test.status]}</b>
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => void downloadFile(`/admin/monitoring/tests/${id}/export`, 'monitoring-natijalar.xlsx')}>
            <Download className="h-4 w-4" /> Excel
          </Button>
          {test.status === 'draft' && (
            <Button onClick={() => void status('published')}>
              <Send className="h-4 w-4" /> {test.delivery === 'online' ? 'Ochish va kodlar' : 'Boshlash'}
            </Button>
          )}
          {test.status === 'published' && (
            <Button onClick={() => void status('closed')}>
              <Lock className="h-4 w-4" /> Yakunlash
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-wrap gap-2 text-sm">
        <Stat label="Topshirdi" value={`${done}/${test.participants.length}`} />
        {test.owners.map((o) => (
          <Stat key={o.ownerId} label={o.ownerName}
            value={o.averagePercent == null ? '—' : national && o.averagePoints != null ? `${o.averagePoints} ball · ${o.averagePercent}%` : `${o.averagePercent}%`} />
        ))}
      </div>

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(([k, l]) => (
          <button key={k} type="button" onClick={() => setTab(k)}
            className={cn('-mb-px shrink-0 border-b-2 px-3 pb-2.5 pt-1 text-sm font-medium', tab === k ? 'border-brand-600 text-brand-700' : 'border-transparent text-slate-500 hover:text-slate-800')}>
            {l}
          </button>
        ))}
      </div>

      {tab === 'entry' && (test.delivery === 'paper' ? <PaperEntry test={test} onSaved={load} /> : <OnlineCodes test={test} onChanged={load} />)}
      {tab === 'results' && <Results test={test} />}
      {tab === 'topics' && <TopicAnalysis test={test} />}
      {tab === 'key' && <KeyEditor test={test} onSaved={load} />}
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2">
      <p className="text-xs text-slate-400">{label}</p>
      <p className="font-semibold tabular-nums text-slate-800">{value}</p>
    </div>
  )
}

/** Qog'oz test: har o'quvchining javoblar qatori ("ABDC-…") yoki "kelmadi". */
function PaperEntry({ test, onSaved }: { test: MonitoringTestDetail; onSaved: () => void }) {
  const [rows, setRows] = useState(() =>
    Object.fromEntries(test.participants.map((p) => [p.id, { answers: p.answers ?? '', absent: p.status === 'absent' }])),
  )
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const n = test.items.length

  async function save(): Promise<void> {
    setBusy(true)
    setMsg(null)
    try {
      await saveAnswers(test.id, test.participants.map((p) => ({
        participantId: p.id,
        answers: rows[p.id].absent ? null : rows[p.id].answers.trim() || null,
        absent: rows[p.id].absent,
      })))
      setMsg('Saqlandi, natijalar qayta hisoblandi ✓')
      onSaved()
    } catch (e) {
      setMsg(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4 text-sm text-slate-500">
        <p>
          Har o'quvchining javoblarini ketma-ket yozing: {n} ta harf (A–{letter(Math.max(...test.items.map((i) => i.optionCount)) - 1)}),
          javob bermagan savolga «-». Masalan: <span className="font-mono">ABDC-A…</span>
        </p>
        <Button onClick={() => void save()} disabled={busy}>{busy ? 'Saqlanmoqda…' : 'Saqlash'}</Button>
      </div>
      {msg && <p className="px-4 pt-3 text-sm text-slate-600">{msg}</p>}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-4 py-2">O'quvchi</th>
              <th className="px-4 py-2">Sinf</th>
              <th className="px-4 py-2">Javoblar</th>
              <th className="px-4 py-2">Kelmadi</th>
              <th className="px-4 py-2 text-right">Natija</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {test.participants.map((p) => {
              const r = rows[p.id]
              const len = r.answers.replace(/\s/g, '').length
              return (
                <tr key={p.id}>
                  <td className="px-4 py-2 text-slate-800">{p.studentName}</td>
                  <td className="px-4 py-2 text-slate-500">{p.ownerName}</td>
                  <td className="px-4 py-2">
                    <input
                      className={cn(control, 'font-mono uppercase tracking-widest', len > n && 'border-red-300')}
                      disabled={r.absent}
                      value={r.answers}
                      aria-label={`${p.studentName} javoblari`}
                      onChange={(e) => setRows({ ...rows, [p.id]: { ...r, answers: e.target.value.toUpperCase() } })}
                    />
                    <span className={cn('text-xs', len > n ? 'text-red-500' : 'text-slate-400')}>{len}/{n}</span>
                  </td>
                  <td className="px-4 py-2">
                    <input type="checkbox" checked={r.absent} onChange={(e) => setRows({ ...rows, [p.id]: { ...r, absent: e.target.checked } })} aria-label="Kelmadi" />
                  </td>
                  <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-slate-600">
                    {p.status === 'done' ? `${p.correct}/${n}${p.points != null ? ` · ${p.points}` : ''}` : '—'}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

/** Onlayn test: shaxsiy kodlar (chop etish uchun), holat, qurilma qulfini ochish. */
function OnlineCodes({ test, onChanged }: { test: MonitoringTestDetail; onChanged: () => void }) {
  const link = `${window.location.origin}/monitoring`

  async function unlock(id: string): Promise<void> {
    if (!confirm("O'quvchi testni boshqa qurilmada davom ettira olsinmi?")) return
    try {
      await resetDevice(id)
      onChanged()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4 text-sm">
        <p className="text-slate-600">
          O'quvchi <b className="font-mono">{link}</b> sahifasini ochib, shaxsiy kodini kiritadi.
          {test.opensAt && ` Ochiq: ${new Date(test.opensAt).toLocaleString('uz-UZ')} — ${new Date(test.closesAt!).toLocaleString('uz-UZ')}, ${test.timeLimitMin} daqiqa.`}
        </p>
        <Button variant="secondary" onClick={() => window.print()} disabled={test.status === 'draft'}>
          <Printer className="h-4 w-4" /> Chop etish
        </Button>
      </div>
      {test.status === 'draft' ? (
        <p className="p-6 text-sm text-slate-500">Kodlar test ochilganda («Ochish va kodlar») beriladi.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">O'quvchi</th>
                <th className="px-4 py-2">Sinf</th>
                <th className="px-4 py-2">Kod</th>
                <th className="px-4 py-2">Holat</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {test.participants.map((p) => (
                <tr key={p.id}>
                  <td className="px-4 py-2 text-slate-800">{p.studentName}</td>
                  <td className="px-4 py-2 text-slate-500">{p.ownerName}</td>
                  <td className="px-4 py-2 font-mono text-base tracking-widest">{p.accessCode}</td>
                  <td className="px-4 py-2 text-slate-600">
                    {PARTICIPANT_LABEL[p.status]}
                    {p.status === 'in_progress' && p.answers && (
                      <span className="ml-1 text-xs text-slate-400">({p.answers.replace(/-/g, '').length}/{test.items.length})</span>
                    )}
                  </td>
                  <td className="px-4 py-2 text-right">
                    {p.status === 'in_progress' && p.deviceBound && (
                      <button type="button" onClick={() => void unlock(p.id)} className="inline-flex items-center gap-1 text-xs text-brand-600 hover:underline">
                        <Unlock className="h-3.5 w-3.5" /> Qurilmani almashtirish
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

function Results({ test }: { test: MonitoringTestDetail }) {
  const national = test.scoring === 'national'
  const rows = [...test.participants].sort((a, b) =>
    (b.status === 'done' ? 1 : 0) - (a.status === 'done' ? 1 : 0) || (b.points ?? b.percent) - (a.points ?? a.percent))
  return (
    <div className="space-y-4">
      {test.smallSampleWarning && (
        <p className="flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <AlertTriangle className="h-4 w-4" /> Rasch bali topshirganlar orasida solishtirib hisoblanadi — 30 tadan kam o'quvchida aniqlik past.
        </p>
      )}
      {national && (
        <div className="flex flex-wrap gap-2">
          {LEVELS.map((l) => (
            <Stat key={l} label={l === '—' ? 'Darajasiz' : l} value={String(test.levels[l] ?? 0)} />
          ))}
        </div>
      )}
      <Card className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">#</th>
                <th className="px-4 py-2">O'quvchi</th>
                <th className="px-4 py-2">Sinf</th>
                <th className="px-4 py-2">Holat</th>
                <th className="px-4 py-2 text-right">To'g'ri</th>
                <th className="px-4 py-2 text-right">Foiz</th>
                {national && <th className="px-4 py-2 text-right">Ball</th>}
                {national && <th className="px-4 py-2">Daraja</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((p, i) => (
                <tr key={p.id}>
                  <td className="px-4 py-2 tabular-nums text-slate-400">{p.status === 'done' ? i + 1 : ''}</td>
                  <td className="px-4 py-2 text-slate-800">{p.studentName}</td>
                  <td className="px-4 py-2 text-slate-500">{p.ownerName}</td>
                  <td className="px-4 py-2 text-slate-500">{PARTICIPANT_LABEL[p.status]}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{p.status === 'done' ? `${p.correct}/${test.items.length}` : '—'}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{p.status === 'done' ? `${p.percent}%` : '—'}</td>
                  {national && <td className="px-4 py-2 text-right font-semibold tabular-nums">{p.points ?? '—'}</td>}
                  {national && <td className="px-4 py-2 font-semibold text-brand-700">{p.level ?? (p.status === 'done' ? '—' : '')}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}

function TopicAnalysis({ test }: { test: MonitoringTestDetail }) {
  return (
    <div className="space-y-4">
      <Card className="p-0">
        <p className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-700">Mavzular bo'yicha (eng bo'sh mavzular tepada)</p>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">Mavzu</th>
                <th className="px-4 py-2 text-right">Savol</th>
                <th className="px-4 py-2 text-right">To'g'ri</th>
                {test.owners.map((o) => <th key={o.ownerId} className="px-4 py-2 text-right normal-case">{o.ownerName}</th>)}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {test.topics.map((t) => (
                <tr key={t.topicId ?? 'none'}>
                  <td className="px-4 py-2 text-slate-800">{t.title}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{t.itemCount}</td>
                  <td className={cn('px-4 py-2 text-right font-semibold tabular-nums', rateColor(t.correctRate))}>{t.correctRate}%</td>
                  {t.byOwner.map((o) => (
                    <td key={o.ownerId} className={cn('px-4 py-2 text-right tabular-nums', rateColor(o.correctRate))}>{o.correctRate}%</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
      <Card className="p-0">
        <p className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-700">Savollar bo'yicha</p>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">№</th>
                <th className="px-4 py-2">Mavzu</th>
                <th className="px-4 py-2">Kalit</th>
                <th className="px-4 py-2 text-right">To'g'ri</th>
                <th className="px-4 py-2">Tanlovlar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {test.itemStats.map((s) => (
                <tr key={s.order}>
                  <td className="px-4 py-2 tabular-nums text-slate-400">{s.order}</td>
                  <td className="px-4 py-2 text-slate-600">{s.topicTitle ?? '—'}</td>
                  <td className="px-4 py-2 font-semibold">{s.correct}</td>
                  <td className={cn('px-4 py-2 text-right tabular-nums', rateColor(s.correctRate))}>{s.correctRate}%</td>
                  <td className="px-4 py-2 font-mono text-xs text-slate-500">
                    {Object.entries(s.choices).sort().map(([c, n]) => `${c}:${n}`).join('  ')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}

/** Kalit, ish reja mavzusi va vazn — o'zgartirilsa test qayta baholanadi. */
function KeyEditor({ test, onSaved }: { test: MonitoringTestDetail; onSaved: () => void }) {
  const [items, setItems] = useState(() => test.items.map((i) => ({ ...i })))
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const topics = test.planTopics ?? []

  async function save(): Promise<void> {
    setBusy(true)
    setMsg(null)
    try {
      await saveTestItems(test.id, items.map((i) => ({ order: i.order, correctIndex: i.correctIndex, optionCount: i.optionCount, topicId: i.topicId, weight: i.weight })))
      setMsg('Saqlandi, natijalar qayta hisoblandi ✓')
      onSaved()
    } catch (e) {
      setMsg(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const set = (order: number, patch: Partial<(typeof items)[number]>) =>
    setItems(items.map((i) => (i.order === order ? { ...i, ...patch } : i)))

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4 text-sm text-slate-500">
        <p>
          Har savolga ish reja mavzusini biriktiring — «Mavzu tahlili» shundan chiqadi.
          {test.nationalMethod === 'weights' && ' «Vazn» — savol bali (qo‘lda vazn usuli).'}
          {topics.length === 0 && ' Bu fan va sinf uchun ish reja yuklanmagan.'}
        </p>
        <Button onClick={() => void save()} disabled={busy}>{busy ? 'Saqlanmoqda…' : 'Saqlash'}</Button>
      </div>
      {msg && <p className="px-4 pt-3 text-sm text-slate-600">{msg}</p>}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-4 py-2">№</th>
              <th className="px-4 py-2">Savol</th>
              <th className="px-4 py-2">To'g'ri javob</th>
              <th className="px-4 py-2">Mavzu</th>
              <th className="px-4 py-2">Vazn</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {items.map((i) => (
              <tr key={i.order}>
                <td className="px-4 py-2 tabular-nums text-slate-400">{i.order}</td>
                <td className="max-w-md px-4 py-2 text-slate-700"><span className="line-clamp-2">{i.text ?? '—'}</span></td>
                <td className="px-4 py-2">
                  <select className={cn(control, 'w-20')} value={i.correctIndex} onChange={(e) => set(i.order, { correctIndex: Number(e.target.value) })} aria-label={`${i.order}-savol kaliti`}>
                    {Array.from({ length: i.optionCount }, (_, k) => <option key={k} value={k}>{letter(k)}</option>)}
                  </select>
                </td>
                <td className="px-4 py-2">
                  <select className={cn(control, 'min-w-[14rem]')} value={i.topicId ?? ''} disabled={topics.length === 0}
                    onChange={(e) => set(i.order, { topicId: e.target.value || null })} aria-label={`${i.order}-savol mavzusi`}>
                    <option value="">—</option>
                    {topics.map((t) => <option key={t.id} value={t.id}>{t.order}. {t.title}</option>)}
                  </select>
                </td>
                <td className="px-4 py-2">
                  <input className={cn(control, 'w-20')} inputMode="decimal" value={i.weight}
                    onChange={(e) => set(i.order, { weight: Number(e.target.value.replace(',', '.')) || 1 })} aria-label={`${i.order}-savol vazni`} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )
}
