import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, ArrowLeft } from 'lucide-react'
import { curriculumError, getCurriculumReport, type CurriculumReport } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Ish reja hisoboti (curriculum-monitoring.md §1): har sinf/guruh × fan — o'tilgan va qolgan,
 * rejadan orqada/oldinda, hozirgi tezlikda tugash sanasi (o'quv yili oxiriga ulgurmasa —
 * ogohlantirish), chorak bo'yicha reja va o'tilgan; hamda jadvalda bor-u ish rejasi yo'q fanlar.
 */
export function CurriculumReportPage() {
  const [data, setData] = useState<CurriculumReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [owner, setOwner] = useState('')
  const [onlyBehind, setOnlyBehind] = useState(false)

  useEffect(() => {
    getCurriculumReport().then(setData).catch((e) => setError(curriculumError(e, "Hisobotni olib bo'lmadi")))
  }, [])

  const owners = useMemo(() => [...new Set(data?.rows.map((r) => r.ownerName) ?? [])], [data])
  const rows = (data?.rows ?? []).filter(
    (r) => (!owner || r.ownerName === owner) && (!onlyBehind || (r.delta ?? 0) < 0 || r.lateWarning),
  )
  const quarterNums = data?.rows[0]?.quarters.map((q) => q.quarter) ?? []

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!data) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link to="/admin/curriculum" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100" aria-label="Orqaga">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ish reja hisoboti</h1>
          <p className="text-sm text-slate-400">
            Bugun {formatDate(data.today)}
            {data.yearEnd ? ` · o'quv yili tugashi ${formatDate(data.yearEnd)}` : ''}
          </p>
        </div>
      </div>

      {!data.quartersSet && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
          Choraklar sanasi kiritilmagan (Sozlamalar → Choraklar) — «rejadan orqada/oldinda» va chorak bo'yicha reja hisoblanmaydi.
        </p>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <select className={control} value={owner} onChange={(e) => setOwner(e.target.value)} aria-label="Sinf yoki guruh">
            <option value="">Barcha sinf va guruhlar</option>
            {owners.map((o) => (
              <option key={o} value={o}>{o}</option>
            ))}
          </select>
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" checked={onlyBehind} onChange={(e) => setOnlyBehind(e.target.checked)} />
            Faqat rejadan orqada qolganlar
          </label>
        </div>
        {rows.length === 0 ? (
          <p className="p-10 text-center text-sm text-slate-400">Ma'lumot yo'q — avval ish reja yuklang.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sinf / guruh</th>
                  <th className="px-4 py-3">Fan</th>
                  <th className="px-4 py-3 text-right">O'tilgan</th>
                  <th className="px-4 py-3 text-right">Qolgan</th>
                  <th className="px-4 py-3">Reja bo'yicha</th>
                  <th className="px-4 py-3">Tugash prognozi</th>
                  {quarterNums.map((q) => (
                    <th key={q} className="px-3 py-3 text-center">{q}-chorak</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={`${r.planId}|${r.ownerId}`} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{r.ownerName}</td>
                    <td className="px-4 py-3">
                      <Link to={`/admin/curriculum/${r.planId}`} className="text-brand-700 hover:underline">{r.subjectName}</Link>
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {r.passedCount}/{r.topicCount} <span className="text-slate-400">({r.percent}%)</span>
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {r.topicCount - r.passedCount} <span className="text-slate-400">· {r.totalHours - r.passedHours} soat</span>
                    </td>
                    <td className="px-4 py-3">
                      {r.delta == null ? (
                        <span className="text-slate-300">—</span>
                      ) : r.delta < 0 ? (
                        <span className="font-medium text-red-600">{-r.delta} mavzu orqada</span>
                      ) : r.delta > 0 ? (
                        <span className="font-medium text-emerald-600">{r.delta} mavzu oldinda</span>
                      ) : (
                        <span className="text-slate-600">rejada</span>
                      )}
                      {r.expected != null && <span className="ml-1 text-xs text-slate-400">(kutilgan {r.expected})</span>}
                    </td>
                    <td className={cn('whitespace-nowrap px-4 py-3', r.lateWarning ? 'font-medium text-red-600' : 'text-slate-600')}>
                      {r.forecastFinish ? (
                        <span className="inline-flex items-center gap-1">
                          {r.lateWarning && <AlertTriangle className="h-4 w-4" />}
                          {formatDate(r.forecastFinish)}
                        </span>
                      ) : (
                        <span className="text-slate-300">hali belgi yo'q</span>
                      )}
                    </td>
                    {r.quarters.map((q) => (
                      <td key={q.quarter} className="px-3 py-3 text-center tabular-nums text-slate-600">
                        {q.passed}/{q.planned}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card>
        <h2 className="text-sm font-semibold text-slate-700">Ish rejasi yuklanmagan fanlar</h2>
        <p className="mb-3 text-xs text-slate-400">Dars jadvalida bor, lekin ish rejasi yo'q (fan + sinf darajasi yoki yo'nalish)</p>
        {data.missing.length === 0 ? (
          <p className="text-sm text-emerald-600">Hammasining ish rejasi bor ✓</p>
        ) : (
          <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {data.missing.map((m) => (
              <li key={`${m.subjectId}|${m.grade}|${m.groupId}`} className="rounded-lg border border-slate-200 px-3 py-2 text-sm">
                <p className="font-medium text-slate-700">
                  {m.groupName ?? `${m.grade}-sinf`} · {m.subjectName}
                </p>
                <p className="text-xs text-slate-400">{m.owners.join(', ')}</p>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
