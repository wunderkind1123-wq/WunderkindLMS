import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, FileText, FileUp, ImagePlus, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  bankApi,
  letter,
  monitoringError,
  type BankApi,
  type BankScope,
  type MonitoringBankDetail,
  type MonitoringQuestion,
  type MonitoringTopicRef,
} from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { control } from './MonitoringBanksPage'
import { WordImportModal } from './WordImportModal'

/** Bitta bank: savollar (2–6 variant, to'g'ri javob, ish reja mavzusi, vazn), Excel'dan import. */
export function MonitoringBankPage({ scope = 'admin' }: { scope?: BankScope }) {
  const { id = '' } = useParams()
  const bapi = useMemo(() => bankApi(scope), [scope])
  const [wordImport, setWordImport] = useState(false)
  const [data, setData] = useState<MonitoringBankDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<MonitoringQuestion | 'new' | null>(null)
  const [importing, setImporting] = useState(false)

  const load = useCallback(() => {
    bapi.getBank(id).then(setData).catch((e) => setError(monitoringError(e, "Bankni olib bo'lmadi")))
  }, [id, bapi])

  useEffect(() => {
    load()
  }, [load])

  async function onImport(file: File | undefined): Promise<void> {
    if (!file) return
    setImporting(true)
    try {
      const r = await bapi.importXlsx(id, file)
      alert(`${r.added} ta savol qo'shildi`)
      load()
    } catch (e) {
      alert(monitoringError(e, "Import bo'lmadi"))
    } finally {
      setImporting(false)
    }
  }

  async function remove(q: MonitoringQuestion): Promise<void> {
    if (!confirm(`${q.order}-savol o'chirilsinmi?`)) return
    try {
      await bapi.deleteQuestion(id, q.id)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!data) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Link to={bapi.listPath} className="rounded-lg p-2 text-slate-500 hover:bg-slate-100" aria-label="Orqaga">
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">{data.bank.title}</h1>
            <p className="text-sm text-slate-400">
              {data.bank.subjectName} · {data.bank.groupName ?? `${data.bank.grade}-sinf`} · {data.questions.length} savol
            </p>
          </div>
        </div>
        <div className="flex gap-2">
          <label className={cn('inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50', importing && 'opacity-50')}>
            <FileUp className="h-4 w-4" /> {importing ? 'Yuklanmoqda…' : 'Excel'}
            <input type="file" accept=".xlsx" className="hidden" disabled={importing} onChange={(e) => { void onImport(e.target.files?.[0]); e.target.value = '' }} />
          </label>
          <Button variant="secondary" onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> Savol
          </Button>
          <Button onClick={() => setWordImport(true)}>
            <FileText className="h-4 w-4" /> Word'dan yuklash
          </Button>
        </div>
      </div>

      {data.topics.length === 0 && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
          Bu fan va sinf uchun ish reja yuklanmagan — savollarni mavzuga bog'lab bo'lmaydi va mavzu tahlili chiqmaydi.
        </p>
      )}

      {data.questions.length === 0 ? (
        <Card className="py-12 text-center text-sm text-slate-500">Savol yo'q. Testingiz Word'da bo'lsa — «Word'dan yuklash»ni bosing.</Card>
      ) : (
        <div className="space-y-2">
          {data.questions.map((q) => (
            <Card key={q.id} className="p-4">
              <div className="flex items-start gap-3">
                <span className="mt-0.5 w-7 shrink-0 text-sm font-semibold tabular-nums text-slate-400">{q.order}.</span>
                <div className="min-w-0 flex-1">
                  <p className="whitespace-pre-wrap text-sm text-slate-800">{q.text}</p>
                  {q.imageUrl && <img src={q.imageUrl} alt="" className="mt-2 max-h-40 rounded-lg" />}
                  <ul className="mt-2 grid gap-1 text-sm sm:grid-cols-2">
                    {q.options.map((o, i) => (
                      <li key={i} className={cn('rounded-md px-2 py-1', i === q.correctIndex ? 'bg-emerald-50 font-medium text-emerald-700' : 'text-slate-600')}>
                        {letter(i)}) {o}
                        {q.optionImages?.[i] && <img src={q.optionImages[i]} alt="" className="mt-1 max-h-24 rounded-md" />}
                      </li>
                    ))}
                  </ul>
                  <p className="mt-2 text-xs text-slate-400">
                    {q.topicTitle ? `Mavzu: ${q.topicTitle}` : 'Mavzu biriktirilmagan'} · vazn {q.weight}
                  </p>
                </div>
                <div className="flex gap-0.5">
                  <button type="button" title="Tahrirlash" onClick={() => setEditing(q)} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button type="button" title="O'chirish" onClick={() => void remove(q)} className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {editing && (
        <QuestionModal
          api={bapi}
          bankId={id}
          question={editing === 'new' ? null : editing}
          topics={data.topics}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}

      {wordImport && (
        <WordImportModal
          api={bapi}
          bankId={id}
          onClose={() => setWordImport(false)}
          onAdded={(n) => {
            setWordImport(false)
            alert(`${n} ta savol qo'shildi`)
            load()
          }}
        />
      )}
    </div>
  )
}

function QuestionModal({
  api,
  bankId,
  question,
  topics,
  onClose,
  onSaved,
}: {
  api: BankApi
  bankId: string
  question: MonitoringQuestion | null
  topics: MonitoringTopicRef[]
  onClose: () => void
  onSaved: () => void
}) {
  const [text, setText] = useState(question?.text ?? '')
  const [options, setOptions] = useState<string[]>(question?.options ?? ['', '', '', ''])
  const [correct, setCorrect] = useState(question?.correctIndex ?? 0)
  const [topicId, setTopicId] = useState(question?.topicId ?? '')
  const [weight, setWeight] = useState(String(question?.weight ?? 1))
  const [imageUrl, setImageUrl] = useState<string | null>(question?.imageUrl ?? null)
  const [optionImages, setOptionImages] = useState<string[]>(() =>
    (question?.options ?? ['', '', '', '']).map((_, i) => question?.optionImages?.[i] ?? ''))
  const [uploading, setUploading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /** Rasmni yuklaydi: option — variant raqami, undefined — savolning o'zi. */
  async function pickImage(file: File | undefined, option?: number): Promise<void> {
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const url = await api.uploadImage(bankId, file)
      if (option === undefined) setImageUrl(url)
      else setOptionImages((imgs) => imgs.map((x, k) => (k === option ? url : x)))
    } catch (e) {
      setError(monitoringError(e, "Rasmni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await api.saveQuestion(bankId, question?.id ?? null, {
        text: text.trim(),
        imageUrl,
        options: options.map((o) => o.trim()),
        optionImages: optionImages.some((x) => x) ? optionImages : null,
        correctIndex: correct,
        topicId: topicId || null,
        weight: Number(weight.replace(',', '.')) || 1,
      })
      onSaved()
    } catch (e) {
      setError(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={question ? `${question.order}-savol` : 'Yangi savol'}
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void save()} disabled={busy || uploading}>{busy ? 'Saqlanmoqda…' : 'Saqlash'}</Button>
        </div>
      }
    >
      <div className="space-y-3 text-sm">
        <label className="block space-y-1">
          <span className="font-medium text-slate-600">Savol</span>
          <textarea className={cn(control, 'min-h-[80px]')} value={text} onChange={(e) => setText(e.target.value)} />
        </label>
        <div className="space-y-2">
          {imageUrl && <img src={imageUrl} alt="Savol rasmi" className="max-h-48 rounded-lg border border-slate-200" />}
          <div className="flex flex-wrap items-center gap-3">
            <label className={cn('inline-flex cursor-pointer items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline', uploading && 'pointer-events-none opacity-60')}>
              <ImagePlus className="h-4 w-4" />
              {uploading ? 'Yuklanmoqda…' : imageUrl ? 'Rasmni almashtirish' : "Rasm qo'shish"}
              <input type="file" accept="image/png,image/jpeg,image/gif,image/webp" className="hidden"
                onChange={(e) => { void pickImage(e.target.files?.[0]); e.target.value = '' }} />
            </label>
            {imageUrl && (
              <button type="button" className="text-sm text-slate-500 hover:text-red-600" onClick={() => setImageUrl(null)}>
                Rasmni olib tashlash
              </button>
            )}
            <span className="text-xs text-slate-400">PNG, JPG, GIF yoki WEBP, 5 MB gacha</span>
          </div>
        </div>
        <div className="space-y-2">
          <span className="font-medium text-slate-600">Variantlar (to'g'risini belgilang)</span>
          {options.map((o, i) => (
            <div key={i} className="flex items-center gap-2">
              <input type="radio" name="correct" checked={correct === i} onChange={() => setCorrect(i)} aria-label={`${letter(i)} to'g'ri`} />
              <span className="w-5 font-semibold text-slate-500">{letter(i)}</span>
              <div className="min-w-0 flex-1">
                <input className={control} value={o} placeholder={optionImages[i] ? 'Matn (ixtiyoriy)' : undefined}
                  onChange={(e) => setOptions(options.map((x, k) => (k === i ? e.target.value : x)))} />
                {optionImages[i] && (
                  <div className="mt-1 flex items-end gap-2">
                    <img src={optionImages[i]} alt={`${letter(i)} variant rasmi`} className="max-h-24 rounded-md border border-slate-200" />
                    <button type="button" className="text-xs text-slate-500 hover:text-red-600"
                      onClick={() => setOptionImages(optionImages.map((x, k) => (k === i ? '' : x)))}>
                      Rasmni olib tashlash
                    </button>
                  </div>
                )}
              </div>
              <label title={optionImages[i] ? 'Rasmni almashtirish' : "Variantga rasm qo'shish"}
                className={cn('cursor-pointer text-slate-400 hover:text-brand-600', uploading && 'pointer-events-none opacity-50')}>
                <ImagePlus className="h-4 w-4" />
                <input type="file" accept="image/png,image/jpeg,image/gif,image/webp" className="hidden"
                  onChange={(e) => { void pickImage(e.target.files?.[0], i); e.target.value = '' }} />
              </label>
              {options.length > 2 && (
                <button type="button" className="text-slate-400 hover:text-red-600" aria-label="Variantni olib tashlash"
                  onClick={() => {
                    setOptions(options.filter((_, k) => k !== i))
                    setOptionImages(optionImages.filter((_, k) => k !== i))
                    if (correct >= i && correct > 0) setCorrect(correct - 1)
                  }}>
                  <Trash2 className="h-4 w-4" />
                </button>
              )}
            </div>
          ))}
          {options.length < 6 && (
            <button type="button" className="text-sm font-medium text-brand-600 hover:underline" onClick={() => { setOptions([...options, '']); setOptionImages([...optionImages, '']) }}>
              + Variant
            </button>
          )}
        </div>
        <div className="grid gap-3 sm:grid-cols-[1fr_8rem]">
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Ish reja mavzusi</span>
            <select className={control} value={topicId} onChange={(e) => setTopicId(e.target.value)} disabled={topics.length === 0}>
              <option value="">— biriktirilmagan —</option>
              {topics.map((t) => <option key={t.id} value={t.id}>{t.order}. {t.title}</option>)}
            </select>
          </label>
          <label className="block space-y-1">
            <span className="font-medium text-slate-600">Vazn</span>
            <input className={control} inputMode="decimal" value={weight} onChange={(e) => setWeight(e.target.value)} />
          </label>
        </div>
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
