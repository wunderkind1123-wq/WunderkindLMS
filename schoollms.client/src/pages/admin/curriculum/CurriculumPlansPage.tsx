import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { BookOpenCheck, Download, FileText, Paperclip, Trash2, Upload } from 'lucide-react'
import {
  curriculumError,
  deleteCurriculumPlan,
  downloadFile,
  getCurriculumOptions,
  getCurriculumPlans,
  type CurriculumOptions,
  type CurriculumPlanListItem,
} from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatDate } from '@/lib/utils'
import { CurriculumUploadModal } from './CurriculumUploadModal'

/**
 * Ish rejalar (mijoz, 2026-11-06 — docs/modules/curriculum-monitoring.md §1). Bitta reja — fan +
 * sinf darajasi (barcha 5-sinflar) yoki fan + yo'nalish guruhi. O'quv bo'limi Excel namunasi bo'yicha
 * yuklaydi, asl hujjat ham saqlanadi. Qayta yuklash mavzularni yangilaydi, "o'tildi" belgilari qoladi.
 */
export function CurriculumPlansPage() {
  const [plans, setPlans] = useState<CurriculumPlanListItem[]>([])
  const [options, setOptions] = useState<CurriculumOptions | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [upload, setUpload] = useState<CurriculumPlanListItem | 'new' | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    Promise.all([getCurriculumPlans(), getCurriculumOptions()])
      .then(([p, o]) => {
        setPlans(p)
        setOptions(o)
        setError(null)
      })
      .catch((e) => setError(curriculumError(e, "Ish rejalarni olib bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda yuklanadi (loyihadagi naqsh)
    load()
  }, [load])

  async function remove(p: CurriculumPlanListItem): Promise<void> {
    if (!confirm(`«${p.title}» ish rejasi va undagi barcha «o'tildi» belgilari o'chirilsinmi?`)) return
    try {
      await deleteCurriculumPlan(p.id)
      load()
    } catch (e) {
      alert(curriculumError(e, "O'chirib bo'lmadi"))
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ish rejalar</h1>
          <p className="text-sm text-slate-400">
            Fan va sinf darajasi (yoki yo'nalish) bo'yicha to'liq ish reja — qaysi mavzu o'tildi, qaysi biri qoldi
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => void downloadFile('/admin/curriculum/template', 'ish-reja-namuna.xlsx')}>
            <Download className="h-4 w-4" /> Namuna (Excel)
          </Button>
          <Link to="/admin/curriculum/report">
            <Button variant="secondary">
              <BookOpenCheck className="h-4 w-4" /> Hisobot
            </Button>
          </Link>
          <Button onClick={() => setUpload('new')} disabled={!options}>
            <Upload className="h-4 w-4" /> Ish reja yuklash
          </Button>
        </div>
      </div>

      <Card className="p-4 text-sm text-slate-600">
        <p className="font-medium text-slate-700">Ish reja qanday tuziladi</p>
        <ol className="mt-1 list-decimal space-y-0.5 pl-5">
          <li>«Namuna (Excel)» ni yuklab oling. Ustunlar: № · Chorak · Hafta · Bo'lim · <b>Mavzu</b> · Soat · Maqsad · Uy vazifasi.</li>
          <li>Har mavzu alohida qatorda, butun o'quv yili uchun, o'qituvchi darsda ishlatadigan to'liq nomi bilan.</li>
          <li>«Ish reja yuklash» — fan va sinf darajasini (yoki yo'nalishni) tanlang, Excel faylni va xohlasangiz asl hujjatni (Word/PDF) biriktiring.</li>
          <li>O'qituvchilar o'z sinflarida mavzuga «o'tildi» belgisini qo'yadi; hisobotda kim rejadan orqada ekanligi ko'rinadi.</li>
        </ol>
      </Card>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : plans.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <FileText className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Hali ish reja yuklanmagan</p>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Ish reja</th>
                  <th className="px-4 py-3">Kimga</th>
                  <th className="px-4 py-3">Sinflar / guruhlar</th>
                  <th className="px-4 py-3 text-right">Mavzu</th>
                  <th className="px-4 py-3 text-right">Soat</th>
                  <th className="px-4 py-3 text-right">O'tilgan (o'rtacha)</th>
                  <th className="px-4 py-3">Yangilangan</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {plans.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <Link to={`/admin/curriculum/${p.id}`} className="font-medium text-brand-700 hover:underline">
                        {p.title}
                      </Link>
                      <p className="text-xs text-slate-400">{p.subjectName}</p>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{p.groupName ?? `${p.grade}-sinf`}</td>
                    <td className="px-4 py-3 text-slate-500">{p.owners.length ? p.owners.join(', ') : '—'}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{p.topicCount}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{p.totalHours}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{p.averagePercent}%</td>
                    <td className="px-4 py-3 text-slate-500">{formatDate(p.updatedAt.slice(0, 10))}</td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end gap-0.5">
                        {p.fileUrl && (
                          <a href={p.fileUrl} target="_blank" rel="noreferrer" title={p.fileName ?? 'Asl hujjat'}
                            className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-brand-600">
                            <Paperclip className="h-4 w-4" />
                          </a>
                        )}
                        <button type="button" title="Qayta yuklash" onClick={() => setUpload(p)}
                          className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
                          <Upload className="h-4 w-4" />
                        </button>
                        <button type="button" title="O'chirish" onClick={() => void remove(p)}
                          className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {upload && options && (
        <CurriculumUploadModal
          options={options}
          plan={upload === 'new' ? null : upload}
          onClose={() => setUpload(null)}
          onSaved={() => {
            setUpload(null)
            load()
          }}
        />
      )}
    </div>
  )
}
