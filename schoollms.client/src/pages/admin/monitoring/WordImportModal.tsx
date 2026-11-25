import { useState } from 'react'
import { AlertTriangle, Download, FileText, Trash2, Upload } from 'lucide-react'
import { downloadFile } from '@/api/services/curriculum'
import { letter, monitoringError, type BankApi, type MonitoringImportPreview, type MonitoringQuestionPayload } from '@/api/services/monitoring'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { control } from './MonitoringBanksPage'

const SAMPLE = `1. 2 + 3 nechaga teng?
A) 4
*B) 5
C) 6
D) 7

2. O'zbekiston poytaxti?
A) Samarqand
B) Buxoro
C) Toshkent
D) Xiva
Javob: C`

/**
 * Ustozning Word testini bankka yuklash (curriculum-monitoring.md §2.2): fayl yoki matn → server
 * savollarga ajratadi (hech narsa saqlamaydi) → ustoz ko'radi, to'g'ri javobni tuzatadi, keraksizini
 * olib tashlaydi → «Qo'shish» bankning oxiriga yozadi.
 */
export function WordImportModal({
  api,
  bankId,
  onClose,
  onAdded,
}: {
  api: BankApi
  bankId: string
  onClose: () => void
  onAdded: (count: number) => void
}) {
  const [mode, setMode] = useState<'file' | 'text'>('file')
  const [text, setText] = useState('')
  const [preview, setPreview] = useState<MonitoringImportPreview | null>(null)
  const [rows, setRows] = useState<MonitoringQuestionPayload[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function read(input: { file?: File; text?: string }): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const p = await api.previewImport(bankId, input)
      setPreview(p)
      setRows(p.questions)
    } catch (e) {
      setError(monitoringError(e, "Faylni o'qib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const { added } = await api.addQuestions(bankId, rows)
      onAdded(added)
    } catch (e) {
      setError(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const footer = preview ? (
    <div className="flex w-full flex-wrap items-center justify-between gap-2">
      <Button variant="secondary" onClick={() => { setPreview(null); setRows([]) }} disabled={busy}>Boshqa fayl</Button>
      <Button onClick={() => void save()} disabled={busy || rows.length === 0}>
        {busy ? 'Saqlanmoqda…' : `${rows.length} ta savolni bankka qo'shish`}
      </Button>
    </div>
  ) : (
    <div className="flex w-full justify-end gap-2">
      <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
      {mode === 'text' && (
        <Button onClick={() => void read({ text })} disabled={busy || text.trim().length === 0}>
          {busy ? "O'qilmoqda…" : 'Tekshirish'}
        </Button>
      )}
    </div>
  )

  return (
    <Modal open onClose={onClose} size="xl" title="Word fayldan savollar yuklash" footer={footer}>
      {!preview ? (
        <div className="space-y-4 text-sm">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(['file', 'text'] as const).map((m) => (
              <button key={m} type="button" onClick={() => setMode(m)}
                className={cn('flex-1 rounded-md px-3 py-1.5 font-medium', mode === m ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500')}>
                {m === 'file' ? 'Word fayl' : 'Matnni qo’yish'}
              </button>
            ))}
          </div>

          {mode === 'file' ? (
            <label className={cn('flex cursor-pointer flex-col items-center gap-2 rounded-xl border-2 border-dashed border-slate-200 px-4 py-10 text-center hover:border-brand-300 hover:bg-brand-50/30', busy && 'pointer-events-none opacity-50')}>
              <Upload className="h-7 w-7 text-slate-400" />
              <span className="font-medium text-slate-700">{busy ? "O'qilmoqda…" : 'Word (.docx) faylni tanlang'}</span>
              <span className="text-xs text-slate-400">yoki .txt · 10 MB gacha · bir faylda 200 tagacha savol</span>
              <input type="file" accept=".docx,.txt" className="hidden" disabled={busy}
                onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) void read({ file: f }) }} />
            </label>
          ) : (
            <textarea className={cn(control, 'min-h-[220px] font-mono text-xs')} value={text} placeholder={SAMPLE}
              onChange={(e) => setText(e.target.value)} />
          )}

          <div className="rounded-lg bg-slate-50 p-3 text-xs text-slate-600">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <p className="flex items-center gap-1.5 font-medium text-slate-700"><FileText className="h-3.5 w-3.5" /> Faylda testni qanday yozish kerak</p>
              <Button variant="secondary" onClick={() => void downloadFile(api.wordTemplatePath, 'test-savollari-namuna.docx')}>
                <Download className="h-4 w-4" /> Word namunasini yuklab olish
              </Button>
            </div>
            <ul className="list-disc space-y-0.5 pl-4">
              <li>Savol raqam bilan: <code>1.</code> yoki <code>1)</code>; variantlar harf bilan: <code>A)</code> <code>B)</code> … (2–6 ta). Word'ning avtomatik raqamlangan ro'yxati ham bo'ladi.</li>
              <li>To'g'ri javob: variant oldiga <code>*</code> (<code>*B) 5</code>), yoki variantlardan keyin <code>Javob: B</code> qatori, yoki variantni <b>qalin</b> qiling.</li>
              <li>Formulalar (Word'ning «Уравнение / Equation») matnga aylanadi: <code>(a+b)/(c)</code>, <code>x^(2)</code>, <code>√(x)</code>. Eski «Equation 3.0 / MathType» o'qilmaydi.</li>
              <li>Savoldagi rasm (chizma, grafik) savolga biriktiriladi — bir savolga bitta rasm. Variant rasm bo‘lsa: «A)» dan keyin shu qatorga rasmni qo‘ying.</li>
            </ul>
          </div>
          {error && <p className="whitespace-pre-line text-red-600">{error}</p>}
        </div>
      ) : (
        <div className="space-y-3 text-sm">
          <p className="text-slate-600">
            <b>{rows.length}</b> ta savol tayyor. Tekshiring: to'g'ri javob yashil — boshqa variantni bossangiz o'shanga o'zgaradi.
          </p>
          {preview.problems.length > 0 && (
            <div className="rounded-lg bg-amber-50 p-3 text-amber-800">
              <p className="mb-1 flex items-center gap-1.5 font-medium"><AlertTriangle className="h-4 w-4" /> {preview.problems.length} ta savol olinmadi — Word faylda tuzatib qayta yuklang yoki qo'lda qo'shing:</p>
              <ul className="list-disc space-y-0.5 pl-5 text-xs">
                {preview.problems.map((p, i) => <li key={i}>{p}</li>)}
              </ul>
            </div>
          )}
          <div className="max-h-[55vh] space-y-2 overflow-auto pr-1">
            {rows.map((q, qi) => (
              <div key={qi} className="rounded-lg border border-slate-200 p-3">
                <div className="flex items-start gap-2">
                  <span className="w-6 shrink-0 font-semibold tabular-nums text-slate-400">{qi + 1}.</span>
                  <div className="min-w-0 flex-1">
                    <p className="whitespace-pre-wrap text-slate-800">{q.text}</p>
                    {q.imageUrl && <img src={q.imageUrl} alt="" className="mt-2 max-h-40 rounded-lg border border-slate-100" />}
                    <div className="mt-2 grid gap-1 sm:grid-cols-2">
                      {q.options.map((o, oi) => (
                        <button key={oi} type="button" title="To'g'ri javob qilish"
                          onClick={() => setRows(rows.map((r, k) => (k === qi ? { ...r, correctIndex: oi } : r)))}
                          className={cn('rounded-md px-2 py-1 text-left', oi === q.correctIndex ? 'bg-emerald-50 font-medium text-emerald-700' : 'text-slate-600 hover:bg-slate-50')}>
                          {letter(oi)}) {o}
                          {q.optionImages?.[oi] && <img src={q.optionImages[oi]} alt="" className="mt-1 max-h-24 rounded-md" />}
                        </button>
                      ))}
                    </div>
                  </div>
                  <button type="button" title="Olib tashlash" onClick={() => setRows(rows.filter((_, k) => k !== qi))}
                    className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
          {error && <p className="whitespace-pre-line text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
