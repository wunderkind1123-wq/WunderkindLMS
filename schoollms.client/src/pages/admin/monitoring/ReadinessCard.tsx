import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle2, Info, RefreshCw, XCircle } from 'lucide-react'
import { getReadiness, monitoringError, type MonitoringCheck } from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

/**
 * Tayyorlik tekshiruvi (curriculum-monitoring.md §2.8): o'quvchilar kirishidan oldin ma'lum
 * muammolar — ochilmagan test, qisqa oyna, yo'qolgan rasm, bo'sh variant, aralashtirishga mos
 * kelmaydigan variant. Sahifa ochilganda o'zi tekshiradi.
 */
export function ReadinessCard({ kind, id }: { kind: 'sitting' | 'test'; id: string }) {
  const [checks, setChecks] = useState<MonitoringCheck[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [round, setRound] = useState(0)

  useEffect(() => {
    let alive = true
    getReadiness(kind, id)
      .then((c) => { if (alive) { setChecks(c); setError(null) } })
      .catch((e) => { if (alive) setError(monitoringError(e, 'Tekshirib bo‘lmadi')) })
      .finally(() => { if (alive) setBusy(false) })
    return () => { alive = false }
  }, [kind, id, round])

  const run = () => {
    setBusy(true)
    setRound((n) => n + 1)
  }

  const errors = checks?.filter((c) => c.level === 'error').length ?? 0
  const warnings = checks?.filter((c) => c.level === 'warning').length ?? 0

  return (
    <Card className={cn('p-4 print:hidden', errors > 0 ? 'border border-red-200' : warnings > 0 ? 'border border-amber-200' : '')}>
      <div className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold text-slate-800">
          {errors > 0 ? <XCircle className="h-5 w-5 text-red-500" />
            : warnings > 0 ? <AlertTriangle className="h-5 w-5 text-amber-500" />
              : <CheckCircle2 className="h-5 w-5 text-emerald-500" />}
          Tayyorlik tekshiruvi
          {checks && (errors > 0 || warnings > 0) && (
            <span className="text-sm font-normal text-slate-500">
              {errors > 0 && `${errors} xato`}{errors > 0 && warnings > 0 && ', '}{warnings > 0 && `${warnings} ogohlantirish`}
            </span>
          )}
        </h2>
        <button type="button" onClick={run} disabled={busy} className="inline-flex items-center gap-1 text-sm text-brand-600 hover:underline disabled:opacity-50">
          <RefreshCw className={cn('h-4 w-4', busy && 'animate-spin')} /> Qayta tekshirish
        </button>
      </div>
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      {checks && (
        <ul className="mt-3 space-y-1.5 text-sm">
          {checks.map((c, i) => (
            <li key={i} className="flex items-start gap-2">
              {c.level === 'error' ? <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
                : c.level === 'warning' ? <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
                  : <Info className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />}
              <span className={cn(c.level === 'error' ? 'text-red-700' : c.level === 'warning' ? 'text-amber-800' : 'text-slate-600')}>{c.message}</span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
