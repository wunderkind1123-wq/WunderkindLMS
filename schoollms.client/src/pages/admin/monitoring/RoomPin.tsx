import { useState } from 'react'
import { KeyRound, RefreshCw } from 'lucide-react'
import { monitoringError, rotatePin } from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'

/**
 * Xona PIN'i (curriculum-monitoring.md §2.9): nazoratchi test boshida doskaga yozadi — doimiy kodli
 * o'quvchi kod + PIN bilan kiradi. PIN boshqa xonaga ma'lum bo'lib qolsa — "Yangi PIN".
 */
export function RoomPin({ kind, id, pin, onChanged }: { kind: 'sitting' | 'test'; id: string; pin: string | null; onChanged: () => void }) {
  const [busy, setBusy] = useState(false)

  async function rotate(): Promise<void> {
    if (pin && !confirm('Yangi PIN berilsinmi? Eski PIN bilan endi kirib bo‘lmaydi (ishlayotganlarga ta’sir qilmaydi).')) return
    setBusy(true)
    try {
      await rotatePin(kind, id)
      onChanged()
    } catch (e) {
      alert(monitoringError(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card className="flex flex-wrap items-center justify-between gap-3 border border-amber-200 bg-amber-50/60 p-4 print:hidden">
      <div className="flex items-center gap-3">
        <KeyRound className="h-6 w-6 text-amber-600" />
        <div>
          <p className="text-sm text-amber-900">Xona PIN — test boshida doskaga yozing</p>
          <p className="font-mono text-3xl font-bold tracking-[0.35em] text-amber-900">{pin ?? '— — — —'}</p>
        </div>
      </div>
      <div className="flex flex-col items-end gap-1 text-right">
        <button type="button" onClick={() => void rotate()} disabled={busy} className="inline-flex items-center gap-1 text-sm text-brand-600 hover:underline disabled:opacity-50">
          <RefreshCw className="h-4 w-4" /> {pin ? 'Yangi PIN' : 'PIN berish'}
        </button>
        <p className="max-w-xs text-xs text-amber-800">O‘quvchi doimiy kodi va shu PIN bilan kiradi. Bir martalik kartochka kodi bilan PIN shart emas.</p>
      </div>
    </Card>
  )
}
