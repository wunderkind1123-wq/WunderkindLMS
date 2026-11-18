import { useState } from 'react'
import { curriculumError, saveCurriculumTopic, type CurriculumTopic } from '@/api/services/curriculum'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Ish rejadagi bitta mavzuni qo'shish yoki o'zgartirish. Sana majburiy — chorak va hafta undan
 * hisoblanadi. `topic` yo'q — yangi mavzu `afterOrder` dan keyin qo'yiladi.
 */
export function CurriculumTopicModal({
  planId,
  topic,
  afterOrder,
  teacher,
  onClose,
  onSaved,
}: {
  planId: string
  topic: CurriculumTopic | null
  afterOrder: number | null
  teacher: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const [title, setTitle] = useState(topic?.title ?? '')
  const [section, setSection] = useState(topic?.section ?? '')
  // an estimated date (~) is not the teacher's — start empty so they pick the real one
  const [plannedOn, setPlannedOn] = useState(topic?.plannedExact ? (topic.plannedOn ?? '') : '')
  const [hours, setHours] = useState(String(topic?.hours ?? 1))
  const [goal, setGoal] = useState(topic?.goal ?? '')
  const [homework, setHomework] = useState(topic?.homework ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const ready = title.trim() !== '' && plannedOn !== '' && Number(hours) >= 1

  async function save(): Promise<void> {
    if (!ready) return
    setBusy(true)
    setError(null)
    try {
      await saveCurriculumTopic(
        planId,
        topic?.id ?? null,
        {
          title: title.trim(),
          section: section.trim() || null,
          plannedOn,
          hours: Number(hours),
          goal: goal.trim() || null,
          homework: homework.trim() || null,
          afterOrder: topic ? null : afterOrder,
        },
        teacher,
      )
      onSaved()
    } catch (e) {
      setError(curriculumError(e, "Mavzuni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={topic ? `${topic.order}-mavzu` : afterOrder ? `Yangi mavzu — ${afterOrder}-mavzudan keyin` : 'Yangi mavzu'}
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void save()} disabled={!ready || busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm sm:col-span-2">
          <span className="font-medium text-slate-600">Mavzu</span>
          <input className={control} value={title} autoFocus onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="space-y-1 text-sm">
          <span className="font-medium text-slate-600">Dars sanasi</span>
          <input type="date" className={control} value={plannedOn} onChange={(e) => setPlannedOn(e.target.value)} />
          <span className="block text-xs text-slate-400">Chorak va hafta sanadan avtomatik hisoblanadi.</span>
        </label>
        <label className="space-y-1 text-sm">
          <span className="font-medium text-slate-600">Soat</span>
          <input type="number" min={1} max={50} className={control} value={hours} onChange={(e) => setHours(e.target.value)} />
        </label>
        <label className="space-y-1 text-sm sm:col-span-2">
          <span className="font-medium text-slate-600">Bo'lim (modul, ixtiyoriy)</span>
          <input className={control} value={section} onChange={(e) => setSection(e.target.value)} />
        </label>
        <label className="space-y-1 text-sm sm:col-span-2">
          <span className="font-medium text-slate-600">Maqsad / kutilgan natija (ixtiyoriy)</span>
          <textarea rows={2} className={control} value={goal} onChange={(e) => setGoal(e.target.value)} />
        </label>
        <label className="space-y-1 text-sm sm:col-span-2">
          <span className="font-medium text-slate-600">Uy vazifasi (ixtiyoriy)</span>
          <input className={control} value={homework} onChange={(e) => setHomework(e.target.value)} />
        </label>
        {error && <p className="text-sm text-red-600 sm:col-span-2">{error}</p>}
      </div>
    </Modal>
  )
}
