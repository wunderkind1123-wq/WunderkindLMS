import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { Block, BlockMonthly, BlockReport } from '@/api/services/blocks'
import { getBlockMonthly, getBlockReport, getBlocks } from '@/api/services/blocks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { todayIso } from './blockDates'

/** Bugundan 6 kun oldin — standart davr bir hafta (shanba hisoboti). */
function weekAgoIso(): string {
  const d = new Date()
  d.setDate(d.getDate() - 6)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

const MONTHS = ['Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun', 'Iyul', 'Avgust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr']

const avg = (n: number | null) => (n == null ? '—' : n.toFixed(2))
const game = (plus: number, minus: number) => (plus === 0 && minus === 0 ? '—' : `+${plus} / −${minus}`)

/**
 * Blok hisoboti — TZ 6.5: davrda blok guruhlari ustozlar kesimida va manba sinflar kesimida
 * (baholar sinf bo'yicha jamlanadi); oylik test natijalari guruh va sinf bo'yicha, balldan
 * yuqoridan pastga. Faqat xodimlar uchun — ota-onaga guruh nomi ko'rsatilmaydi.
 */
export function BlockReportPage() {
  const navigate = useNavigate()
  const [from, setFrom] = useState(weekAgoIso())
  const [to, setTo] = useState(todayIso())
  const [report, setReport] = useState<BlockReport | null>(null)
  const [blocks, setBlocks] = useState<Block[]>([])
  const now = new Date()
  const [blockId, setBlockId] = useState('')
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)
  const [monthly, setMonthly] = useState<BlockMonthly | null>(null)

  useEffect(() => {
    if (from && to) getBlockReport(from, to).then(setReport)
  }, [from, to])

  useEffect(() => {
    getBlocks().then((b) => {
      setBlocks(b)
      setBlockId((cur) => cur || b[0]?.id || '')
    })
  }, [])

  useEffect(() => {
    if (blockId) getBlockMonthly(blockId, year, month).then(setMonthly)
  }, [blockId, year, month])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/blocks')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">Blok hisoboti</h1>
            <p className="text-sm text-slate-400">Blok guruhlari ustozlar va sinflar kesimida, oylik test natijalari</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <DatePicker value={from} onChange={setFrom} max={to} ariaLabel="Boshlanish" />
          <span className="text-slate-400">—</span>
          <DatePicker value={to} onChange={setTo} min={from} ariaLabel="Tugash" />
        </div>
      </div>

      {!report ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <Table
            title="Ustozlar kesimida"
            head={['Blok', 'Guruh', 'Ustoz', 'Darslar', 'Baholar', "O'rtacha", 'Kelmagan', 'Ball (+/−)']}
            rows={report.groups.map((r) => [
              r.block, r.group, r.teacher || '—', r.lessons, r.grades, avg(r.average), r.absent, game(r.gamePlus, r.gameMinus),
            ])}
          />
          <Table
            title="Sinflar kesimida"
            head={['Blok', 'Sinf / yo\'nalish', 'Baholar', "O'rtacha", 'Kelmagan', 'Ball (+/−)']}
            rows={report.classes.map((r) => [r.block, r.source, r.grades, avg(r.average), r.absent, game(r.gamePlus, r.gameMinus)])}
          />
        </>
      )}

      <Card className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <h2 className="mr-auto text-sm font-semibold text-slate-700">Oylik test natijalari</h2>
          <Select label="Blok" value={blockId} onChange={(e) => setBlockId(e.target.value)}>
            {blocks.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </Select>
          <Select label="Oy" value={month} onChange={(e) => setMonth(Number(e.target.value))}>
            {MONTHS.map((m, i) => (
              <option key={m} value={i + 1}>
                {m}
              </option>
            ))}
          </Select>
          <Select label="Yil" value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {[now.getFullYear() - 1, now.getFullYear(), now.getFullYear() + 1].map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </Select>
        </div>
        {monthly && (
          <div className="grid gap-4 lg:grid-cols-2">
            <Ranking title="Guruh bo'yicha" parts={monthly.byGroup} detail={(r) => r.source} />
            <Ranking title="Sinf bo'yicha" parts={monthly.byClass} detail={(r) => r.group} />
          </div>
        )}
      </Card>
    </div>
  )
}

function Table({ title, head, rows }: { title: string; head: string[]; rows: (string | number)[][] }) {
  return (
    <Card className="p-0">
      <h2 className="px-4 pt-4 text-sm font-semibold text-slate-700">{title}</h2>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              {head.map((h) => (
                <th key={h} className="px-4 py-3">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {rows.map((r, i) => (
              <tr key={i}>
                {r.map((c, k) => (
                  <td key={k} className="px-4 py-2.5 text-slate-700">
                    {c}
                  </td>
                ))}
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={head.length} className="px-4 py-8 text-center text-slate-400">
                  Bu davrda ma'lumot yo'q
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function Ranking({
  title,
  parts,
  detail,
}: {
  title: string
  parts: BlockMonthly['byGroup']
  detail: (r: BlockMonthly['byGroup'][number]['rows'][number]) => string
}) {
  return (
    <div className="space-y-3">
      <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</h3>
      {parts.map((p) => (
        <div key={p.name} className="rounded-xl border border-slate-100">
          <p className="border-b border-slate-100 px-3 py-2 text-sm font-semibold text-slate-700">{p.name}</p>
          <ol className="divide-y divide-slate-50 text-sm">
            {p.rows.map((r, i) => (
              <li key={r.studentId} className="flex items-center gap-2 px-3 py-1.5">
                <span className="w-5 text-right text-xs text-slate-400">{i + 1}</span>
                <span className="flex-1 text-slate-800">
                  {r.fullName} <span className="text-slate-400">· {detail(r)}</span>
                </span>
                <span className="font-semibold tabular-nums text-slate-700">{r.score ?? '—'}</span>
              </li>
            ))}
            {p.rows.length === 0 && <li className="px-3 py-2 text-slate-400">O'quvchi yo'q</li>}
          </ol>
        </div>
      ))}
    </div>
  )
}
