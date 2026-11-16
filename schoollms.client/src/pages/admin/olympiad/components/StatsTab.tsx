import { useEffect, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { fmtDay, getEventStats, type EventStats } from '@/api/services/olympiad'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { ErrorBox } from './common'
import { errText } from './lib'

const BAR = '#3b82f6'

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card>
      <h3 className="mb-3 text-sm font-semibold text-slate-700">{title}</h3>
      {children}
    </Card>
  )
}

/** Tadbir → Statistika: yozilish dinamikasi, sinf va maktab bo'yicha, kelganlar foizi. */
export function StatsTab({ eventId }: { eventId: string }) {
  const [s, setS] = useState<EventStats | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getEventStats(eventId)
      .then(setS)
      .catch((e) => setError(errText(e, "Yuklab bo'lmadi")))
  }, [eventId])

  if (error) return <ErrorBox message={error} />
  if (!s) return <Loader label="Yuklanmoqda..." />

  const tiles: [string, string | number][] = [
    ['Jami yozilgan', s.total],
    ["Ro'yxatda", s.registered],
    ['Kutishda', s.waitlist],
    ['Keldi', s.attended],
    ['Kelganlar', s.attendancePercent !== null ? `${s.attendancePercent}%` : '—'],
  ]

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
        {tiles.map(([label, value]) => (
          <div key={label} className="rounded-2xl bg-white px-4 py-3 shadow-sm ring-1 ring-slate-100">
            <p className="text-xs text-slate-400">{label}</p>
            <p className="text-2xl font-semibold tabular-nums text-slate-800">{value}</p>
          </div>
        ))}
      </div>

      <ChartCard title="Kunlar bo'yicha yozilish">
        {s.byDay.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
        ) : (
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={s.byDay.map((d) => ({ ...d, label: fmtDay(d.date).slice(0, 5) }))}>
                <CartesianGrid vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="label" tickLine={false} axisLine={false} fontSize={12} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} fontSize={12} width={32} />
                <Tooltip formatter={(v) => [v, 'Yozildi']} />
                <Bar dataKey="count" fill={BAR} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </ChartCard>

      <div className="grid gap-4 lg:grid-cols-2">
        <ChartCard title="Sinflar bo'yicha">
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={s.byGrade.map((g) => ({ ...g, label: `${g.grade}-sinf` }))}>
                <CartesianGrid vertical={false} stroke="#e2e8f0" />
                <XAxis dataKey="label" tickLine={false} axisLine={false} fontSize={12} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} fontSize={12} width={32} />
                <Tooltip formatter={(v) => [v, 'Bola']} />
                <Bar dataKey="count" fill={BAR} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </ChartCard>
        <ChartCard title="Maktablar bo'yicha (top 20)">
          <ul className="space-y-1.5">
            {s.bySchool.map((x) => {
              const max = s.bySchool[0]?.count || 1
              return (
                <li key={x.school} className="flex items-center gap-2 text-sm">
                  <span className="w-40 shrink-0 truncate text-slate-600" title={x.school}>
                    {x.school}
                  </span>
                  <span className="h-2 flex-1 rounded-full bg-slate-100">
                    <span className="block h-2 rounded-full bg-blue-500" style={{ width: `${(x.count / max) * 100}%` }} />
                  </span>
                  <span className="w-8 text-right tabular-nums text-slate-700">{x.count}</span>
                </li>
              )
            })}
            {s.bySchool.length === 0 && <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>}
          </ul>
        </ChartCard>
      </div>
    </div>
  )
}
