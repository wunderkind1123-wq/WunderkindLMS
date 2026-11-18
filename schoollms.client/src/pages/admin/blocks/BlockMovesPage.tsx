import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { BlockMoveItem } from '@/api/services/blocks'
import { getBlockMoves } from '@/api/services/blocks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { formatDmy, todayIso } from './blockDates'

/** Bugundan 6 kun oldin — standart davr bir hafta (haftalik hisobot uchun). */
function weekAgoIso(): string {
  const d = new Date()
  d.setDate(d.getDate() - 6)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/**
 * "Ko'chirishlar" hisoboti — TZ 6.2: tanlangan davrda bir blok guruhidan ikkinchisiga
 * (Kichikdan Kattaga va aksincha) o'tganlar.
 */
export function BlockMovesPage() {
  const navigate = useNavigate()
  const [from, setFrom] = useState(weekAgoIso())
  const [to, setTo] = useState(todayIso())
  const [rows, setRows] = useState<BlockMoveItem[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    if (!from || !to) return
    setLoading(true)
    getBlockMoves(from, to)
      .then(setRows)
      .finally(() => setLoading(false))
  }, [from, to])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda ro'yxatni yangilaymiz (loyihadagi mavjud naqsh)
    load()
  }, [load])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/blocks')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">Ko'chirishlar</h1>
            <p className="text-sm text-slate-400">Davrda blok guruhlari orasida ko'chirilgan o'quvchilar — {rows.length} ta</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <DatePicker value={from} onChange={setFrom} max={to} ariaLabel="Boshlanish" />
          <span className="text-slate-400">—</span>
          <DatePicker value={to} onChange={setTo} min={from} ariaLabel="Tugash" />
        </div>
      </div>

      <Card className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">Sana</th>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Blok</th>
                <th className="px-4 py-3">Qaysi guruhdan → qaysi guruhga</th>
                <th className="px-4 py-3">Sabab</th>
                <th className="px-4 py-3">Kim ko'chirgan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r) => (
                <tr key={`${r.studentId}-${r.blockId}-${r.date}`} className="hover:bg-slate-50/60">
                  <td className="px-4 py-3 text-slate-600">{formatDmy(r.date)}</td>
                  <td className="px-4 py-3">
                    <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-800 hover:text-brand-600 hover:underline">
                      {r.fullName}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-slate-600">{r.blockName}</td>
                  <td className="px-4 py-3 text-slate-600">
                    {r.fromGroup} → <span className="font-medium text-slate-800">{r.toGroup}</span>
                  </td>
                  <td className="px-4 py-3 text-slate-600">{r.reason ?? '—'}</td>
                  <td className="px-4 py-3 text-slate-600">{r.movedBy ?? '—'}</td>
                </tr>
              ))}
              {!loading && rows.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                    Bu davrda ko'chirish bo'lmagan
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}
