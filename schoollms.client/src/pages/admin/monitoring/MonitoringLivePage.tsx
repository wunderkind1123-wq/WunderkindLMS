import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AlertTriangle, ArrowLeft, CheckCircle2, Radio, WifiOff } from 'lucide-react'
import {
  getSittingLive,
  getTestLive,
  monitoringError,
  type LiveStatus,
  type MonitoringLive,
  type MonitoringLiveRow,
} from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { control } from './MonitoringBanksPage'

/** Brauzer shuncha vaqt jim bo'lsa (ping 30 s da bir) — "aloqa yo'q". */
const SILENT_MS = 75_000
const REFRESH_MS = 5_000

const STATUS: Record<LiveStatus, { label: string; tone: string }> = {
  pending: { label: 'Boshlamagan', tone: 'bg-slate-100 text-slate-500' },
  in_progress: { label: 'Ishlamoqda', tone: 'bg-brand-50 text-brand-700' },
  between: { label: 'Keyingi fanga o‘tmoqda', tone: 'bg-sky-50 text-sky-700' },
  done: { label: 'Tugatdi', tone: 'bg-emerald-50 text-emerald-700' },
  absent: { label: 'Kelmadi', tone: 'bg-slate-100 text-slate-400' },
}

function silent(r: MonitoringLiveRow, now: number): boolean {
  return r.status === 'in_progress' && r.lastSeenAt != null && now - new Date(r.lastSeenAt).getTime() > SILENT_MS
}

/**
 * Jonli kuzatuv (curriculum-monitoring.md §2.6): test paytida o'qituvchi / o'quv bo'limi ekrani — kim
 * qaysi fanda, nechta javob, kim oynadan chiqdi, kimning aloqasi uzildi. Har 5 soniyada yangilanadi.
 */
export function MonitoringLivePage({ kind }: { kind: 'sitting' | 'test' }) {
  const { id = '' } = useParams()
  const [data, setData] = useState<MonitoringLive | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [owner, setOwner] = useState('')
  const [now, setNow] = useState(() => Date.now())

  const load = useCallback(() => {
    ;(kind === 'sitting' ? getSittingLive(id) : getTestLive(id))
      .then((d) => {
        setData(d)
        setError(null)
        setNow(Date.now())
      })
      .catch((e) => setError(monitoringError(e, "Ma'lumot olinmadi — internetni tekshiring")))
  }, [id, kind])

  useEffect(() => {
    load()
    const t = setInterval(load, REFRESH_MS)
    return () => clearInterval(t)
  }, [load])

  const owners = useMemo(() => [...new Set((data?.rows ?? []).map((r) => r.ownerName))].sort(), [data])
  const rows = useMemo(() => (data?.rows ?? []).filter((r) => !owner || r.ownerName === owner), [data, owner])

  if (!data) return error ? <p className="text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />

  const count = (f: (r: MonitoringLiveRow) => boolean) => rows.filter(f).length
  const back = kind === 'sitting' ? `/admin/monitoring/sittings/${id}` : `/admin/monitoring/${id}`

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link to={back} className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700">
            <ArrowLeft className="h-4 w-4" /> Orqaga
          </Link>
          <h1 className="mt-1 flex items-center gap-2 text-xl font-semibold text-slate-800">
            <Radio className="h-5 w-5 text-red-500" /> Jonli kuzatuv
          </h1>
          <p className="text-sm text-slate-500">
            {data.title}
            {data.subjects.length > 1 && ` · ${data.subjects.join(' → ')}`}
          </p>
        </div>
        <div className="flex items-center gap-3">
          {data.roomPin && (
            <span className="rounded-xl bg-amber-50 px-3 py-1.5 text-sm text-amber-800">
              Xona PIN: <b className="font-mono text-lg tracking-[0.3em]">{data.roomPin}</b>
            </span>
          )}
          {owners.length > 1 && (
            <select className={control.replace('w-full', 'w-40')} value={owner} onChange={(e) => setOwner(e.target.value)} aria-label="Sinf">
              <option value="">Barcha sinflar</option>
              {owners.map((o) => <option key={o} value={o}>{o}</option>)}
            </select>
          )}
          <span className={cn('text-xs', error ? 'text-red-600' : 'text-slate-400')}>
            {error ?? `Yangilandi: ${new Date(now).toLocaleTimeString('uz-UZ')}`}
          </span>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Chip label="Jami" value={rows.length} />
        <Chip label="Boshlamagan" value={count((r) => r.status === 'pending')} />
        <Chip label="Ishlamoqda" value={count((r) => r.status === 'in_progress' || r.status === 'between')} tone="text-brand-700" />
        <Chip label="Tugatdi" value={count((r) => r.status === 'done')} tone="text-emerald-700" />
        <Chip label="Oynadan chiqqan" value={count((r) => r.leaves > 0)} tone="text-amber-700" />
        <Chip label="Aloqa yo‘q" value={count((r) => silent(r, now))} tone="text-red-600" />
      </div>

      {rows.length === 0 ? (
        <Card className="py-10 text-center text-sm text-slate-500">O‘quvchi yo‘q</Card>
      ) : (
        <div className="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {rows.map((r) => <Tile key={r.studentId} r={r} now={now} />)}
        </div>
      )}
    </div>
  )
}

function Chip({ label, value, tone = 'text-slate-700' }: { label: string; value: number; tone?: string }) {
  return (
    <div className="rounded-xl bg-white px-4 py-2 shadow-sm">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('text-lg font-semibold tabular-nums', tone)}>{value}</p>
    </div>
  )
}

function Tile({ r, now }: { r: MonitoringLiveRow; now: number }) {
  const lost = silent(r, now)
  const left = r.deadline ? Math.max(0, Math.floor((new Date(r.deadline).getTime() - now) / 1000)) : null
  const pct = r.total > 0 ? Math.round((r.answered / r.total) * 100) : 0
  return (
    <div className={cn('rounded-xl border bg-white p-3 shadow-sm',
      lost || r.stopped ? 'border-red-300' : r.leaves > 0 ? 'border-amber-300' : 'border-transparent')}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-slate-800" title={r.studentName}>{r.studentName}</p>
          <p className="text-xs text-slate-400">{r.ownerName}</p>
        </div>
        <span className={cn('shrink-0 rounded px-1.5 py-0.5 text-[11px] font-medium', STATUS[r.status].tone)}>
          {r.status === 'done' ? <CheckCircle2 className="inline h-3 w-3" /> : null} {STATUS[r.status].label}
        </span>
      </div>
      {r.status !== 'absent' && (
        <div className="mt-2">
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span className="truncate">{r.steps > 1 && `${r.step}/${r.steps} · `}{r.subject}</span>
            <span className="tabular-nums">{r.answered}/{r.total}</span>
          </div>
          <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
            <div className={cn('h-full rounded-full', r.status === 'done' ? 'bg-emerald-500' : 'bg-brand-500')} style={{ width: `${pct}%` }} />
          </div>
        </div>
      )}
      <div className="mt-2 flex flex-wrap items-center gap-1.5 text-[11px]">
        {left != null && (
          <span className={cn('rounded px-1.5 py-0.5 tabular-nums', left < 300 ? 'bg-red-50 text-red-600' : 'bg-slate-50 text-slate-500')}>
            {String(Math.floor(left / 60)).padStart(2, '0')}:{String(left % 60).padStart(2, '0')}
          </span>
        )}
        {r.leaves > 0 && (
          <span className="inline-flex items-center gap-0.5 rounded bg-amber-50 px-1.5 py-0.5 font-medium text-amber-700">
            <AlertTriangle className="h-3 w-3" /> Chiqdi: {r.leaves}
          </span>
        )}
        {r.stopped && <span className="rounded bg-red-50 px-1.5 py-0.5 font-medium text-red-600">To‘xtatildi</span>}
        {lost && (
          <span className="inline-flex items-center gap-0.5 rounded bg-red-50 px-1.5 py-0.5 font-medium text-red-600">
            <WifiOff className="h-3 w-3" /> Aloqa yo‘q
          </span>
        )}
      </div>
    </div>
  )
}
