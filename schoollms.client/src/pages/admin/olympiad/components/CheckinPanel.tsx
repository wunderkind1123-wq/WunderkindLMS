import { useCallback, useEffect, useRef, useState, type ComponentType } from 'react'
import { CheckCircle2, Search, TriangleAlert, Undo2, XCircle } from 'lucide-react'
import {
  checkinManual,
  finishCheckin,
  getCheckinSummary,
  hm,
  scanQr,
  searchCheckin,
  undoCheckin,
  type CheckinResult,
  type CheckinSummary,
  type OlympiadRegistration,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import { Confirm, ErrorBox, RegBadge } from './common'
import { errText } from './lib'
import { QrScanner } from './QrScanner'

const TONE: Record<CheckinResult['outcome'], { box: string; Icon: typeof CheckCircle2 }> = {
  ok: { box: 'bg-emerald-50 text-emerald-800 border-emerald-200', Icon: CheckCircle2 },
  already: { box: 'bg-amber-50 text-amber-800 border-amber-200', Icon: TriangleAlert },
  error: { box: 'bg-red-50 text-red-700 border-red-200', Icon: XCircle },
}

/** Kirish so'rovlari. Admin paneli — JWT; olimpiada botidagi skaner — Telegram initData (`OlympiadScannerPage`). */
export interface CheckinApi {
  summary: (eventId: string) => Promise<CheckinSummary>
  search: (eventId: string, q: string) => Promise<OlympiadRegistration[]>
  scan: (eventId: string, token: string) => Promise<CheckinResult>
  checkin: (registrationId: string) => Promise<CheckinResult>
  undo: (registrationId: string) => Promise<OlympiadRegistration>
  /** Yo'q bo'lsa «Yakunlash» tugmasi chiqmaydi (botdagi skanerda — faqat admin panelida). */
  finish?: (eventId: string) => Promise<CheckinSummary>
}

const adminCheckinApi: CheckinApi = {
  summary: getCheckinSummary,
  search: searchCheckin,
  scan: scanQr,
  checkin: checkinManual,
  undo: undoCheckin,
  finish: finishCheckin,
}

/**
 * Imtihon kuni kirish (docs/modules/olympiad.md §8): QR skaner, qidiruv, hisob, yakunlash.
 * Ham alohida «Kirish (QR)» sahifasida, ham tadbir sahifasining «Kirish» tabida, ham olimpiada
 * botidagi skanerda (`api` va `Scanner` bilan) ishlatiladi.
 */
export function CheckinPanel({
  eventId,
  closed,
  onFinished,
  api = adminCheckinApi,
  Scanner,
}: {
  eventId: string
  closed?: boolean
  onFinished?: () => void
  api?: CheckinApi
  /** Kamera o'rniga boshqa skaner (masalan, Telegramning o'z QR oynasi). `onScan` natijani qaytaradi. */
  Scanner?: ComponentType<{ onScan: (text: string) => Promise<CheckinResult | null> }>
}) {
  const [summary, setSummary] = useState<CheckinSummary | null>(null)
  const [result, setResult] = useState<CheckinResult | null>(null)
  const [q, setQ] = useState('')
  const [hits, setHits] = useState<OlympiadRegistration[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [confirmFinish, setConfirmFinish] = useState(false)
  const scanning = useRef(false)

  const refresh = useCallback(() => {
    api.summary(eventId).then(setSummary).catch(() => {})
  }, [eventId, api])

  // Tadbir almashsa komponent `key` bilan qayta yaratiladi — bu yerda faqat hisobni o'qiymiz.
  useEffect(refresh, [refresh])

  // Qidiruv — yozish to'xtagach. 2 belgidan qisqa so'rovda natija ko'rsatilmaydi (`shown`).
  const searching = q.trim().length >= 2
  const shown = searching ? hits : []
  useEffect(() => {
    if (q.trim().length < 2) return
    const t = setTimeout(() => {
      api.search(eventId, q.trim()).then(setHits).catch((e) => setError(errText(e)))
    }, 300)
    return () => clearTimeout(t)
  }, [q, eventId, api])

  const replaceHit = (r: OlympiadRegistration | null) => {
    if (!r) return
    setHits((h) => h.map((x) => (x.id === r.id ? r : x)))
  }

  const onScan = async (text: string): Promise<CheckinResult | null> => {
    if (scanning.current) return null
    scanning.current = true
    setError('')
    try {
      const r = await api.scan(eventId, text)
      setResult(r)
      if (r.outcome === 'ok') refresh()
      return r
    } catch (e) {
      setError(errText(e))
      return null
    } finally {
      scanning.current = false
    }
  }

  const manual = async (id: string) => {
    setBusy(true)
    setError('')
    try {
      const r = await api.checkin(id)
      setResult(r)
      replaceHit(r.registration)
      refresh()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  const undo = async (id: string) => {
    setBusy(true)
    setError('')
    try {
      const r = await api.undo(id)
      replaceHit(r)
      if (result?.registration?.id === id) setResult(null)
      refresh()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  const finish = async () => {
    if (!api.finish) return
    setConfirmFinish(false)
    setBusy(true)
    try {
      setSummary(await api.finish(eventId))
      onFinished?.()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  const reg = result?.registration
  const tone = result ? TONE[result.outcome] : null

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-3 gap-2">
        <Counter label="Ro'yxatda" value={summary?.registered} />
        <Counter label="Kirdi" value={summary?.attended} tone="text-emerald-600" />
        <Counter label="Kelmadi" value={summary?.notYet} tone="text-slate-500" />
      </div>

      <ErrorBox message={error} />

      {result && tone && (
        <div className={cn('flex items-start gap-3 rounded-2xl border p-4', tone.box)}>
          <tone.Icon className="mt-0.5 h-7 w-7 shrink-0" />
          <div className="min-w-0 flex-1">
            <p className="text-lg font-semibold">{result.message}</p>
            {reg && (
              <div className="mt-1 text-sm">
                <p className="text-base font-medium">{reg.fullName}</p>
                <p>
                  {reg.number} · {reg.grade}-sinf · {reg.school}
                </p>
              </div>
            )}
          </div>
          {reg && result.outcome !== 'error' && (
            <button className="rounded-lg p-1 opacity-70 hover:opacity-100" title="Belgini bekor qilish" onClick={() => undo(reg.id)}>
              <Undo2 className="h-5 w-5" />
            </button>
          )}
        </div>
      )}

      {Scanner ? <Scanner onScan={onScan} /> : <QrScanner onScan={onScan} />}

      <Card className="space-y-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Ism, telefon yoki raqam (M1-0012)"
            className="w-full rounded-xl border border-slate-200 py-3 pl-9 pr-3 text-base outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>
        {shown.length > 0 && (
          <ul className="divide-y divide-slate-100">
            {shown.map((r) => (
              <li key={r.id} className="flex items-center gap-3 py-2.5">
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-slate-800">{r.fullName}</p>
                  <p className="text-xs text-slate-400">
                    {r.number} · {r.grade}-sinf · {r.school}
                    {r.checkedInAt ? ` · ${hm(r.checkedInAt.slice(11))} ${r.checkedInBy}` : ''}
                  </p>
                </div>
                {r.status === 'attended' ? (
                  <Button variant="secondary" disabled={busy} onClick={() => undo(r.id)}>
                    <Undo2 className="h-4 w-4" />
                  </Button>
                ) : r.status === 'registered' || r.status === 'no_show' ? (
                  <Button disabled={busy} onClick={() => manual(r.id)}>
                    Kirdi
                  </Button>
                ) : (
                  <RegBadge status={r.status} />
                )}
              </li>
            ))}
          </ul>
        )}
        {searching && shown.length === 0 && <p className="text-sm text-slate-400">Topilmadi</p>}
      </Card>

      {api.finish && (
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs text-slate-400">
            {closed ? 'Kirish yakunlangan. Kech kelganni baribir belgilash mumkin.' : "Tadbir tugagach «Yakunlash» — belgilanmaganlar «Kelmadi» bo'ladi."}
          </p>
          <Button variant="secondary" disabled={busy} onClick={() => setConfirmFinish(true)}>
            Yakunlash
          </Button>
        </div>
      )}

      <Confirm open={confirmFinish} title="Kirishni yakunlash" onClose={() => setConfirmFinish(false)} onConfirm={finish} busy={busy}>
        Belgilanmagan {summary?.notYet ?? 0} ta ishtirokchi «Kelmadi» deb belgilanadi, tadbir «Yakunlangan» bo'ladi. Davom etamizmi?
      </Confirm>
    </div>
  )
}

function Counter({ label, value, tone }: { label: string; value: number | undefined; tone?: string }) {
  return (
    <div className="rounded-2xl bg-white px-3 py-3 text-center shadow-sm ring-1 ring-slate-100">
      <p className={cn('text-2xl font-semibold text-slate-800', tone)}>{value ?? '—'}</p>
      <p className="text-xs text-slate-400">{label}</p>
    </div>
  )
}
