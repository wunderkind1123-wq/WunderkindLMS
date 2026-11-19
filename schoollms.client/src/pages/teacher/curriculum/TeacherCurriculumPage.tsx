import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, BookOpenCheck, Download, Upload } from 'lucide-react'
import {
  curriculumError,
  downloadFile,
  getTeacherCurriculum,
  getTeacherCurriculumOptions,
  type TeacherCurriculumItem,
  type TeacherCurriculumKey,
} from '@/api/services/curriculum'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { CurriculumUploadModal } from '@/pages/admin/curriculum/CurriculumUploadModal'

/**
 * O'qituvchining ish rejalari (curriculum-monitoring.md §1): o'zi dars beradigan sinf/guruh × fan.
 * Ustoz o'z rejasini o'zi yuklaydi (namuna Excel bo'yicha) va tahrirlaydi; o'tgan mavzusiga
 * "o'tildi" belgisini qo'yadi. Har kartada — rejadan oldinda/orqada.
 */
export function TeacherCurriculumPage() {
  const [items, setItems] = useState<TeacherCurriculumItem[] | null>(null)
  const [keys, setKeys] = useState<TeacherCurriculumKey[]>([])
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    getTeacherCurriculum().then(setItems).catch((e) => setError(curriculumError(e, "Ish rejalarni olib bo'lmadi")))
    getTeacherCurriculumOptions().then(setKeys).catch(() => setKeys([]))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!items) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ish reja</h1>
          <p className="text-sm text-slate-400">Darsda o'tgan mavzuingizga «o'tildi» belgisini qo'ying</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => void downloadFile('/teacher/curriculum/template', 'ish-reja-namuna.xlsx')}>
            <Download className="h-4 w-4" /> Namuna
          </Button>
          <Button onClick={() => setUploading(true)} disabled={keys.length === 0} title={keys.length === 0 ? "Dars jadvalida sizga biriktirilgan dars yo'q" : undefined}>
            <Upload className="h-4 w-4" /> Ish rejamni yuklash
          </Button>
        </div>
      </div>
      {items.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-12 text-center">
          <BookOpenCheck className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Siz dars beradigan fanlar uchun hali ish reja yo'q.</p>
          <p className="text-xs text-slate-400">Namunani yuklab oling, to'ldiring va «Ish rejamni yuklash» tugmasini bosing.</p>
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
      {uploading && (
        <CurriculumUploadModal
          teacherKeys={keys}
          plan={null}
          onClose={() => setUploading(false)}
          onSaved={() => {
            setUploading(false)
            load()
          }}
        />
      )}
    </div>
  )
}

/** Rejadan orqada/oldinda — kartada va reja sahifasida. */
export function DeltaBadge({ delta }: { delta: number }) {
  return delta < 0 ? (
    <span className="rounded-md bg-red-50 px-1.5 py-0.5 font-medium text-red-600">{-delta} mavzu orqada</span>
  ) : delta > 0 ? (
    <span className="rounded-md bg-emerald-50 px-1.5 py-0.5 font-medium text-emerald-600">{delta} mavzu oldinda</span>
  ) : (
    <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-slate-500">Reja bo'yicha</span>
  )
}

/** Rejadan orqada/oldinda va tugash prognozi — o'quv bo'limi hisobotidagi bilan bir xil hisob. */
function PaceLine({ item }: { item: TeacherCurriculumItem }) {
  if (item.delta == null && !item.forecastFinish) return null
  return (
    <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
      {item.delta != null && <DeltaBadge delta={item.delta} />}
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
