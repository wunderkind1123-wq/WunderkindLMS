import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, BookOpenCheck } from 'lucide-react'
import { curriculumError, getTeacherCurriculum, type TeacherCurriculumItem } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'

/**
 * O'qituvchining ish rejalari (curriculum-monitoring.md §1): o'zi dars beradigan sinf/guruh × fan.
 * Rejani o'quv bo'limi yuklaydi; o'qituvchi o'tgan mavzusiga "o'tildi" belgisini qo'yadi.
 */
export function TeacherCurriculumPage() {
  const [items, setItems] = useState<TeacherCurriculumItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getTeacherCurriculum().then(setItems).catch((e) => setError(curriculumError(e, "Ish rejalarni olib bo'lmadi")))
  }, [])

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!items) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Ish reja</h1>
        <p className="text-sm text-slate-400">Darsda o'tgan mavzuingizga «o'tildi» belgisini qo'ying</p>
      </div>
      {items.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-12 text-center">
          <BookOpenCheck className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Siz dars beradigan fanlar uchun hali ish reja yuklanmagan.</p>
        </Card>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {items.map((i) => {
            const pct = i.topicCount ? Math.round((i.passedCount * 100) / i.topicCount) : 0
            return (
              <Link key={`${i.planId}|${i.ownerId}`} to={`/teacher/ish-reja/${i.planId}`}>
                <Card className="h-full p-4 transition-shadow hover:shadow-md">
                  <p className="font-medium text-slate-800">{i.ownerName} · {i.subjectName}</p>
                  <p className="text-xs text-slate-400">{i.title}</p>
                  <div className="mt-3 h-2 rounded-full bg-slate-100">
                    <div className="h-2 rounded-full bg-brand-500" style={{ width: `${pct}%` }} />
                  </div>
                  <p className="mt-1 text-xs text-slate-500">{i.passedCount}/{i.topicCount} mavzu · {pct}%</p>
                  <PaceLine item={i} />
                  {i.nextTopicTitle && <p className="mt-2 text-sm text-slate-600">Keyingi: {i.nextTopicTitle}</p>}
                </Card>
              </Link>
            )
          })}
        </div>
      )}
    </div>
  )
}

/** Rejadan orqada/oldinda va tugash prognozi — o'quv bo'limi hisobotidagi bilan bir xil hisob. */
function PaceLine({ item }: { item: TeacherCurriculumItem }) {
  if (item.delta == null && !item.forecastFinish) return null
  return (
    <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
      {item.delta != null &&
        (item.delta < 0 ? (
          <span className="rounded-md bg-red-50 px-1.5 py-0.5 font-medium text-red-600">{-item.delta} mavzu orqada</span>
        ) : item.delta > 0 ? (
          <span className="rounded-md bg-emerald-50 px-1.5 py-0.5 font-medium text-emerald-600">{item.delta} mavzu oldinda</span>
        ) : (
          <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-slate-500">Reja bo'yicha</span>
        ))}
      {item.forecastFinish && (
        <span
          className={cn('inline-flex items-center gap-1', item.lateWarning ? 'font-medium text-red-600' : 'text-slate-400')}
          title="Hozirgi tezlikda reja tugaydigan sana"
        >
          {item.lateWarning && <AlertTriangle className="h-3.5 w-3.5" />}
          Tugash: {formatDate(item.forecastFinish)}
          {item.lateWarning && ' — yil oxiriga ulgurmaydi'}
        </span>
      )}
    </div>
  )
}
