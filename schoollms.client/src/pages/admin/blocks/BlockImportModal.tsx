import { useEffect, useState } from 'react'
import type { BlockImportError } from '@/api/services/blocks'
import { apiError, importBlocks } from '@/api/services/blocks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { todayIso } from './blockDates'

interface Props {
  open: boolean
  onClose: () => void
  onDone: () => void
}

/**
 * Excel'dan taqsimlash (TZ 6.2): ustunlar — O'quvchi ID, Blok, Guruh. Shablon — blokning
 * "Excel" tugmasi bilan yuklanadigan ro'yxat. Hammasi yoki hech narsa: xato qator bo'lsa,
 * hech narsa saqlanmaydi.
 */
export function BlockImportModal({ open, onClose, onDone }: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [date, setDate] = useState(todayIso())
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [errors, setErrors] = useState<BlockImportError[]>([])

  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalaymiz (loyihadagi mavjud naqsh)
      setFile(null)
      setDate(todayIso())
      setMessage(null)
      setErrors([])
    }
  }, [open])

  const handleSubmit = async () => {
    if (!file) return
    setBusy(true)
    setMessage(null)
    setErrors([])
    try {
      const res = await importBlocks(file, date)
      if (res.ok) {
        alert(
          `Saqlandi: ${res.result.assigned} ta qo'shildi, ${res.result.moved} ta ko'chirildi, ` +
            `${res.result.unchanged} ta o'zgarmadi.`,
        )
        onDone()
      } else {
        setMessage(res.message)
        setErrors(res.errors)
      }
    } catch (e) {
      setMessage(apiError(e, "Import qilib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Excel'dan taqsimlash"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={busy || !file}>
            {busy ? 'Yuklanmoqda...' : 'Yuklash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-600">
          Ustunlar: <b>O'quvchi ID</b>, <b>Blok</b>, <b>Guruh</b>. Shablon sifatida blokning taqsimlash
          sahifasidagi <b>Excel</b> tugmasi bilan ro'yxatni yuklab oling va "Guruh" ustunini to'ldiring.
          Taqsimlanmagan o'quvchi guruhga qo'shiladi, boshqa guruhdagisi tanlangan sanadan ko'chiriladi.
          Birorta qatorda xato bo'lsa, hech narsa saqlanmaydi.
        </div>
        <input
          type="file"
          accept=".xlsx"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-lg file:border-0 file:bg-slate-100 file:px-3 file:py-2 file:text-sm file:text-slate-700"
        />
        <DatePicker label="Qaysi sanadan" value={date} onChange={setDate} required />
        {message && <p className="text-red-600">{message}</p>}
        {errors.length > 0 && (
          <ul className="max-h-48 list-disc overflow-y-auto pl-5 text-red-600">
            {errors.map((e) => (
              <li key={`${e.row}-${e.message}`}>
                {e.row}-qator: {e.message}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  )
}
