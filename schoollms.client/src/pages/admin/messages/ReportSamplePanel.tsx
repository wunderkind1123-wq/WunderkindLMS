/**
 * Hisobot qoidalarining "Namuna PDF"i (TZ "O'quvchi hisoboti", 2026-11-17): o'quvchini va davrni tanlab,
 * qoida yuboradigan PDF'ning aynan o'zini ko'rish — birinchi o'quvchi emas. Hech kimga yuborilmaydi.
 *
 *   Haftalik — haftaning istalgan kuni (yakshanba — tugagan hafta), bo'sh — shu hafta;
 *   Oylik    — hisobot oyi, standart — o'tgan oy.
 */
import { useMemo, useState } from 'react'
import { FileDown } from 'lucide-react'
import { downloadMonthlyReportSample, downloadWeeklyReportSample } from '@/api/services/autoMessages'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Select } from '@/components/ui/Input'
import { StudentSelect } from '@/pages/admin/billing/StudentSelect'
import { useStudentsIndex } from '@/pages/admin/billing/useStudents'
import { errorText } from './autoMessageLabels'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** Oxirgi 12 oy, eng yangisi — o'tgan oy. */
function lastMonths(): { value: string; label: string }[] {
  const now = new Date()
  return Array.from({ length: 12 }, (_, i) => {
    const d = new Date(now.getFullYear(), now.getMonth() - 1 - i, 1)
    const value = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
    return { value, label: `${MONTHS[d.getMonth()]} ${d.getFullYear()}` }
  })
}

export function ReportSamplePanel({ monthly, onClose }: { monthly: boolean; onClose: () => void }) {
  const students = useStudentsIndex()
  const months = useMemo(lastMonths, [])
  const [studentId, setStudentId] = useState('')
  const [month, setMonth] = useState(months[0].value)
  const [date, setDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const download = async () => {
    setBusy(true)
    setError(null)
    try {
      if (monthly) await downloadMonthlyReportSample({ studentId, month })
      else await downloadWeeklyReportSample({ studentId, date })
    } catch (e) {
      // Blob javobdagi xabar (responseType: 'blob').
      const data = (e as { response?: { data?: unknown } })?.response?.data
      let text = errorText(e, "Namuna PDF'ni olib bo'lmadi.")
      if (data instanceof Blob) {
        try {
          text = (JSON.parse(await data.text()) as { message?: string }).message ?? text
        } catch {
          /* JSON emas */
        }
      }
      setError(text)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3 rounded-lg border border-slate-200 bg-slate-50/60 p-3">
      <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_200px]">
        <StudentSelect
          options={students.options}
          loading={students.loading}
          error={students.error}
          value={studentId}
          onChange={setStudentId}
          emptyLabel="Birinchi o'quvchi (avtomatik)"
        />
        {monthly ? (
          <Select label="Hisobot oyi" value={month} onChange={(e) => setMonth(e.target.value)}>
            {months.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </Select>
        ) : (
          <DatePicker label="Haftaning kuni" value={date} onChange={setDate} clearable placeholder="Shu hafta" />
        )}
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-slate-400">
          Ota-onaga ketadigan PDF'ning o'zi. Hech kimga yuborilmaydi.
        </p>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={onClose}>
            Yopish
          </Button>
          <Button variant="secondary" onClick={() => void download()} disabled={busy}>
            <FileDown className="h-4 w-4" /> {busy ? 'Tayyorlanmoqda…' : 'Yuklab olish'}
          </Button>
        </div>
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </div>
  )
}
