import { useCallback, useEffect, useMemo, useState } from 'react'
import { Ban, Clock, Pencil, Plus, Send, Trash2, Users } from 'lucide-react'
import {
  BROADCAST_STATUS,
  REG_STATUS,
  audienceCount,
  cancelBroadcast,
  createBroadcast,
  deleteBroadcast,
  fmtDateTime,
  fromLocalInput,
  getBroadcasts,
  getDeliveries,
  getEvents,
  getSchools,
  sendBroadcast,
  updateBroadcast,
  type Audience,
  type BroadcastInput,
  type BroadcastStatus,
  type DeliveryRow,
  type OlympiadBroadcast,
  type OlympiadButton,
  type OlympiadEvent,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { RichText } from '@/components/ui/RichText'
import { RichTextField } from '@/components/ui/RichTextField'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { AttachmentsEditor, Badge, ErrorBox, TestSendButton } from './components/common'
import { errText, visibleLength } from './components/lib'

const TONE: Record<BroadcastStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  scheduled: 'bg-amber-50 text-amber-700',
  sending: 'bg-brand-50 text-brand-700',
  done: 'bg-emerald-50 text-emerald-700',
  cancelled: 'bg-slate-100 text-slate-400',
}

const GRADES = Array.from({ length: 11 }, (_, i) => i + 1)

/** Bir nechta tanlash — chip ko'rinishida. */
function Chips<T extends string | number>({
  options,
  value,
  onChange,
  label,
}: {
  options: { value: T; label: string }[]
  value: T[]
  onChange: (v: T[]) => void
  label: string
}) {
  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>
      <div className="flex flex-wrap gap-1.5">
        {options.map((o) => {
          const on = value.includes(o.value)
          return (
            <button
              type="button"
              key={String(o.value)}
              onClick={() => onChange(on ? value.filter((x) => x !== o.value) : [...value, o.value])}
              className={cn(
                'rounded-full border px-3 py-1 text-xs transition-colors',
                on ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
              )}
            >
              {o.label}
            </button>
          )
        })}
      </div>
    </div>
  )
}

function ButtonsEditor({ value, onChange }: { value: OlympiadButton[]; onChange: (v: OlympiadButton[]) => void }) {
  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">Tugmalar (havola)</span>
      <div className="space-y-2">
        {value.map((b, i) => (
          <div key={i} className="flex gap-2">
            <Input value={b.text} placeholder="Tugma matni" onChange={(e) => onChange(value.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)))} />
            <Input value={b.url} placeholder="https://…" onChange={(e) => onChange(value.map((x, j) => (j === i ? { ...x, url: e.target.value } : x)))} />
            <button type="button" className="rounded p-2 text-slate-400 hover:bg-red-50 hover:text-red-600" onClick={() => onChange(value.filter((_, j) => j !== i))}>
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        <Button type="button" variant="secondary" onClick={() => onChange([...value, { text: '', url: '' }])}>
          <Plus className="h-4 w-4" /> Tugma qo'shish
        </Button>
      </div>
    </div>
  )
}

function EditorModal({
  broadcast,
  events,
  schools,
  onClose,
  onSaved,
}: {
  broadcast: OlympiadBroadcast | null
  events: OlympiadEvent[]
  schools: string[]
  onClose: () => void
  onSaved: (msg: string) => void
}) {
  const [f, setF] = useState<BroadcastInput>(
    broadcast
      ? { title: broadcast.title, text: broadcast.text, attachments: broadcast.attachments, buttons: broadcast.buttons, audience: broadcast.audience }
      : { title: '', text: '', attachments: [], buttons: [], audience: {} },
  )
  const [count, setCount] = useState<number | null>(null)
  const [when, setWhen] = useState<'now' | 'later'>(broadcast?.scheduledAt ? 'later' : 'now')
  const [at, setAt] = useState(broadcast?.scheduledAt?.slice(0, 16) ?? '')
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [toast, setToast] = useState<string | null>(null)

  const a = f.audience
  const setA = (patch: Partial<Audience>) => setF((p) => ({ ...p, audience: { ...p.audience, ...patch } }))
  const audienceKey = JSON.stringify(a)

  useEffect(() => {
    const t = setTimeout(() => {
      audienceCount(JSON.parse(audienceKey) as Audience)
        .then(setCount)
        .catch(() => setCount(null))
    }, 300)
    return () => clearTimeout(t)
  }, [audienceKey])

  const len = visibleLength(f.text)
  const hasMedia = f.attachments.length > 0
  const captionWarn = hasMedia && len > 1024
  const hasNote = f.attachments.some((x) => x.kind === 'video_note')
  const cleanButtons = f.buttons.filter((b) => b.text.trim() || b.url.trim())

  const persist = async (): Promise<OlympiadBroadcast> => {
    const body = { ...f, buttons: cleanButtons }
    return broadcast ? updateBroadcast(broadcast.id, body) : createBroadcast(body)
  }

  const saveDraft = async () => {
    setBusy(true)
    setError('')
    try {
      await persist()
      onSaved('Qoralama saqlandi')
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const send = async () => {
    setConfirm(false)
    setBusy(true)
    setError('')
    try {
      const b = await persist()
      const r = await sendBroadcast(b.id, when === 'later' ? fromLocalInput(at) : null)
      onSaved(r.status === 'scheduled' ? `Rejalashtirildi: ${fmtDateTime(r.scheduledAt)}` : `Yuborilmoqda: ${r.total} ta oluvchi`)
    } catch (e) {
      setError(errText(e, "Yuborib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={broadcast ? 'Rassilkani tahrirlash' : 'Yangi rassilka'}
      size="xl"
      footer={
        <>
          <TestSendButton text={f.text} attachments={f.attachments} buttons={cleanButtons} onDone={setToast} onError={setError} />
          <Button variant="secondary" disabled={busy} onClick={saveDraft}>
            Qoralama
          </Button>
          <Button disabled={busy || (!f.text.trim() && !hasMedia) || (when === 'later' && !at)} onClick={() => setConfirm(true)}>
            {when === 'later' ? <Clock className="h-4 w-4" /> : <Send className="h-4 w-4" />}
            {when === 'later' ? 'Rejalashtirish' : 'Yuborish'}
          </Button>
        </>
      }
    >
      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <div className="space-y-4">
          <ErrorBox message={error} />
          <Input label="Nomi (faqat ro'yxat uchun)" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder="Bo'sh — matnning birinchi qatori" />
          <div>
            <RichTextField label="Matn" value={f.text} onChange={(text) => setF({ ...f, text })} rows={8} />
            <p className={cn('mt-1 text-xs', captionWarn || len > 4096 ? 'text-amber-600' : 'text-slate-400')}>
              {len} belgi.{' '}
              {captionWarn
                ? "Rasm/fayl ostidagi matn 1024 belgigacha — matn alohida xabar bo'lib keyin boradi."
                : len > 4096
                  ? "4096 dan uzun — bir nechta xabarga bo'linadi."
                  : '{ism} — oluvchining Telegram ismi.'}
            </p>
          </div>
          <AttachmentsEditor value={f.attachments} onChange={(attachments) => setF({ ...f, attachments })} />
          <ButtonsEditor value={f.buttons} onChange={(buttons) => setF({ ...f, buttons })} />
          {hasNote && (f.text.trim() || cleanButtons.length > 0) && (
            <p className="text-xs text-amber-600">Dumaloq videodan keyin matn va tugmalar alohida xabar bo'lib boradi.</p>
          )}
        </div>

        <div className="space-y-4">
          <div className="rounded-2xl bg-slate-50 p-4">
            <p className="flex items-center gap-2 text-sm font-semibold text-slate-700">
              <Users className="h-4 w-4" /> Oluvchilar: {count ?? '…'}
            </p>
            <p className="text-xs text-slate-400">Filtr bo'sh — botning hamma foydalanuvchilari. Bir akkauntga bir marta boradi.</p>
          </div>
          <Select
            label="Tadbir"
            value={a.eventIds?.[0] ?? ''}
            onChange={(e) => setA({ eventIds: e.target.value ? [e.target.value] : null })}
          >
            <option value="">Hamma tadbirlar</option>
            {events.map((e) => (
              <option key={e.id} value={e.id}>
                {e.title}
              </option>
            ))}
          </Select>
          <Chips
            label="Holat"
            options={Object.entries(REG_STATUS)
              .filter(([k]) => k !== 'cancelled')
              .map(([value, label]) => ({ value, label }))}
            value={a.statuses ?? []}
            onChange={(v) => setA({ statuses: v.length ? v : null })}
          />
          <Chips label="Sinf" options={GRADES.map((g) => ({ value: g, label: String(g) }))} value={a.grades ?? []} onChange={(v) => setA({ grades: v.length ? v : null })} />
          {schools.length > 0 && (
            <Chips label="Maktab" options={schools.map((s) => ({ value: s, label: s }))} value={a.schools ?? []} onChange={(v) => setA({ schools: v.length ? v : null })} />
          )}
          <Select label="Lid" value={a.lead ?? 'any'} onChange={(e) => setA({ lead: e.target.value === 'any' ? null : (e.target.value as Audience['lead']) })}>
            <option value="any">Farqi yo'q</option>
            <option value="not">Lidga aylantirilmaganlar</option>
            <option value="converted">Lidga aylantirilganlar</option>
          </Select>
          <div className="space-y-2 rounded-2xl border border-slate-100 p-3">
            <span className="block text-sm font-medium text-slate-600">Qachon</span>
            <div className="inline-flex rounded-lg bg-slate-100 p-1 text-sm">
              {(['now', 'later'] as const).map((m) => (
                <button
                  type="button"
                  key={m}
                  onClick={() => setWhen(m)}
                  className={'rounded-md px-3 py-1.5 ' + (when === m ? 'bg-white font-medium text-slate-800 shadow-sm' : 'text-slate-500')}
                >
                  {m === 'now' ? 'Darhol' : 'Rejalashtirish'}
                </button>
              ))}
            </div>
            {when === 'later' && <Input type="datetime-local" value={at} onChange={(e) => setAt(e.target.value)} />}
          </div>
        </div>
      </div>

      <Modal
        open={confirm}
        onClose={() => setConfirm(false)}
        title="Rassilkani tasdiqlang"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirm(false)}>
              Bekor
            </Button>
            <Button disabled={busy} onClick={send}>
              {when === 'later' ? 'Rejalashtirish' : 'Yuborish'}
            </Button>
          </>
        }
      >
        <div className="space-y-3 text-sm text-slate-600">
          <p>
            {count ?? '…'} ta oluvchiga {when === 'later' ? `${fmtDateTime(fromLocalInput(at))} da` : 'hozir'} yuboriladi.
          </p>
          <div className="max-h-60 overflow-y-auto rounded-xl bg-sky-50 p-3 text-slate-800">
            <RichText markup={f.text} />
          </div>
        </div>
      </Modal>
      <Toast message={toast} onClose={() => setToast(null)} />
    </Modal>
  )
}

function DeliveriesModal({ b, onClose }: { b: OlympiadBroadcast; onClose: () => void }) {
  const [status, setStatus] = useState('failed')
  const [rows, setRows] = useState<DeliveryRow[] | null>(null)
  useEffect(() => {
    getDeliveries(b.id, status)
      .then(setRows)
      .catch(() => setRows([]))
  }, [b.id, status])
  return (
    <Modal open onClose={onClose} title={`Hisobot: ${b.title}`}>
      <div className="space-y-3">
        <Select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="failed">Xato ({b.failed})</option>
          <option value="blocked">Botni bloklagan ({b.blocked})</option>
          <option value="sent">Yuborildi ({b.sent})</option>
          <option value="pending">Navbatda ({b.pending})</option>
        </Select>
        {rows === null ? (
          <Loader />
        ) : rows.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Bo'sh</p>
        ) : (
          <ul className="max-h-96 divide-y divide-slate-100 overflow-y-auto text-sm">
            {rows.map((r, i) => (
              <li key={i} className="py-2">
                <span className="font-medium text-slate-800">
                  {r.firstName} {r.lastName}
                </span>
                {r.username && <span className="ml-1 text-slate-400">@{r.username}</span>}
                {r.error && <span className="block text-xs text-red-600">{r.error}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  )
}

/** Olimpiada → Rassilka (§14). */
export default function BroadcastsPage() {
  const [list, setList] = useState<OlympiadBroadcast[] | null>(null)
  const [events, setEvents] = useState<OlympiadEvent[]>([])
  const [schools, setSchools] = useState<string[]>([])
  const [editing, setEditing] = useState<OlympiadBroadcast | 'new' | null>(null)
  const [report, setReport] = useState<OlympiadBroadcast | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    getBroadcasts()
      .then(setList)
      .catch((e) => {
        setList([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [])
  useEffect(load, [load])
  useEffect(() => {
    getEvents().then(setEvents).catch(() => {})
    getSchools().then(setSchools).catch(() => {})
  }, [])

  // Yuborilayotgan bo'lsa — jarayon foizi real vaqtda.
  const sending = useMemo(() => (list ?? []).some((b) => b.status === 'sending'), [list])
  useEffect(() => {
    if (!sending) return
    const t = setInterval(load, 3000)
    return () => clearInterval(t)
  }, [sending, load])

  const act = async (fn: () => Promise<unknown>, done: string) => {
    setError('')
    try {
      await fn()
      setToast(done)
      load()
    } catch (e) {
      setError(errText(e))
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Rassilka</h1>
          <p className="text-sm text-slate-400">Olimpiada botining foydalanuvchilariga xabar — matn, rasm, fayl, video va tugmalar</p>
        </div>
        <Button onClick={() => setEditing('new')}>
          <Plus className="h-4 w-4" /> Yangi rassilka
        </Button>
      </div>
      <ErrorBox message={error} />
      {list === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : list.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali rassilka yo'q</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {list.map((b) => {
            const done = b.sent + b.failed + b.blocked
            const pct = b.total > 0 ? Math.round((done / b.total) * 100) : 0
            return (
              <Card key={b.id} className="flex flex-col gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold text-slate-800">{b.title}</span>
                  <Badge className={TONE[b.status]}>{BROADCAST_STATUS[b.status]}</Badge>
                  {b.attachments.length > 0 && <Badge className="bg-slate-100 text-slate-500">📎 {b.attachments.length}</Badge>}
                  <span className="text-xs text-slate-400">
                    {b.createdBy} · {fmtDateTime(b.createdAt)}
                    {b.status === 'scheduled' && ` · Reja: ${fmtDateTime(b.scheduledAt)}`}
                  </span>
                  <div className="ml-auto flex gap-1">
                    {(b.status === 'draft' || b.status === 'scheduled') && (
                      <Button variant="secondary" onClick={() => setEditing(b)}>
                        <Pencil className="h-4 w-4" />
                      </Button>
                    )}
                    {(b.status === 'scheduled' || b.status === 'sending') && (
                      <Button variant="secondary" onClick={() => act(() => cancelBroadcast(b.id), 'Bekor qilindi')}>
                        <Ban className="h-4 w-4" /> Bekor
                      </Button>
                    )}
                    {b.status === 'draft' && (
                      <Button variant="ghost" onClick={() => act(() => deleteBroadcast(b.id), "O'chirildi")}>
                        <Trash2 className="h-4 w-4 text-red-500" />
                      </Button>
                    )}
                    {b.total > 0 && (
                      <Button variant="secondary" onClick={() => setReport(b)}>
                        Hisobot
                      </Button>
                    )}
                  </div>
                </div>
                {b.text && <RichText markup={b.text} className="line-clamp-3 text-sm text-slate-600" />}
                {b.total > 0 && (
                  <div>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                      <div className="h-2 rounded-full bg-brand-600 transition-all" style={{ width: `${pct}%` }} />
                    </div>
                    <p className="mt-1 text-xs text-slate-500">
                      Jami {b.total} · Yuborildi <b className="text-emerald-600">{b.sent}</b> · Xato <b className="text-red-600">{b.failed}</b> · Bloklagan{' '}
                      <b>{b.blocked}</b>
                      {b.pending > 0 && ` · Navbatda ${b.pending}`} · {pct}%
                    </p>
                  </div>
                )}
              </Card>
            )
          })}
        </div>
      )}
      {editing && (
        <EditorModal
          broadcast={editing === 'new' ? null : editing}
          events={events}
          schools={schools}
          onClose={() => setEditing(null)}
          onSaved={(m) => {
            setEditing(null)
            setToast(m)
            load()
          }}
        />
      )}
      {report && <DeliveriesModal b={report} onClose={() => setReport(null)} />}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
