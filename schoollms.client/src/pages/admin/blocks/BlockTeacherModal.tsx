import { useEffect, useState } from 'react'
import type { Teacher } from '@/types'
import type { Block, BlockGroup, BlockTeacherPeriod } from '@/api/services/blocks'
import { apiError, changeBlockTeacher, getBlockTeacherHistory } from '@/api/services/blocks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { formatDmy, todayIso } from './blockDates'

interface Props {
  /** null — oyna yopiq. */
  group: BlockGroup | null
  teachers: Teacher[]
  onClose: () => void
  onDone: (block: Block) => void
}

/**
 * Blok guruhi ustozini almashtirish (TZ 6.1): yangi ustoz va sana. Eski ustoz sanadan bir kun
 * oldingacha, yangisi shu sanadan; o'sha kungacha qo'yilgan baholar eski ustozda qoladi.
 */
export function BlockTeacherModal({ group, teachers, onClose, onDone }: Props) {
  const [teacherId, setTeacherId] = useState('')
  const [date, setDate] = useState(todayIso())
  const [history, setHistory] = useState<BlockTeacherPeriod[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!group) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalaymiz (loyihadagi mavjud naqsh)
    setTeacherId('')
    setDate(todayIso())
    setError(null)
    getBlockTeacherHistory(group.id).then(setHistory)
  }, [group])

  const handleSubmit = async () => {
    if (!group || !teacherId) return
    setBusy(true)
    setError(null)
    try {
      onDone(await changeBlockTeacher(group.id, teacherId, date))
    } catch (e) {
      setError(apiError(e, "Almashtirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={Boolean(group)}
      onClose={onClose}
      title={`Ustozni almashtirish — ${group?.name ?? ''}`}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={busy || !teacherId}>
            {busy ? 'Bajarilmoqda...' : 'Almashtirish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-600">
          Hozirgi ustoz: <b>{group?.teacherName ?? '—'}</b>. Tanlangan sanagacha qo'yilgan baholar unda qoladi.
        </div>
        <Select label="Yangi ustoz" required value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
          <option value="">Tanlang...</option>
          {teachers
            .filter((t) => t.id !== group?.teacherId)
            .map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
        </Select>
        <DatePicker label="Qaysi sanadan" required value={date} onChange={setDate} />
        {history.length > 0 && (
          <div>
            <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-400">Tarix</p>
            <ul className="space-y-0.5 text-slate-600">
              {history.map((h) => (
                <li key={`${h.teacherId}-${h.validFrom}`}>
                  {h.teacherName}: {formatDmy(h.validFrom)} — {h.validTo ? formatDmy(h.validTo) : 'hozirgacha'}
                </li>
              ))}
            </ul>
          </div>
        )}
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
