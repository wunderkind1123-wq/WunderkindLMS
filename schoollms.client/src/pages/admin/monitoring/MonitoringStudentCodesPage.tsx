import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, FileSpreadsheet, KeyRound, Printer, RefreshCw } from 'lucide-react'
import {
  generateStudentCodes,
  getSittings,
  getStudentCodes,
  monitoringError,
  regenerateStudentCode,
  studentCodesExportPath,
  type MonitoringSittingListItem,
  type MonitoringStudentCode,
} from '@/api/services/monitoring'
import { downloadFile } from '@/api/services/curriculum'
import { formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { control } from './MonitoringBanksPage'

/**
 * Doimiy test kodlari (curriculum-monitoring.md §2.9): har o'quvchiga bir marta — yil bo'yi har
 * oylik testda ishlaydi. Testga kirish: kod + doskadagi xona PIN'i. Kartochkalar sinf bo'yicha
 * chop etiladi; kod yo'qolsa — "Yangi kod" (eskisi darhol ishlamaydi).
 */
export function MonitoringStudentCodesPage() {
  const [rows, setRows] = useState<MonitoringStudentCode[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [className, setClassName] = useState('')
  const [busy, setBusy] = useState(false)
  const [note, setNote] = useState<string | null>(null)
  const [sittings, setSittings] = useState<MonitoringSittingListItem[]>([])

  const load = useCallback(() => {
    getStudentCodes().then(setRows).catch((e) => setError(monitoringError(e, "Kodlarni olib bo'lmadi")))
    getSittings().then(setSittings).catch(() => setSittings([]))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const classes = useMemo(() => [...new Set((rows ?? []).map((r) => r.className))].sort((a, b) => a.localeCompare(b, 'uz', { numeric: true })), [rows])
  const shown = useMemo(() => (rows ?? []).filter((r) => !className || r.className === className), [rows, className])
  const missing = (rows ?? []).filter((r) => !r.code).length

  async function generate(): Promise<void> {
    setBusy(true)
    setNote(null)
    try {
      const added = await generateStudentCodes()
      setNote(added > 0 ? `${added} o‘quvchiga kod berildi` : 'Hamma o‘quvchida kod bor')
      load()
    } catch (e) {
      setNote(monitoringError(e))
    } finally {
      setBusy(false)
    }
  }

  async function exportExcel(): Promise<void> {
    setNote(null)
    try {
      await downloadFile(studentCodesExportPath(className || undefined), className ? `test-kodlari-${className}.xlsx` : 'test-kodlari.xlsx')
    } catch (e) {
      setNote(monitoringError(e, "Excel'ni yuklab bo'lmadi"))
    }
  }

  async function regenerate(r: MonitoringStudentCode): Promise<void> {
    if (!confirm(`${r.studentName} uchun yangi kod berilsinmi? Eski kod (${r.code}) darhol ishlamay qoladi.`)) return
    try {
      const code = await regenerateStudentCode(r.studentId)
      setRows((list) => list?.map((x) => (x.studentId === r.studentId ? { ...x, code } : x)) ?? null)
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!rows) return <Loader label="Yuklanmoqda..." />
  const link = `${window.location.origin}/monitoring`

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3 print:hidden">
        <div>
          <Link to="/admin/monitoring" className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700">
            <ArrowLeft className="h-4 w-4" /> Monitoring testlari
          </Link>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">Doimiy test kodlari</h1>
          <p className="max-w-2xl text-sm text-slate-500">
            Har o‘quvchiga bir marta beriladi va har oylik testda ishlaydi. Test kuni o‘quvchi <b className="font-mono">{link}</b> sahifasida
            o‘z kodini va nazoratchi doskaga yozgan <b>xona PIN</b>’ini kiritadi — PIN sessiya sahifasida ko‘rinadi.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <select className={control.replace('w-full', 'w-40')} value={className} onChange={(e) => setClassName(e.target.value)} aria-label="Sinf">
            <option value="">Barcha sinflar</option>
            {classes.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
          <Button variant="secondary" onClick={() => void exportExcel()} disabled={shown.every((r) => !r.code)}>
            <FileSpreadsheet className="h-4 w-4" /> Excel yuklab olish
          </Button>
          <Button variant="secondary" onClick={() => window.print()} disabled={shown.every((r) => !r.code)}>
            <Printer className="h-4 w-4" /> Kartochkalar (chop etish / PDF)
          </Button>
          <Button onClick={() => void generate()} disabled={busy || missing === 0}>
            <KeyRound className="h-4 w-4" /> {missing > 0 ? `Kodsizlarga kod berish (${missing})` : 'Hammada kod bor'}
          </Button>
        </div>
      </div>
      {note && <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700 print:hidden">{note}</p>}

      <PinHelp sittings={sittings} />

      <Card className="p-0 print:hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2">O‘quvchi</th>
                <th className="px-4 py-2">Sinf</th>
                <th className="px-4 py-2">Kod</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {shown.map((r) => (
                <tr key={r.studentId}>
                  <td className="px-4 py-2 text-slate-800">{r.studentName}</td>
                  <td className="px-4 py-2 text-slate-500">{r.className}</td>
                  <td className="px-4 py-2 font-mono text-base tracking-widest">{r.code ?? <span className="text-xs text-slate-300">—</span>}</td>
                  <td className="px-4 py-2 text-right">
                    {r.code && (
                      <button type="button" onClick={() => void regenerate(r)} className="inline-flex items-center gap-1 text-xs text-brand-600 hover:underline">
                        <RefreshCw className="h-3.5 w-3.5" /> Yangi kod
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* chop etish: o'quvchiga beriladigan kartochka (kundalikka yopishtiriladi) */}
      <div className="hidden grid-cols-3 gap-2 print:grid">
        {shown.filter((r) => r.code).map((r) => (
          <div key={r.studentId} className="break-inside-avoid rounded border border-slate-300 p-3 text-xs">
            <p className="font-semibold">{r.studentName}</p>
            <p className="text-slate-600">{r.className} · Wunderkind test kodi</p>
            <p className="mt-1 font-mono text-lg tracking-[0.25em]">{r.code}</p>
            <p className="text-slate-500">{link} · + doskadagi PIN</p>
          </div>
        ))}
      </div>
    </div>
  )
}

/**
 * "PIN qayerdan olinadi?" — har sessiyaning o'z xona PIN'i bor (sessiya yaratilganda beriladi);
 * so'nggi sessiyalar PIN'i bilan shu yerda ham, to'liq boshqaruvi (yangi PIN) — sessiya sahifasida.
 */
function PinHelp({ sittings }: { sittings: MonitoringSittingListItem[] }) {
  const recent = [...sittings].sort((a, b) => b.testDate.localeCompare(a.testDate)).slice(0, 6)
  return (
    <Card className="border border-amber-200 bg-amber-50/60 p-4 print:hidden">
      <div className="flex items-start gap-3">
        <KeyRound className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
        <div className="min-w-0 flex-1 space-y-2 text-sm text-amber-900">
          <p className="font-semibold">Xona PIN qayerdan olinadi?</p>
          <p>
            Kod — o‘quvchining o‘zida (yil bo‘yi bir xil). <b>PIN esa har test sessiyasiga alohida</b> va sessiya yaratilganda avtomatik beriladi:
            {' '}<b>Ichki monitoring → Sessiyalar → sessiyani oching</b> → tepadagi sariq <b>«Xona PIN»</b> kartasi. Test boshida nazoratchi uni doskaga
            yozadi. PIN boshqa xonaga ma’lum bo‘lib qolsa — o‘sha kartada «Yangi PIN».
          </p>
          {recent.length > 0 ? (
            <div className="flex flex-wrap gap-2 pt-1">
              {recent.map((s) => (
                <Link key={s.id} to={`/admin/monitoring/sittings/${s.id}`}
                  className="rounded-lg border border-amber-200 bg-white px-3 py-1.5 hover:border-amber-400">
                  <span className="block text-xs text-slate-500">{formatDate(s.testDate)} · {s.title}</span>
                  <span className="font-mono text-lg font-bold tracking-[0.3em] text-amber-900">{s.roomPin ?? '—'}</span>
                </Link>
              ))}
            </div>
          ) : (
            <p className="text-xs text-amber-800">Hali sessiya yo‘q — sessiya yaratilganda PIN shu yerda ham ko‘rinadi.</p>
          )}
        </div>
      </div>
    </Card>
  )
}
