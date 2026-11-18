import { useRef, useState, type ReactNode } from 'react'
import { FileText, Film, Image as ImageIcon, Paperclip, Send, Trash2, Video } from 'lucide-react'
import {
  REG_STATUS,
  REG_STATUS_TONE,
  testMessage,
  uploadMedia,
  type AttachmentKind,
  type OlympiadAttachment,
  type RegStatus,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { errText } from './lib'

export function ErrorBox({ message }: { message: string }) {
  if (!message) return null
  return <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{message}</p>
}

export function Badge({ className, children }: { className?: string; children: ReactNode }) {
  return <span className={cn('inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium', className)}>{children}</span>
}

export function RegBadge({ status }: { status: RegStatus }) {
  return <Badge className={REG_STATUS_TONE[status]}>{REG_STATUS[status]}</Badge>
}

/** iOS uslubidagi kalit. */
export function Switch({ checked, onChange, label, hint }: { checked: boolean; onChange: (v: boolean) => void; label: string; hint?: string }) {
  return (
    <label className="flex cursor-pointer items-start justify-between gap-3 py-1.5">
      <span>
        <span className="block text-sm text-slate-700">{label}</span>
        {hint && <span className="block text-xs text-slate-400">{hint}</span>}
      </span>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative mt-0.5 h-6 w-10 shrink-0 rounded-full transition-colors',
          checked ? 'bg-brand-600' : 'bg-slate-200',
        )}
      >
        <span
          className={cn(
            'absolute left-0 top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
            checked ? 'translate-x-[18px]' : 'translate-x-0.5',
          )}
        />
      </button>
    </label>
  )
}

/** Bo'lim sarlavhasi sozlamalar/kartochkalar ichida. */
export function SectionTitle({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
      <div>
        <h2 className="text-base font-semibold text-slate-800">{title}</h2>
        {hint && <p className="text-sm text-slate-400">{hint}</p>}
      </div>
      {action}
    </div>
  )
}

/** Tasdiqlash oynasi. */
export function Confirm({
  open,
  title,
  children,
  confirmLabel = 'Tasdiqlash',
  danger,
  busy,
  onConfirm,
  onClose,
}: {
  open: boolean
  title: string
  children: ReactNode
  confirmLabel?: string
  danger?: boolean
  busy?: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button variant={danger ? 'danger' : 'primary'} disabled={busy} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="text-sm text-slate-600">{children}</div>
    </Modal>
  )
}

const KIND_ICON: Record<AttachmentKind, typeof ImageIcon> = {
  photo: ImageIcon,
  document: FileText,
  video: Film,
  video_note: Video,
}

const KIND_LABEL: Record<AttachmentKind, string> = {
  photo: 'Rasm',
  document: 'Fayl',
  video: 'Video',
  video_note: 'Dumaloq video',
}

/**
 * Biriktirmalar: rasm (bir nechta — albom), fayl, video, dumaloq video. Fayl serverga darrov yuklanadi
 * (`/api/admin/olympiad/media`), ro'yxatda faqat havolasi saqlanadi.
 */
export function AttachmentsEditor({
  value,
  onChange,
  max = 10,
  allowVideoNote = true,
}: {
  value: OlympiadAttachment[]
  onChange: (next: OlympiadAttachment[]) => void
  max?: number
  allowVideoNote?: boolean
}) {
  const input = useRef<HTMLInputElement>(null)
  const [mode, setMode] = useState<'auto' | 'video_note' | 'document'>('auto')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const pick = (m: typeof mode, accept: string) => {
    setMode(m)
    if (input.current) {
      input.current.accept = accept
      input.current.click()
    }
  }

  const onFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    setBusy(true)
    setError('')
    const added: OlympiadAttachment[] = []
    try {
      for (const f of Array.from(files).slice(0, max - value.length)) {
        added.push(await uploadMedia(f, mode === 'auto' ? undefined : mode))
      }
      onChange([...value, ...added])
    } catch (e) {
      if (added.length) onChange([...value, ...added])
      setError(errText(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setBusy(false)
      if (input.current) input.current.value = ''
    }
  }

  const hasNote = value.some((a) => a.kind === 'video_note')

  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">Biriktirmalar</span>
      {value.length > 0 && (
        <ul className="mb-2 space-y-1.5">
          {value.map((a, i) => {
            const Icon = KIND_ICON[a.kind]
            return (
              <li key={a.url} className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm">
                <Icon className="h-4 w-4 shrink-0 text-slate-400" />
                {a.kind === 'photo' ? (
                  <img src={a.url} alt="" className="h-8 w-8 rounded object-cover" />
                ) : null}
                <span className="min-w-0 flex-1 truncate text-slate-700">{a.name || a.url}</span>
                <span className="text-xs text-slate-400">{KIND_LABEL[a.kind]}</span>
                <button
                  type="button"
                  className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600"
                  onClick={() => onChange(value.filter((_, j) => j !== i))}
                  aria-label="O'chirish"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {value.length < max && (
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="secondary" disabled={busy} onClick={() => pick('auto', 'image/*')}>
            <ImageIcon className="h-4 w-4" /> Rasm
          </Button>
          <Button type="button" variant="secondary" disabled={busy} onClick={() => pick('auto', 'video/mp4,video/quicktime')}>
            <Film className="h-4 w-4" /> Video
          </Button>
          <Button type="button" variant="secondary" disabled={busy} onClick={() => pick('document', '')}>
            <Paperclip className="h-4 w-4" /> Fayl
          </Button>
          {allowVideoNote && (
            <Button type="button" variant="secondary" disabled={busy} onClick={() => pick('video_note', 'video/mp4')}>
              <Video className="h-4 w-4" /> Dumaloq video
            </Button>
          )}
          {busy && <span className="self-center text-xs text-slate-400">Yuklanmoqda…</span>}
        </div>
      )}
      <input ref={input} type="file" multiple className="hidden" onChange={(e) => onFiles(e.target.files)} />
      <p className="mt-1 text-xs text-slate-400">Rasm ≤ 10 MB, video va fayl ≤ 50 MB. Bir nechta rasm — albom bo'lib boradi.</p>
      {hasNote && (
        <p className="mt-1 text-xs text-amber-600">
          Dumaloq video: kvadrat MP4, 1 daqiqagacha. Telegram dumaloq videoga matn qo'shishga ruxsat bermaydi — matn undan
          keyin alohida xabar bo'lib boradi.
        </p>
      )}
      <ErrorBox message={error} />
    </div>
  )
}

/** "Menga test yuborish" — joriy xodimning Telegramiga. */
export function TestSendButton({
  text,
  attachments = [],
  buttons = [],
  onDone,
  onError,
}: {
  text: string
  attachments?: OlympiadAttachment[]
  buttons?: { text: string; url: string }[]
  onDone: (msg: string) => void
  onError: (msg: string) => void
}) {
  const [busy, setBusy] = useState(false)
  return (
    <Button
      type="button"
      variant="secondary"
      disabled={busy || (!text.trim() && attachments.length === 0)}
      onClick={async () => {
        setBusy(true)
        try {
          await testMessage(text, attachments, buttons)
          onDone('Telegramingizga yuborildi')
        } catch (e) {
          onError(errText(e, "Yuborib bo'lmadi"))
        } finally {
          setBusy(false)
        }
      }}
    >
      <Send className="h-4 w-4" /> Menga test yuborish
    </Button>
  )
}
