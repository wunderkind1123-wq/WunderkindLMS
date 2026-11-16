import { useCallback, useEffect, useState } from 'react'
import { BellOff, BellRing, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  TARGET_LABEL,
  addReminder,
  deleteReminder,
  fmtDateTime,
  fromLocalInput,
  getReminders,
  toLocalInput,
  toggleReminder,
  updateReminder,
  type OlympiadEvent,
  type OlympiadReminder,
  type ReminderInput,
  type ReminderTarget,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { RichText } from '@/components/ui/RichText'
import { RichTextField } from '@/components/ui/RichTextField'
import { Toast } from '@/components/ui/Toast'
import { AttachmentsEditor, Badge, ErrorBox, TestSendButton } from './common'
import { errText } from './lib'

const TOKENS = '{bola}, {tadbir}, {sana}, {vaqt}, {manzil}, {raqam}, {sinf}, {maktab}, {navbat}, {ism}'

function offsetLabel(min: number): string {
  const d = Math.floor(min / 1440)
  const h = Math.floor((min % 1440) / 60)
  const m = min % 60
  return [d && `${d} kun`, h && `${h} soat`, m && `${m} daqiqa`].filter(Boolean).join(' ') + ' oldin'
}

function ReminderModal({
  event,
  reminder,
  onClose,
  onSaved,
}: {
  event: OlympiadEvent
  reminder: OlympiadReminder | null
  onClose: () => void
  onSaved: () => void
}) {
  const standard = !!reminder && reminder.kind !== 'custom'
  const [mode, setMode] = useState<'offset' | 'exact'>(reminder?.sendAt ? 'exact' : 'offset')
  const [hours, setHours] = useState(reminder?.offsetMinutes ? Math.floor(reminder.offsetMinutes / 60) : 24)
  const [mins, setMins] = useState(reminder?.offsetMinutes ? reminder.offsetMinutes % 60 : 0)
  const [sendAt, setSendAt] = useState(toLocalInput(reminder?.sendAt))
  const [text, setText] = useState(reminder?.text ?? '')
  const [attachments, setAttachments] = useState(reminder?.attachments ?? [])
  const [target, setTarget] = useState<ReminderTarget>(reminder?.target ?? 'registered')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [toast, setToast] = useState<string | null>(null)

  const save = async () => {
    const body: ReminderInput = {
      offsetMinutes: mode === 'offset' ? hours * 60 + mins : null,
      sendAt: mode === 'exact' ? fromLocalInput(sendAt) : null,
      text,
      attachments,
      target,
    }
    setBusy(true)
    setError('')
    try {
      if (reminder) await updateReminder(reminder.id, body)
      else await addReminder(event.id, body)
      onSaved()
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={reminder ? 'Eslatmani tahrirlash' : "Eslatma qo'shish"}
      size="lg"
      footer={
        <>
          <TestSendButton text={text} attachments={attachments} onDone={setToast} onError={setError} />
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button disabled={busy} onClick={save}>
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <ErrorBox message={error} />
        {!standard && (
          <div className="inline-flex rounded-lg bg-slate-100 p-1 text-sm">
            {(['offset', 'exact'] as const).map((m) => (
              <button
                key={m}
                onClick={() => setMode(m)}
                className={'rounded-md px-3 py-1.5 ' + (mode === m ? 'bg-white font-medium text-slate-800 shadow-sm' : 'text-slate-500')}
              >
                {m === 'offset' ? 'Tadbirgacha' : 'Aniq vaqt'}
              </button>
            ))}
          </div>
        )}
        {mode === 'offset' ? (
          <div className="grid grid-cols-2 gap-3">
            <Input label="Soat oldin" type="number" min={0} value={hours} onChange={(e) => setHours(Math.max(0, Number(e.target.value)))} />
            <Input label="Daqiqa" type="number" min={0} max={59} value={mins} onChange={(e) => setMins(Math.min(59, Math.max(0, Number(e.target.value))))} />
          </div>
        ) : (
          <Input label="Yuborish vaqti" type="datetime-local" value={sendAt} onChange={(e) => setSendAt(e.target.value)} />
        )}
        <Select label="Kimga" value={target} onChange={(e) => setTarget(e.target.value as ReminderTarget)}>
          {Object.entries(TARGET_LABEL).map(([k, v]) => (
            <option key={k} value={k}>
              {v}
            </option>
          ))}
        </Select>
        <div>
          <RichTextField
            label={standard ? 'Matn (bo\'sh — standart shablon)' : 'Matn'}
            value={text}
            onChange={setText}
            rows={6}
          />
          <p className="mt-1 text-xs text-slate-400">Tokenlar: {TOKENS}</p>
        </div>
        <AttachmentsEditor value={attachments} onChange={setAttachments} />
      </div>
      <Toast message={toast} onClose={() => setToast(null)} />
    </Modal>
  )
}

const STATUS: Record<OlympiadReminder['status'], { label: string; tone: string }> = {
  scheduled: { label: 'Rejada', tone: 'bg-brand-50 text-brand-700' },
  sent: { label: 'Yuborildi', tone: 'bg-emerald-50 text-emerald-700' },
  cancelled: { label: "O'chirilgan", tone: 'bg-slate-100 text-slate-400' },
}

/** Tadbir → Eslatmalar (§7): standart (1 kun va 3 soat oldin) va qo'shimchalar. */
export function RemindersTab({ event }: { event: OlympiadEvent }) {
  const [list, setList] = useState<OlympiadReminder[] | null>(null)
  const [editing, setEditing] = useState<OlympiadReminder | 'new' | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    getReminders(event.id)
      .then(setList)
      .catch((e) => {
        setList([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [event.id])
  useEffect(load, [load])

  const act = async (fn: () => Promise<unknown>) => {
    setError('')
    try {
      await fn()
      load()
    } catch (e) {
      setError(errText(e))
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-slate-500">
          Eslatma har odamga bir marta boradi. Tadbir vaqti o'zgarsa, «tadbirgacha» eslatmalar o'zi siljiydi. Bekor qilganlarga bormaydi.
        </p>
        <Button onClick={() => setEditing('new')}>
          <Plus className="h-4 w-4" /> Eslatma qo'shish
        </Button>
      </div>
      <ErrorBox message={error} />
      {list === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : list.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">Eslatma yo'q</p>
        </Card>
      ) : (
        list.map((r) => (
          <Card key={r.id} className="flex flex-col gap-2">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium text-slate-800">
                {r.offsetMinutes ? offsetLabel(r.offsetMinutes) : fmtDateTime(r.sendAt)}
              </span>
              {r.kind !== 'custom' && <Badge className="bg-slate-100 text-slate-500">Standart</Badge>}
              <Badge className={STATUS[r.status].tone}>{STATUS[r.status].label}</Badge>
              <Badge className="bg-slate-100 text-slate-500">{TARGET_LABEL[r.target]}</Badge>
              <span className="text-xs text-slate-400">
                {r.status === 'sent' ? `Yuborildi: ${fmtDateTime(r.sentAt)} · ${r.sentCount} ta` : r.dueAt ? `Vaqti: ${fmtDateTime(r.dueAt)}` : ''}
              </span>
              <div className="ml-auto flex gap-1">
                {r.status !== 'sent' && (
                  <>
                    <button title="Tahrirlash" onClick={() => setEditing(r)} className="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      title={r.status === 'cancelled' ? 'Yoqish' : "O'chirib qo'yish"}
                      onClick={() => act(() => toggleReminder(r.id))}
                      className="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                    >
                      {r.status === 'cancelled' ? <BellRing className="h-4 w-4" /> : <BellOff className="h-4 w-4" />}
                    </button>
                  </>
                )}
                {r.kind === 'custom' && (
                  <button title="O'chirish" onClick={() => act(() => deleteReminder(r.id))} className="rounded p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                    <Trash2 className="h-4 w-4" />
                  </button>
                )}
              </div>
            </div>
            {r.text ? (
              <RichText markup={r.text} className="text-sm text-slate-600" />
            ) : (
              <p className="text-sm text-slate-400">Standart shablon matni (Sozlamalar → Xabar shablonlari)</p>
            )}
            {r.attachments.length > 0 && <p className="text-xs text-slate-400">Biriktirmalar: {r.attachments.length}</p>}
          </Card>
        ))
      )}
      {editing && (
        <ReminderModal
          event={event}
          reminder={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}
