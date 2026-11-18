import { useEffect, useState } from 'react'
import { Download, ExternalLink } from 'lucide-react'
import {
  downloadStudentReport,
  getStudentReportPeriods,
  viewStudentReport,
  type StudentReportPeriods,
} from '@/api/services/studentReports'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

/**
 * O'quvchi profilidagi "Hisobot (PDF)" oynasi — TZ "O'quvchi hisoboti", 2026-11-17, 1-vazifa.
 *
 * Davr turini tanlash: O'tgan hafta (standart) · Shu hafta · O'tgan oy · Shu oy · Chorak · Ixtiyoriy
 * oraliq. Sanalarni server hisoblaydi (uning "bugun"i), oyna faqat ko'rsatadi; ular faqat
 * "Ixtiyoriy oraliq"da tahrirlanadi. Hech narsa ota-onaga yuborilmaydi.
 */

type Props = {
  open: boolean
  onClose: () => void
  studentId: string
  studentName: string
}

const CUSTOM = 'custom'
const quarterKey = (q: number) => `q${q}`

/** Blob javobdagi server xabari (`responseType: 'blob'` da xato ham Blob bo'lib keladi). */
async function errorMessage(e: unknown, fallback: string): Promise<string> {
  const data = (e as { response?: { data?: unknown } })?.response?.data
  try {
    if (data instanceof Blob) return (JSON.parse(await data.text()) as { message?: string }).message ?? fallback
    if (data && typeof data === 'object' && 'message' in data) return String((data as { message: unknown }).message)
  } catch {
    /* JSON emas — umumiy xabar */
  }
  return fallback
}

const dmy = (iso: string) => (iso ? iso.split('-').reverse().join('.') : '')

export function StudentReportModal({ open, onClose, studentId, studentName }: Props) {
  const [periods, setPeriods] = useState<StudentReportPeriods | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [kind, setKind] = useState('lastWeek')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [busy, setBusy] = useState<'view' | 'download' | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    let active = true
    getStudentReportPeriods()
      .then((p) => {
        if (!active) return
        setPeriods(p)
        const first = p.presets.find((x) => x.key === 'lastWeek') ?? p.presets[0]
        if (first) {
          setKind(first.key)
          setFrom(first.from)
          setTo(first.to)
        }
      })
      .catch(async (e) => active && setLoadError(await errorMessage(e, "Davrlarni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [open])

  const choose = (key: string) => {
    setKind(key)
    setError(null)
    if (!periods || key === CUSTOM) return
    const preset = periods.presets.find((p) => p.key === key)
    const quarter = periods.quarters.find((q) => quarterKey(q.quarter) === key)
    const range = preset ?? quarter
    if (range) {
      setFrom(range.from)
      setTo(range.to)
    }
  }

  const invalid = !from || !to || from > to
  const run = async (mode: 'view' | 'download') => {
    if (invalid) return
    setBusy(mode)
    setError(null)
    try {
      if (mode === 'view') await viewStudentReport(studentId, from, to)
      else await downloadStudentReport(studentId, from, to)
    } catch (e) {
      setError(await errorMessage(e, "Hisobotni tayyorlab bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  const options = periods
    ? [
        ...periods.presets.map((p) => ({ key: p.key, label: p.label })),
        ...periods.quarters.map((q) => ({
          key: quarterKey(q.quarter),
          label: `${q.quarter}-chorak${q.current ? ' (joriy)' : ''}`,
        })),
        { key: CUSTOM, label: 'Ixtiyoriy oraliq' },
      ]
    : []

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Hisobot (PDF)"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button variant="secondary" disabled={invalid || busy !== null || !periods} onClick={() => run('view')}>
            <ExternalLink className="h-4 w-4" />
            {busy === 'view' ? 'Tayyorlanmoqda…' : "Ko'rish"}
          </Button>
          <Button disabled={invalid || busy !== null || !periods} onClick={() => run('download')}>
            <Download className="h-4 w-4" />
            {busy === 'download' ? 'Tayyorlanmoqda…' : 'Yuklab olish'}
          </Button>
        </>
      }
    >
      {loadError ? (
        <p className="text-sm text-red-600">{loadError}</p>
      ) : !periods ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          <p className="text-sm text-slate-500">
            <span className="font-medium text-slate-800">{studentName}</span> — baholar (sinf o'rtachasi bilan),
            davomat, intizom va qisqa xulosa. Ota-onaga hech narsa yuborilmaydi.
          </p>

          <div>
            <div className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Davr turi</div>
            <div className="flex flex-wrap gap-1.5">
              {options.map((o) => (
                <button
                  key={o.key}
                  type="button"
                  onClick={() => choose(o.key)}
                  className={cn(
                    'rounded-full border px-3 py-1.5 text-sm transition-colors',
                    kind === o.key
                      ? 'border-brand-600 bg-brand-50 font-medium text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {o.label}
                </button>
              ))}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <DatePicker
              label="Sana boshi"
              value={from}
              onChange={setFrom}
              max={to || periods.today}
              disabled={kind !== CUSTOM}
            />
            <DatePicker
              label="Sana oxiri"
              value={to}
              onChange={setTo}
              min={from || undefined}
              max={periods.today}
              disabled={kind !== CUSTOM}
              invalid={!!from && !!to && from > to}
            />
          </div>

          {!invalid && (
            <p className="text-xs text-slate-400">
              Hisobot: {dmy(from)} – {dmy(to)}. Hafta — dushanbadan shanbagacha; oy va undan uzun davrda haftalar
              kesimidagi dinamika ham chiqadi.
            </p>
          )}
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
