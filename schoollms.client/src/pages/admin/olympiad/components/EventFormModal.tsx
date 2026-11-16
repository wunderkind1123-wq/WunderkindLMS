import { useEffect, useState } from 'react'
import { FileText, Trash2, Upload } from 'lucide-react'
import {
  createEvent,
  fromLocalInput,
  getSettings,
  toLocalInput,
  updateEvent,
  uploadMedia,
  type OlympiadEvent,
  type OlympiadEventInput,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { RichTextField } from '@/components/ui/RichTextField'
import { ErrorBox, Switch } from './common'
import { errText } from './lib'

const today = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10)

function blank(): OlympiadEventInput {
  return {
    title: '',
    description: '',
    date: today(),
    startTime: '10:00:00',
    endTime: '12:00:00',
    location: '',
    locationMapUrl: '',
    capacity: null,
    onFull: 'waitlist',
    registrationOpensAt: null,
    registrationClosesAt: null,
    maxScore: 100,
    showScore: true,
    showRank: true,
    showCertificate: true,
    showInvite: true,
    rulesText: '',
    rulesFileUrl: '',
    rulesFileName: '',
    rulesDisplay: 'text',
  }
}

function fromEvent(e: OlympiadEvent): OlympiadEventInput {
  const b = blank()
  return Object.fromEntries(Object.keys(b).map((k) => [k, e[k as keyof OlympiadEventInput]])) as unknown as OlympiadEventInput
}

/** Tadbir yaratish/tahrirlash. Yangisida "limit to'lsa" va natija qismlari sozlamadagi standartdan olinadi. */
export function EventFormModal({
  event,
  onClose,
  onSaved,
}: {
  event: OlympiadEvent | null
  onClose: () => void
  onSaved: (e: OlympiadEvent) => void
}) {
  const [f, setF] = useState<OlympiadEventInput>(() => (event ? fromEvent(event) : blank()))
  const [ownRules, setOwnRules] = useState(!!event && (event.rulesText !== '' || event.rulesFileUrl !== ''))
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (event) return
    getSettings()
      .then((s) =>
        setF((p) => ({
          ...p,
          onFull: s.defaultOnFull,
          showScore: s.resultShowScore,
          showRank: s.resultShowRank,
          showCertificate: s.resultShowCertificate,
          showInvite: s.resultShowInvite,
        })),
      )
      .catch(() => {})
  }, [event])

  const set = <K extends keyof OlympiadEventInput>(k: K, v: OlympiadEventInput[K]) => setF((p) => ({ ...p, [k]: v }))

  const save = async () => {
    setBusy(true)
    setError('')
    const body: OlympiadEventInput = ownRules ? f : { ...f, rulesText: '', rulesFileUrl: '', rulesFileName: '', rulesDisplay: 'text' }
    try {
      onSaved(event ? await updateEvent(event.id, body) : await createEvent(body))
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const uploadRules = async (file: File | undefined) => {
    if (!file) return
    setUploading(true)
    try {
      const a = await uploadMedia(file, 'document')
      setF((p) => ({ ...p, rulesFileUrl: a.url, rulesFileName: a.name, rulesDisplay: p.rulesDisplay === 'text' ? 'both' : p.rulesDisplay }))
    } catch (e) {
      setError(errText(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={event ? 'Tadbirni tahrirlash' : 'Yangi olimpiada'}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button disabled={busy || !f.title.trim()} onClick={save}>
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <ErrorBox message={error} />
        <div className="space-y-3">
          <Input label="Nomi" required value={f.title} onChange={(e) => set('title', e.target.value)} placeholder="Masalan: Matematika olimpiadasi (4–6-sinf)" />
          <RichTextField label="Tavsif (botda tadbir kartasida)" value={f.description} onChange={(v) => set('description', v)} rows={3} />
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <Input label="Sana" type="date" required value={f.date} onChange={(e) => set('date', e.target.value)} />
          <Input label="Boshlanishi" type="time" required value={f.startTime.slice(0, 5)} onChange={(e) => set('startTime', e.target.value + ':00')} />
          <Input label="Tugashi" type="time" required value={f.endTime.slice(0, 5)} onChange={(e) => set('endTime', e.target.value + ':00')} />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Manzil" value={f.location} onChange={(e) => set('location', e.target.value)} placeholder="Wunderkind maktabi, 2-qavat" />
          <Input label="Xarita havolasi (ixtiyoriy)" value={f.locationMapUrl} onChange={(e) => set('locationMapUrl', e.target.value)} placeholder="https://maps.google.com/…" />
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <Input
            label="Limit (bo'sh — limitsiz)"
            type="number"
            min={1}
            value={f.capacity ?? ''}
            onChange={(e) => set('capacity', e.target.value === '' ? null : Number(e.target.value))}
          />
          <Select label="Limit to'lsa" value={f.onFull} onChange={(e) => set('onFull', e.target.value as OlympiadEventInput['onFull'])}>
            <option value="waitlist">Kutish ro'yxatiga</option>
            <option value="close">Ro'yxatni yopish</option>
          </Select>
          <Input label="Maksimal ball" type="number" min={1} required value={f.maxScore} onChange={(e) => set('maxScore', Number(e.target.value))} />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Ro'yxat ochiladi (bo'sh — darhol)"
            type="datetime-local"
            value={toLocalInput(f.registrationOpensAt)}
            onChange={(e) => set('registrationOpensAt', fromLocalInput(e.target.value))}
          />
          <Input
            label="Ro'yxat yopiladi (bo'sh — tadbir boshlanguncha)"
            type="datetime-local"
            value={toLocalInput(f.registrationClosesAt)}
            onChange={(e) => set('registrationClosesAt', fromLocalInput(e.target.value))}
          />
        </div>

        <div className="rounded-xl border border-slate-100 p-3">
          <p className="mb-1 text-sm font-medium text-slate-600">Natija xabarida</p>
          <Switch label="Ball va foiz" checked={f.showScore} onChange={(v) => set('showScore', v)} />
          <Switch label="O'rin" checked={f.showRank} onChange={(v) => set('showRank', v)} />
          <Switch label="Sertifikat" checked={f.showCertificate} onChange={(v) => set('showCertificate', v)} />
          <Switch label="Maktabga taklif tugmasi" hint="Matn va havola — Sozlamalar → Natija" checked={f.showInvite} onChange={(v) => set('showInvite', v)} />
        </div>

        <div className="rounded-xl border border-slate-100 p-3">
          <Switch
            label="Tadbirning o'z nizomi"
            hint="O'chiq bo'lsa — Sozlamalardagi umumiy nizom ko'rsatiladi"
            checked={ownRules}
            onChange={setOwnRules}
          />
          {ownRules && (
            <div className="mt-2 space-y-3">
              <RichTextField label="Nizom matni" value={f.rulesText} onChange={(v) => set('rulesText', v)} rows={5} />
              <div className="flex flex-wrap items-center gap-2">
                {f.rulesFileUrl ? (
                  <span className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm">
                    <FileText className="h-4 w-4 text-slate-400" />
                    <a href={f.rulesFileUrl} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                      {f.rulesFileName || 'Nizom.pdf'}
                    </a>
                    <button type="button" onClick={() => setF((p) => ({ ...p, rulesFileUrl: '', rulesFileName: '' }))} className="text-slate-400 hover:text-red-600">
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </span>
                ) : (
                  <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50">
                    <Upload className="h-4 w-4" /> {uploading ? 'Yuklanmoqda…' : 'PDF yuklash'}
                    <input type="file" accept="application/pdf" className="hidden" onChange={(e) => uploadRules(e.target.files?.[0])} />
                  </label>
                )}
                <Select value={f.rulesDisplay} onChange={(e) => set('rulesDisplay', e.target.value as OlympiadEventInput['rulesDisplay'])} className="w-auto">
                  <option value="text">Faqat matn</option>
                  <option value="pdf">Faqat PDF</option>
                  <option value="both">Matn va PDF</option>
                </Select>
              </div>
              {event && <p className="text-xs text-slate-400">Joriy versiya: {event.rulesVersion}. O'zgarsa versiya oshadi; avval tasdiqlaganlardan qayta so'ralmaydi.</p>}
            </div>
          )}
        </div>
      </div>
    </Modal>
  )
}
