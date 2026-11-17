import type { CurriculumFit, CurriculumFitQuarter } from '@/api/services/curriculum'
import { cn, formatDate } from '@/lib/utils'

/**
 * Reja ↔ dars jadvali (mijoz, 2026-11-17): har sinf/guruh × chorak — rejadagi soat va jadvaldagi
 * dars soni. Ko'p — reja sig'maydi (qizil), kam — bo'sh darslar qoladi (sariq), teng — yashil.
 */
export function CurriculumFitTable({ fit, personal }: { fit: CurriculumFit[]; personal: boolean }) {
  if (fit.length === 0) return null
  const quarters = [...new Set(fit.flatMap((f) => f.quarters.map((q) => q.quarter)))].sort()
  const issues = fit.filter((f) => f.dateIssues.length > 0)

  return (
    <div className="space-y-3">
      {quarters.length === 0 ? (
        <p className="text-sm text-amber-700">Chorak sanalari kiritilmagan — jadvaldagi darslarni hisoblab bo'lmaydi.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs text-slate-400">
              <tr>
                <th className="px-3 py-2">{personal ? 'Ustozning darsi' : 'Sinf / guruh'}</th>
                <th className="px-3 py-2 text-right">Haftasiga</th>
                {quarters.map((q) => (
                  <th key={q} className="px-3 py-2 text-center">{q}-chorak</th>
                ))}
                <th className="px-3 py-2 text-center">Yil</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {fit.map((f) => (
                <tr key={f.ownerId}>
                  <td className="px-3 py-2 font-medium text-slate-700">{f.ownerName}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-500">{f.weeklyLessons} dars</td>
                  {quarters.map((q) => {
                    const cell = f.quarters.find((x) => x.quarter === q)
                    return (
                      <td key={q} className="px-2 py-1.5 text-center">
                        {cell ? <FitCell plan={cell.planHours} lessons={cell.lessons} quarter={cell} /> : '—'}
                      </td>
                    )
                  })}
                  <td className="px-2 py-1.5 text-center">
                    <FitCell plan={f.planHours} lessons={f.lessons} />
                    {f.unplacedHours > 0 && (
                      <p className="mt-0.5 text-[11px] text-slate-400">{f.unplacedHours} soat chorakka tushmagan</p>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="text-xs text-slate-400">
        Har katakda: rejadagi soat / jadvaldagi dars (yakshanba va bayramlarsiz).
        {personal ? ' Faqat shu ustozning darslari sanaladi.' : ''} Qizil — reja jadvalga sig'maydi, sariq — bo'sh darslar
        qoladi. «~» — ba'zi haftalarga jadval biriktirilmagan, ular asosiy jadvaldan taxmin qilindi.
      </p>
      {issues.length > 0 && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
          <p className="font-medium">Reja sanasida dars yo'q:</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-5">
            {issues.flatMap((f) =>
              f.dateIssues.slice(0, 20).map((d) => (
                <li key={`${f.ownerId}-${d.order}`}>
                  {formatDate(d.date)} · {d.order}. {d.title} — {d.reason}
                  {fit.length > 1 && d.reason.indexOf(f.ownerName) < 0 ? ` (${f.ownerName})` : ''}
                </li>
              )),
            )}
          </ul>
        </div>
      )}
    </div>
  )
}

function FitCell({ plan, lessons, quarter }: { plan: number; lessons: number; quarter?: CurriculumFitQuarter }) {
  const diff = lessons - plan
  const estimated = (quarter?.estimatedWeeks ?? 0) > 0
  const title =
    diff === 0
      ? 'Reja jadvalga to‘liq mos'
      : diff < 0
        ? `Rejada ${-diff} soat ortiqcha — jadvalda dars yetmaydi`
        : `Jadvalda ${diff} dars ortiqcha — rejada mavzu yetmaydi`
  return (
    <span
      title={estimated ? `${title}. ${quarter!.estimatedWeeks} haftaga jadval biriktirilmagan — taxmin.` : title}
      className={cn(
        'inline-flex min-w-[4.5rem] justify-center rounded-md px-2 py-0.5 text-xs tabular-nums',
        diff === 0 ? 'bg-emerald-50 text-emerald-700' : diff < 0 ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-800',
      )}
    >
      {plan} / {estimated ? '~' : ''}
      {lessons}
    </span>
  )
}
