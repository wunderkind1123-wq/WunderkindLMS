import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Check, Paperclip } from 'lucide-react'
import {
  curriculumError,
  getCurriculumPlan,
  markCurriculumTopic,
  type CurriculumPlanDetail,
  type CurriculumTopic,
} from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'

/**
 * Bitta ish reja: mavzular × sinf/guruh. Katakni bosish — "o'tildi" (bugungi sana bilan) yoki
 * belgini olib tashlash. `teacher` — o'qituvchi rejimi: faqat o'zi dars beradigan sinf/guruhlar.
 */
export function CurriculumPlanPage({ teacher = false }: { teacher?: boolean }) {
  const { id = '' } = useParams()
  const [plan, setPlan] = useState<CurriculumPlanDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const today = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Tashkent' })
  const back = teacher ? '/teacher/ish-reja' : '/admin/curriculum'

  const load = useCallback(() => {
    getCurriculumPlan(id, teacher)
      .then((p) => {
        setPlan(p)
        setError(null)
      })
      .catch((e) => setError(curriculumError(e, "Ish rejani olib bo'lmadi")))
  }, [id, teacher])

  useEffect(() => {
    load()
  }, [load])

  async function toggle(topic: CurriculumTopic, ownerKind: string, ownerId: string, passed: boolean): Promise<void> {
    const key = `${topic.id}|${ownerId}`
    setBusy(key)
    try {
      await markCurriculumTopic({ topicId: topic.id, ownerKind, ownerId, passed: !passed }, teacher)
      load()
    } catch (e) {
      alert(curriculumError(e, "Belgilab bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!plan) return <Loader label="Yuklanmoqda..." />

  const passedCount = (ownerId: string) => plan.topics.filter((t) => t.marks.some((m) => m.ownerId === ownerId)).length
  // a section header row before the first topic of each section
  const sectionStarts = new Set(plan.topics.filter((t, i) => t.section && t.section !== plan.topics[i - 1]?.section).map((t) => t.id))

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <Link to={back} className="rounded-lg p-2 text-slate-500 hover:bg-slate-100" aria-label="Orqaga">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="min-w-0">
          <h1 className="text-xl font-semibold text-slate-800">{plan.title}</h1>
          <p className="text-sm text-slate-400">
            {plan.subjectName} · {plan.topics.length} mavzu · {plan.topics.reduce((s, t) => s + t.hours, 0)} soat
            {plan.fileUrl && (
              <>
                {' · '}
                <a href={plan.fileUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-brand-600 hover:underline">
                  <Paperclip className="h-3.5 w-3.5" /> {plan.fileName ?? 'Asl hujjat'}
                </a>
              </>
            )}
          </p>
        </div>
      </div>

      {plan.owners.length === 0 ? (
        <Card className="p-6 text-sm text-slate-500">
          Bu rejani o'tadigan sinf yoki guruh topilmadi (dars jadvalida shu fan yo'q).
        </Card>
      ) : (
        <>
          <div className="flex flex-wrap gap-2">
            {plan.owners.map((o) => {
              const n = passedCount(o.id)
              const pct = plan.topics.length ? Math.round((n * 100) / plan.topics.length) : 0
              return (
                <div key={o.id} className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm">
                  <span className="font-medium text-slate-700">{o.name}</span>
                  <span className="ml-2 tabular-nums text-slate-500">{n}/{plan.topics.length} · {pct}%</span>
                </div>
              )
            })}
          </div>
          <p className="text-xs text-slate-400">
            Katakni bosing — mavzu bugungi sana bilan «o'tildi» deb belgilanadi; qayta bosish belgini olib tashlaydi.
            «Reja» — chorak va haftaga ko'ra kutilgan sana; o'tib ketgan, lekin belgilanmagan mavzu qizil.
          </p>
          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-3">№</th>
                    <th className="px-3 py-3">Mavzu</th>
                    <th className="px-3 py-3">Ch./hafta</th>
                    <th className="px-3 py-3 text-right">Soat</th>
                    <th className="px-3 py-3">Reja</th>
                    {plan.owners.map((o) => (
                      <th key={o.id} className="px-3 py-3 text-center normal-case">{o.name}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {plan.topics.map((t) => {
                    const sectionRow = sectionStarts.has(t.id)
                    return (
                      <FragmentRows key={t.id} section={sectionRow ? t.section : null} colSpan={5 + plan.owners.length}>
                        <tr className="hover:bg-slate-50/60">
                          <td className="px-3 py-2 tabular-nums text-slate-400">{t.order}</td>
                          <td className="px-3 py-2">
                            <p className="text-slate-800">{t.title}</p>
                            {t.goal && <p className="text-xs text-slate-400">{t.goal}</p>}
                          </td>
                          <td className="whitespace-nowrap px-3 py-2 text-slate-500">
                            {t.quarter ? `${t.quarter}-ch.` : '—'}
                            {t.week ? ` ${t.week}-h.` : ''}
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums">{t.hours}</td>
                          <td className="whitespace-nowrap px-3 py-2 text-slate-500">{t.plannedOn ? formatDate(t.plannedOn) : '—'}</td>
                          {plan.owners.map((o) => {
                            const mark = t.marks.find((m) => m.ownerId === o.id)
                            const overdue = !mark && t.plannedOn != null && t.plannedOn < today
                            const key = `${t.id}|${o.id}`
                            return (
                              <td key={o.id} className="px-2 py-1.5 text-center">
                                <button
                                  type="button"
                                  disabled={busy === key}
                                  onClick={() => void toggle(t, o.kind, o.id, !!mark)}
                                  title={mark ? `O'tilgan: ${formatDate(mark.passedOn)} — bosib olib tashlang` : "O'tildi deb belgilash"}
                                  className={cn(
                                    'inline-flex min-w-[5.5rem] items-center justify-center gap-1 rounded-lg border px-2 py-1 text-xs transition-colors',
                                    mark
                                      ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
                                      : overdue
                                        ? 'border-red-200 bg-red-50/60 text-red-400 hover:bg-red-50'
                                        : 'border-slate-200 text-slate-300 hover:border-brand-300 hover:text-brand-600',
                                  )}
                                >
                                  {mark ? (
                                    <>
                                      <Check className="h-3.5 w-3.5" /> {formatDate(mark.passedOn).slice(0, 5)}
                                    </>
                                  ) : (
                                    "o'tilmagan"
                                  )}
                                </button>
                              </td>
                            )
                          })}
                        </tr>
                      </FragmentRows>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  )
}

function FragmentRows({ section, colSpan, children }: { section: string | null; colSpan: number; children: React.ReactNode }) {
  return (
    <>
      {section && (
        <tr className="bg-slate-50/70">
          <td colSpan={colSpan} className="px-3 py-1.5 text-xs font-semibold uppercase tracking-wide text-slate-500">
            {section}
          </td>
        </tr>
      )}
      {children}
    </>
  )
}
