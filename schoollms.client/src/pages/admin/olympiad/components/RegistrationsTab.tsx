import { useCallback, useEffect, useState } from 'react'
import { ArrowUpCircle, Download, Pencil, Plus, Search, XCircle } from 'lucide-react'
import {
  REG_STATUS,
  addRegistration,
  cancelRegistration,
  exportEvent,
  fmtDateTime,
  getParticipants,
  getRegistrations,
  promoteRegistration,
  updateParticipant,
  type OlympiadEvent,
  type OlympiadParticipant,
  type OlympiadRegistration,
  type ParticipantInput,
  type RegFilter,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { Confirm, ErrorBox, RegBadge } from './common'
import { TH, errText } from './lib'

const GRADES = Array.from({ length: 11 }, (_, i) => i + 1)

/** Bola ma'lumoti formasi (qo'shish va tahrirlash). */
export function ParticipantForm({ value, onChange }: { value: ParticipantInput; onChange: (v: ParticipantInput) => void }) {
  return (
    <div className="space-y-3">
      <Input label="F.I.Sh" required value={value.fullName} onChange={(e) => onChange({ ...value, fullName: e.target.value })} placeholder="Karimov Aziz" />
      <PhoneInput label="Telefon" required value={value.phone} onChange={(phone) => onChange({ ...value, phone })} />
      <div className="grid gap-3 sm:grid-cols-2">
        <Select label="Sinf" required value={value.grade} onChange={(e) => onChange({ ...value, grade: Number(e.target.value) })}>
          {GRADES.map((g) => (
            <option key={g} value={g}>
              {g}-sinf
            </option>
          ))}
        </Select>
        <Input label="Maktab" required value={value.school} onChange={(e) => onChange({ ...value, school: e.target.value })} placeholder="Yaypan 5-maktab" />
      </div>
    </div>
  )
}

export function EditParticipantModal({
  initial,
  participantId,
  onClose,
  onSaved,
}: {
  initial: ParticipantInput
  participantId: string
  onClose: () => void
  onSaved: () => void
}) {
  const [v, setV] = useState(initial)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  return (
    <Modal
      open
      onClose={onClose}
      title="Ishtirokchini tahrirlash"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button
            disabled={busy}
            onClick={async () => {
              setBusy(true)
              setError('')
              try {
                await updateParticipant(participantId, v)
                onSaved()
              } catch (e) {
                setError(errText(e, "Saqlab bo'lmadi"))
              } finally {
                setBusy(false)
              }
            }}
          >
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <ErrorBox message={error} />
        <ParticipantForm value={v} onChange={setV} />
        <p className="text-xs text-slate-400">O'zgarish bolaning barcha yozuvlarida ko'rinadi.</p>
      </div>
    </Modal>
  )
}

function AddModal({ event, onClose, onSaved }: { event: OlympiadEvent; onClose: () => void; onSaved: (r: OlympiadRegistration) => void }) {
  const [mode, setMode] = useState<'new' | 'existing'>('new')
  const [kid, setKid] = useState<ParticipantInput>({ fullName: '', phone: '', grade: 5, school: '' })
  const [q, setQ] = useState('')
  const [found, setFound] = useState<OlympiadParticipant[]>([])
  const [picked, setPicked] = useState<OlympiadParticipant | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (mode !== 'existing' || q.trim().length < 2) return
    const t = setTimeout(() => getParticipants({ q: q.trim() }).then((l) => setFound(l.slice(0, 20))).catch(() => {}), 300)
    return () => clearTimeout(t)
  }, [q, mode])

  const save = async () => {
    setBusy(true)
    setError('')
    try {
      onSaved(await addRegistration(event.id, mode === 'new' ? { participant: kid } : { participantId: picked!.id }))
    } catch (e) {
      setError(errText(e, "Qo'shib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Ishtirokchi qo'shish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button disabled={busy || (mode === 'existing' && !picked)} onClick={save}>
            Qo'shish
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="inline-flex rounded-lg bg-slate-100 p-1 text-sm">
          {(['new', 'existing'] as const).map((m) => (
            <button
              key={m}
              onClick={() => setMode(m)}
              className={'rounded-md px-3 py-1.5 ' + (mode === m ? 'bg-white font-medium text-slate-800 shadow-sm' : 'text-slate-500')}
            >
              {m === 'new' ? 'Yangi bola' : 'Bazadan tanlash'}
            </button>
          ))}
        </div>
        <ErrorBox message={error} />
        {mode === 'new' ? (
          <ParticipantForm value={kid} onChange={setKid} />
        ) : (
          <div className="space-y-2">
            <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ism, telefon yoki maktab" />
            <ul className="max-h-72 divide-y divide-slate-100 overflow-y-auto">
              {found.map((p) => (
                <li key={p.id}>
                  <button
                    onClick={() => setPicked(p)}
                    className={'w-full px-2 py-2 text-left text-sm ' + (picked?.id === p.id ? 'bg-brand-50' : 'hover:bg-slate-50')}
                  >
                    <span className="font-medium text-slate-800">{p.fullName}</span>
                    <span className="ml-2 text-slate-400">
                      {p.grade}-sinf · {p.school} · {p.phone}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
        <p className="text-xs text-slate-400">
          Qo'lda qo'shilganda ro'yxat muddati tekshirilmaydi, limit — tekshiriladi. Bola botdan kelmagan bo'lsa, unga xabar bormaydi.
        </p>
      </div>
    </Modal>
  )
}

/** Tadbir → Ishtirokchilar (§11.2). */
export function RegistrationsTab({ event, onChanged }: { event: OlympiadEvent; onChanged: () => void }) {
  const [rows, setRows] = useState<OlympiadRegistration[] | null>(null)
  const [f, setF] = useState<RegFilter>({})
  const [q, setQ] = useState('')
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<OlympiadRegistration | null>(null)
  const [cancelling, setCancelling] = useState<OlympiadRegistration | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const filter = { ...f, q: q.trim() || undefined }
  const load = useCallback(() => {
    getRegistrations(event.id, { ...f, q: q.trim() || undefined })
      .then(setRows)
      .catch((e) => {
        setRows([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [event.id, f, q])

  useEffect(() => {
    const t = setTimeout(load, 250)
    return () => clearTimeout(t)
  }, [load])

  const schools = Array.from(new Set((rows ?? []).map((r) => r.school))).sort()

  const act = async (fn: () => Promise<unknown>) => {
    setBusy(true)
    setError('')
    try {
      await fn()
      load()
      onChanged()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-2">
        <div className="relative min-w-[14rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Ism, telefon yoki raqam"
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>
        <Select value={f.status ?? ''} onChange={(e) => setF({ ...f, status: e.target.value || undefined })} className="w-auto">
          <option value="">Barcha holatlar</option>
          {Object.entries(REG_STATUS).map(([k, v]) => (
            <option key={k} value={k}>
              {v}
            </option>
          ))}
        </Select>
        <Select value={f.grade ?? ''} onChange={(e) => setF({ ...f, grade: e.target.value || undefined })} className="w-auto">
          <option value="">Barcha sinflar</option>
          {GRADES.map((g) => (
            <option key={g} value={g}>
              {g}-sinf
            </option>
          ))}
        </Select>
        <Select value={f.school ?? ''} onChange={(e) => setF({ ...f, school: e.target.value || undefined })} className="w-auto max-w-[14rem]">
          <option value="">Barcha maktablar</option>
          {schools.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </Select>
        <Button variant="secondary" onClick={() => exportEvent(event.id, 'registrations', filter).catch((e) => setError(errText(e)))}>
          <Download className="h-4 w-4" /> Excel
        </Button>
        <Button onClick={() => setAdding(true)}>
          <Plus className="h-4 w-4" /> Qo'shish
        </Button>
      </div>

      <ErrorBox message={error} />

      <Card className="overflow-hidden p-0">
        {rows === null ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">Ishtirokchi yo'q</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className={TH}>
                <tr>
                  <th className="px-4 py-3">Raqam</th>
                  <th className="px-4 py-3">F.I.Sh</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Maktab</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Ota-ona</th>
                  <th className="px-4 py-3">Yozilgan</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={r.id} className={r.status === 'cancelled' ? 'text-slate-400' : 'text-slate-700'}>
                    <td className="whitespace-nowrap px-4 py-2.5 font-mono text-xs">{r.number}</td>
                    <td className="px-4 py-2.5 font-medium">
                      {r.fullName}
                      {r.leadId && <span className="ml-1 text-xs text-brand-600">· lid</span>}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5">{r.phone}</td>
                    <td className="px-4 py-2.5">{r.grade}</td>
                    <td className="px-4 py-2.5">{r.school}</td>
                    <td className="whitespace-nowrap px-4 py-2.5">
                      <RegBadge status={r.status} />
                      {r.waitlistPosition > 0 && <span className="ml-1 text-xs text-slate-400">#{r.waitlistPosition}</span>}
                    </td>
                    <td className="px-4 py-2.5 text-xs">
                      {r.hasTelegram ? (
                        <>
                          {r.parentName}
                          {r.parentUsername && <span className="block text-slate-400">@{r.parentUsername}</span>}
                        </>
                      ) : (
                        <span className="text-slate-400">CRM'da qo'shilgan</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-xs text-slate-400">{fmtDateTime(r.createdAt)}</td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-right">
                      {r.status === 'waitlist' && (
                        <button
                          title="Ro'yxatga o'tkazish (limitdan oshsa ham)"
                          disabled={busy}
                          onClick={() => act(() => promoteRegistration(r.id))}
                          className="rounded p-1 text-slate-400 hover:bg-emerald-50 hover:text-emerald-600"
                        >
                          <ArrowUpCircle className="h-4 w-4" />
                        </button>
                      )}
                      <button title="Tahrirlash" onClick={() => setEditing(r)} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
                        <Pencil className="h-4 w-4" />
                      </button>
                      {(r.status === 'registered' || r.status === 'waitlist') && (
                        <button title="Bekor qilish" onClick={() => setCancelling(r)} className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600">
                          <XCircle className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {adding && (
        <AddModal
          event={event}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false)
            load()
            onChanged()
          }}
        />
      )}
      {editing && (
        <EditParticipantModal
          participantId={editing.participantId}
          initial={{ fullName: editing.fullName, phone: editing.phone, grade: editing.grade, school: editing.school }}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
      <Confirm
        open={!!cancelling}
        title="Yozuvni bekor qilish"
        danger
        confirmLabel="Bekor qilish"
        busy={busy}
        onClose={() => setCancelling(null)}
        onConfirm={() => {
          const r = cancelling!
          setCancelling(null)
          act(() => cancelRegistration(r.id))
        }}
      >
        {cancelling?.fullName} ro'yxatdan chiqariladi. Kutish ro'yxati bo'lsa, navbatdagi bola avtomatik o'tadi va unga xabar boradi.
      </Confirm>
    </div>
  )
}
