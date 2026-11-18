import { useEffect, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { todayIso } from './blockDates'

export interface MoveRequest {
  /** `move` — boshqa guruhga ko'chirish; `unassign` — blokdan chiqarish. */
  mode: 'move' | 'unassign'
  studentIds: string[]
  names: string[]
  /** Ko'chirishda — yangi guruh. */
  groupId?: string
  groupName?: string
}

interface Props {
  request: MoveRequest | null
  onClose: () => void
  onSubmit: (date: string, reason: string) => Promise<string | null>
}

/**
 * Ko'chirish oynasi (TZ 6.2): o'quvchi → yangi guruh → sana (standart: bugun) → sabab
 * (ixtiyoriy). Bitta tugma bilan saqlanadi. Eski a'zolik sanadan bir kun oldin yopiladi.
 */
export function BlockMoveModal({ request, onClose, onSubmit }: Props) {
  const [date, setDate] = useState(todayIso())
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!request) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalaymiz (loyihadagi mavjud naqsh)
    setDate(todayIso())
    setReason('')
    setError(null)
  }, [request])

  const handleSubmit = async () => {
    setBusy(true)
    const err = await onSubmit(date, reason.trim())
    setBusy(false)
    if (err) setError(err)
  }

  const isMove = request?.mode === 'move'
  const who = request
    ? request.names.length <= 3
      ? request.names.join(', ')
      : `${request.names.slice(0, 3).join(', ')} va yana ${request.names.length - 3} ta`
    : ''

  return (
    <Modal
      open={Boolean(request)}
      onClose={onClose}
      title={isMove ? "Boshqa guruhga ko'chirish" : 'Blokdan chiqarish'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant={isMove ? 'primary' : 'danger'} onClick={handleSubmit} disabled={busy || !date}>
            {busy ? 'Bajarilmoqda...' : isMove ? "Ko'chirish" : 'Chiqarish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-600">
          <b>{who}</b>{' '}
          {isMove ? (
            <>
              → <b>{request?.groupName}</b>. Shu sanadan yangi ustozning ro'yxatida chiqadi; eski guruhdagi
              baholar joyida qoladi.
            </>
          ) : (
            <>shu sanadan blok guruhida bo'lmaydi.</>
          )}
        </div>
        <DatePicker label="Qaysi sanadan" required value={date} onChange={setDate} />
        <Input
          label="Sabab (ixtiyoriy)"
          placeholder={isMove ? 'Masalan: darajasi yetdi' : 'Masalan: boshqa maktabga ketdi'}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
