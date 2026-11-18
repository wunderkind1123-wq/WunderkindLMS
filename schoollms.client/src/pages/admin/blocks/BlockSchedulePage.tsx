import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { AlertTriangle, ArrowLeft, Check, Users } from 'lucide-react'
import type { Block, BlockSlot } from '@/api/services/blocks'
import {
  DAY_PATTERN_LABELS,
  apiError,
  applyBlockPattern,
  getBlock,
  getBlockSchedule,
  setBlockSchedule,
} from '@/api/services/blocks'
import { weekDays, schedulePeriods } from '@/config/constants'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

/** Naqshga mos kunlarmi (server bilan bir xil qoida: toq — 0, 2, 4; juft — 1, 3, 5). */
function allows(pattern: Block['dayPattern'], day: number): boolean {
  if (pattern === 'odd') return day % 2 === 0
  if (pattern === 'even') return day % 2 === 1
  return true
}

/**
 * Blok jadvali — TZ 6.3. Katak bosilsa blok shu soatga qo'yiladi (yoki olinadi): ikkala guruhda
 * bir vaqtda, barcha manba sinflar qatorida ko'rinadi. Naqshga zid kun yopiq (9-qoida). "Toq
 * kunlarga qo'llash" / "Juft kunlarga qo'llash" — bitta soatni butun haftaga. Sinf, ustoz yoki
 * xona band bo'lsa (3–5-qoidalar) server rad etadi va sababini aytadi.
 */
export function BlockSchedulePage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [block, setBlock] = useState<Block | null>(null)
  const [slots, setSlots] = useState<BlockSlot[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    Promise.all([getBlock(id), getBlockSchedule(id)]).then(([b, s]) => {
      setBlock(b)
      setSlots(s)
    })
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  const has = (day: number, period: number) => slots.some((s) => s.day === day && s.period === period)

  const run = async (action: () => Promise<BlockSlot[]>) => {
    setBusy(true)
    setError(null)
    try {
      setSlots(await action())
    } catch (e) {
      setError(apiError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const toggle = (day: number, period: number) =>
    run(() =>
      has(day, period)
        ? setBlockSchedule(id, [], [{ day, period }])
        : setBlockSchedule(id, [{ day, period }], []),
    )

  if (!block) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/blocks')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">{block.name} — jadval</h1>
            <p className="text-sm text-slate-400">
              {block.subjectName} · {DAY_PATTERN_LABELS[block.dayPattern]} · {block.sources.map((s) => s.name).join(', ')}
            </p>
          </div>
        </div>
        <Button variant="secondary" onClick={() => navigate(`/admin/blocks/${id}/taqsimlash`)}>
          <Users className="h-4 w-4" /> Taqsimlash
        </Button>
      </div>

      {!block.isActive && (
        <Card className="border-amber-200 bg-amber-50 text-sm text-amber-800">
          Blok hali faol emas: soatlar band qilinadi, lekin darslar jurnal va davomatda blok faollashgandan keyin
          ko'rinadi.
        </Card>
      )}
      {error && (
        <Card className="flex items-start gap-2 border-red-200 bg-red-50 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </Card>
      )}

      <Card className="p-0">
        <div className="overflow-x-auto p-4">
          <table className="w-full border-separate border-spacing-0 text-sm">
            <thead>
              <tr className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <th className="w-16 px-2 py-2 text-left font-medium">Dars</th>
                {weekDays.slice(0, 6).map((d) => (
                  <th key={d} className="min-w-[110px] px-2 py-2 text-left font-medium">
                    {d}
                  </th>
                ))}
                {block.dayPattern !== 'custom' && <th className="px-2 py-2" />}
              </tr>
            </thead>
            <tbody>
              {schedulePeriods.map((period) => (
                <tr key={period}>
                  <td className="px-2 py-1 text-slate-500">{period}-dars</td>
                  {weekDays.slice(0, 6).map((_, day) => {
                    const ok = allows(block.dayPattern, day)
                    const on = has(day, period)
                    return (
                      <td key={day} className="px-1 py-1">
                        <button
                          type="button"
                          disabled={busy || (!ok && !on)}
                          title={ok ? (on ? 'Olib tashlash' : "Blokni shu soatga qo'yish") : 'Kun naqshiga mos emas'}
                          onClick={() => toggle(day, period)}
                          className={cn(
                            'flex min-h-[44px] w-full flex-col justify-center rounded-lg border p-2 text-left text-xs transition-colors',
                            on
                              ? 'border-2 border-amber-400 bg-amber-50 text-slate-800'
                              : ok
                                ? 'border-dashed border-slate-200 text-slate-300 hover:border-amber-300 hover:bg-amber-50/50'
                                : 'cursor-not-allowed border-slate-100 bg-slate-50 text-slate-200',
                          )}
                        >
                          {on &&
                            block.groups.map((g) => (
                              <span key={g.id} className="truncate">
                                <b>{g.name}</b> · {g.teacherName ?? '—'}
                              </span>
                            ))}
                        </button>
                      </td>
                    )
                  })}
                  {block.dayPattern !== 'custom' && (
                    <td className="px-1 py-1">
                      <Button
                        variant="ghost"
                        className="whitespace-nowrap px-2 py-1 text-xs"
                        disabled={busy}
                        onClick={() => run(() => applyBlockPattern(id, period, block.dayPattern as 'odd' | 'even'))}
                      >
                        <Check className="h-3.5 w-3.5" />
                        {block.dayPattern === 'odd' ? 'Toq kunlarga qo\'llash' : 'Juft kunlarga qo\'llash'}
                      </Button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}
