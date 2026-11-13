import { useState } from 'react'
import {
  curriculumError,
  importCurriculum,
  uploadOriginal,
  type CurriculumImportResult,
  type CurriculumOptions,
  type CurriculumPlanListItem,
} from '@/api/services/curriculum'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { formatDate } from '@/lib/utils'

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Ish reja yuklash: avval tekshiruv (dry run — mavzular ro'yxati yoki qator xatolari), keyin saqlash.
 * `plan` berilsa — qayta yuklash (fan va ega o'zgarmaydi).
 */
export function CurriculumUploadModal({
  options,
  plan,
  onClose,
  onSaved,
}: {
  options: CurriculumOptions
  plan: CurriculumPlanListItem | null
  onClose: () => void
  onSaved: () => void
}) {
  const [kind, setKind] = useState<'grade' | 'track'>(plan?.groupId ? 'track' : 'grade')
  const [subjectId, setSubjectId] = useState(plan?.subjectId ?? '')
  const [grade, setGrade] = useState(plan?.grade != null ? String(plan.grade) : '')
  const [groupId, setGroupId] = useState(plan?.groupId ?? '')
  const [title, setTitle] = useState(plan?.title ?? '')
  const [file, setFile] = useState<File | null>(null)
  const [original, setOriginal] = useState<File | null>(null)
  const [preview, setPreview] = useState<CurriculumImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const ready = !!file && !!subjectId && (kind === 'grade' ? grade !== '' : !!groupId)

  function input() {
    return {
      file: file!,
      subjectId,
      grade: kind === 'grade' ? Number(grade) : undefined,
      groupId: kind === 'track' ? groupId : undefined,
      title: title.trim() || undefined,
    }
  }

  async function check(): Promise<void> {
    if (!ready) return
    setBusy(true)
    setError(null)
    try {
      setPreview(await importCurriculum(input(), true))
    } catch (e) {
      setError(curriculumError(e, 'Faylni tekshirib bo‘lmadi'))
    } finally {
      setBusy(false)
    }
  }

  async function save(): Promise<void> {
    if (!ready) return
    setBusy(true)
    setError(null)
    try {
      // a Word plan is itself the original document unless another one is attached
      const source = original ?? (file && /\.docx$/i.test(file.name) ? file : null)
      const orig = source ? await uploadOriginal(source) : null
      const result = await importCurriculum({ ...input(), originalUrl: orig?.url, originalName: orig?.fileName }, false)
      if (result.errors.length > 0) setPreview(result)
      else onSaved()
    } catch (e) {
      setError(curriculumError(e, 'Saqlab bo‘lmadi'))
    } finally {
      setBusy(false)
    }
  }

  const okPreview = preview && preview.errors.length === 0

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title={plan ? `Qayta yuklash — ${plan.title}` : 'Ish reja yuklash'}
      footer={
        <div className="flex w-full flex-wrap justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="secondary" onClick={() => void check()} disabled={!ready || busy}>
            Tekshirish
          </Button>
          <Button onClick={() => void save()} disabled={!ready || busy || (preview !== null && !okPreview)}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        {plan && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Mavzular tartib raqami (№) bo'yicha yangilanadi: shu raqamli mavzuning «o'tildi» belgilari saqlanadi,
            yangi faylda yo'q raqamlar belgilari bilan o'chadi.
          </p>
        )}
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="space-y-1 text-sm">
            <span className="font-medium text-slate-600">Fan</span>
            <select className={control} value={subjectId} disabled={!!plan} onChange={(e) => { setSubjectId(e.target.value); setPreview(null) }}>
              <option value="">Tanlang</option>
              {options.subjects.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </label>
          <div className="space-y-1 text-sm">
            <span className="font-medium text-slate-600">Kimga</span>
            <div className="flex gap-2">
              <select className={control} value={kind} disabled={!!plan} onChange={(e) => setKind(e.target.value as 'grade' | 'track')}>
                <option value="grade">Sinf darajasi</option>
                <option value="track">Yo'nalish</option>
              </select>
              {kind === 'grade' ? (
                <select className={control} value={grade} disabled={!!plan} onChange={(e) => setGrade(e.target.value)} aria-label="Sinf darajasi">
                  <option value="">Daraja</option>
                  {options.grades.map((g) => (
                    <option key={g} value={g}>{g}-sinf</option>
                  ))}
                </select>
              ) : (
                <select className={control} value={groupId} disabled={!!plan} onChange={(e) => setGroupId(e.target.value)} aria-label="Yo'nalish">
                  <option value="">Yo'nalish</option>
                  {options.tracks.map((t) => (
                    <option key={t.id} value={t.id}>{t.name}</option>
                  ))}
                </select>
              )}
            </div>
          </div>
          <label className="space-y-1 text-sm sm:col-span-2">
            <span className="font-medium text-slate-600">Nomi (ixtiyoriy)</span>
            <input className={control} value={title} placeholder="Masalan: 5-sinf Matematika (2026–2027)" onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="space-y-1 text-sm">
            <span className="font-medium text-slate-600">Ish reja (Excel .xlsx yoki Word .docx)</span>
            <input type="file" accept=".xlsx,.docx" className="block w-full text-sm" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null) }} />
          </label>
          <label className="space-y-1 text-sm">
            <span className="font-medium text-slate-600">Asl hujjat (Word/PDF, ixtiyoriy)</span>
            <input type="file" accept=".pdf,.doc,.docx,.xls,.xlsx" className="block w-full text-sm" onChange={(e) => setOriginal(e.target.files?.[0] ?? null)} />
          </label>
        </div>

        {error && <p className="text-sm text-red-600">{error}</p>}

        {preview && preview.errors.length > 0 && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            <p className="font-medium">Faylda xatolar bor — tuzatib qayta yuklang:</p>
            <ul className="mt-1 list-disc pl-5">
              {preview.errors.slice(0, 30).map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          </div>
        )}
        {preview && preview.warnings?.length > 0 && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <p className="font-medium">Diqqat — saqlashga to'sqinlik qilmaydi, lekin tekshirib ko'ring:</p>
            <ul className="mt-1 list-disc pl-5">
              {preview.warnings.slice(0, 30).map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          </div>
        )}
        {okPreview && (
          <div>
            <p className="mb-2 text-sm text-emerald-700">
              ✓ {preview.topicCount} ta mavzu, {preview.totalHours} soat
              {preview.preview.some((t) => t.plannedOn) && `, ${preview.preview.filter((t) => t.plannedOn).length} tasida aniq sana`}.
              Tekshirib «Saqlash» ni bosing.
            </p>
            <div className="max-h-72 overflow-auto rounded-lg border border-slate-200">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 bg-slate-50 text-xs text-slate-400">
                  <tr>
                    <th className="px-3 py-2">№</th>
                    <th className="px-3 py-2">Chorak</th>
                    <th className="px-3 py-2">Hafta</th>
                    <th className="px-3 py-2">Sana</th>
                    <th className="px-3 py-2">Mavzu</th>
                    <th className="px-3 py-2 text-right">Soat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {preview.preview.map((t) => (
                    <tr key={t.order}>
                      <td className="px-3 py-1.5 tabular-nums text-slate-400">{t.order}</td>
                      <td className="px-3 py-1.5">{t.quarter ?? '—'}</td>
                      <td className="px-3 py-1.5">{t.week ?? '—'}</td>
                      <td className="whitespace-nowrap px-3 py-1.5 tabular-nums">{t.plannedOn ? formatDate(t.plannedOn) : '—'}</td>
                      <td className="px-3 py-1.5">
                        {t.section && <span className="text-xs text-slate-400">{t.section} · </span>}
                        {t.title}
                      </td>
                      <td className="px-3 py-1.5 text-right tabular-nums">{t.hours}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </Modal>
  )
}
