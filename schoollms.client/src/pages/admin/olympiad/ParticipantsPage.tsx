import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, Pencil, Search, UserPlus } from 'lucide-react'
import {
  convertLeads,
  exportParticipants,
  fmtDay,
  getEvents,
  getParticipantCard,
  getParticipants,
  getSchools,
  type ConvertLeadsResult,
  type OlympiadEvent,
  type OlympiadParticipant,
  type ParticipantCard,
  type ParticipantFilter,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Drawer } from '@/components/ui/Drawer'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { Badge, ErrorBox, RegBadge } from './components/common'
import { TH, errText } from './components/lib'
import { EditParticipantModal } from './components/RegistrationsTab'

const GRADES = Array.from({ length: 11 }, (_, i) => i + 1)

function CardDrawer({ id, onClose, onEdit }: { id: string; onClose: () => void; onEdit: (c: ParticipantCard) => void }) {
  const [card, setCard] = useState<ParticipantCard | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    getParticipantCard(id)
      .then(setCard)
      .catch((e) => setError(errText(e)))
  }, [id])
  const p = card?.participant
  return (
    <Drawer open onClose={onClose} title={p?.fullName ?? 'Ishtirokchi'} size="lg" footer={card && <Button variant="secondary" onClick={() => onEdit(card)}><Pencil className="h-4 w-4" /> Tahrirlash</Button>}>
      <ErrorBox message={error} />
      {!card || !p ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <dl className="grid grid-cols-2 gap-3 text-sm">
            <Field label="Telefon" value={p.phone} />
            <Field label="Sinf" value={`${p.grade}-sinf`} />
            <Field label="Maktab" value={p.school} />
            <Field label="Ota-ona (Telegram)" value={p.hasTelegram ? `${p.parentName}${p.parentUsername ? ` (@${p.parentUsername})` : ''}` : "CRM'da qo'shilgan"} />
            <Field label="Lid" value={p.leadId ? `Aylantirilgan ${fmtDay(p.leadConvertedAt)}` : '—'} />
            <Field label="Eng yaxshi natija" value={p.bestPercent !== null ? `${p.bestPercent}%` : '—'} />
          </dl>
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">Tadbirlar</h3>
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-100">
              {card.registrations.map((r) => (
                <li key={r.registrationId} className="flex flex-wrap items-center gap-2 px-3 py-2.5 text-sm">
                  <Link to={`/admin/olimpiada/${r.eventId}`} className="min-w-0 flex-1 font-medium text-slate-800 hover:text-brand-600">
                    {r.eventTitle}
                    <span className="block text-xs font-normal text-slate-400">
                      {fmtDay(r.date)} · {r.number}
                    </span>
                  </Link>
                  <RegBadge status={r.status} />
                  {r.score !== null && (
                    <span className="text-slate-700">
                      {r.score}/{r.maxScore} ({r.percent}%){r.rank ? ` · ${r.rank}-o'rin` : ''}
                    </span>
                  )}
                </li>
              ))}
              {card.registrations.length === 0 && <li className="px-3 py-4 text-center text-sm text-slate-400">Yozuv yo'q</li>}
            </ul>
          </div>
        </div>
      )}
    </Drawer>
  )
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-slate-400">{label}</dt>
      <dd className="text-slate-800">{value}</dd>
    </div>
  )
}

/** Olimpiada → umumiy ishtirokchilar bazasi va lidga aylantirish (§11.3–11.5). */
export default function ParticipantsPage() {
  const [rows, setRows] = useState<OlympiadParticipant[] | null>(null)
  const [f, setF] = useState<ParticipantFilter>({})
  const [q, setQ] = useState('')
  const [schools, setSchools] = useState<string[]>([])
  const [events, setEvents] = useState<OlympiadEvent[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [open, setOpen] = useState<string | null>(null)
  const [editing, setEditing] = useState<ParticipantCard | null>(null)
  const [result, setResult] = useState<ConvertLeadsResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const filter: ParticipantFilter = { ...f, q: q.trim() || undefined }
  const load = useCallback(() => {
    getParticipants({ ...f, q: q.trim() || undefined })
      .then(setRows)
      .catch((e) => {
        setRows([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [f, q])

  useEffect(() => {
    const t = setTimeout(load, 250)
    return () => clearTimeout(t)
  }, [load])

  useEffect(() => {
    getSchools().then(setSchools).catch(() => {})
    getEvents().then(setEvents).catch(() => {})
  }, [])

  const toggle = (id: string) =>
    setSelected((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })

  const selectable = (rows ?? []).filter((r) => !r.leadId)
  const allSelected = selectable.length > 0 && selectable.every((r) => selected.has(r.id))

  const convert = async () => {
    setBusy(true)
    setError('')
    try {
      setResult(await convertLeads([...selected]))
      setSelected(new Set())
      load()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ishtirokchilar</h1>
          <p className="text-sm text-slate-400">Barcha olimpiadalardagi bolalar — qiziqqanlarni Lidlarga o'tkazing</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => exportParticipants(filter).catch((e) => setError(errText(e)))}>
            <Download className="h-4 w-4" /> Excel
          </Button>
          <Button disabled={busy || selected.size === 0} onClick={convert}>
            <UserPlus className="h-4 w-4" /> Lidga aylantirish{selected.size > 0 ? ` (${selected.size})` : ''}
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-end gap-2">
        <div className="relative min-w-[14rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Ism, telefon yoki maktab"
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>
        <Select value={f.eventId ?? ''} onChange={(e) => setF({ ...f, eventId: e.target.value || undefined })} className="w-auto max-w-[16rem]">
          <option value="">Barcha tadbirlar</option>
          {events.map((e) => (
            <option key={e.id} value={e.id}>
              {e.title}
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
        <Select value={f.lead ?? ''} onChange={(e) => setF({ ...f, lead: e.target.value || undefined })} className="w-auto">
          <option value="">Lid: hammasi</option>
          <option value="not">Lidga aylantirilmagan</option>
          <option value="converted">Lidga aylantirilgan</option>
        </Select>
      </div>

      <ErrorBox message={error} />

      <Card className="overflow-hidden p-0">
        {rows === null ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">Ishtirokchi topilmadi</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className={TH}>
                <tr>
                  <th className="w-10 px-4 py-3">
                    <input
                      type="checkbox"
                      checked={allSelected}
                      onChange={() => setSelected(allSelected ? new Set() : new Set(selectable.map((r) => r.id)))}
                      aria-label="Hammasini belgilash"
                    />
                  </th>
                  <th className="px-4 py-3">F.I.Sh</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Maktab</th>
                  <th className="px-4 py-3">Ota-ona</th>
                  <th className="px-4 py-3 text-right">Tadbirlar</th>
                  <th className="px-4 py-3 text-right">Qatnashgan</th>
                  <th className="px-4 py-3 text-right">Eng yaxshi</th>
                  <th className="px-4 py-3">Lid</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={r.id} className="cursor-pointer text-slate-700 hover:bg-slate-50" onClick={() => setOpen(r.id)}>
                    <td className="px-4 py-2.5" onClick={(e) => e.stopPropagation()}>
                      <input type="checkbox" disabled={!!r.leadId} checked={selected.has(r.id)} onChange={() => toggle(r.id)} />
                    </td>
                    <td className="px-4 py-2.5 font-medium">{r.fullName}</td>
                    <td className="whitespace-nowrap px-4 py-2.5">{r.phone}</td>
                    <td className="px-4 py-2.5">{r.grade}</td>
                    <td className="px-4 py-2.5">{r.school}</td>
                    <td className="px-4 py-2.5 text-xs">
                      {r.hasTelegram ? (
                        <>
                          {r.parentName}
                          {r.parentUsername && <span className="block text-slate-400">@{r.parentUsername}</span>}
                        </>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-right tabular-nums">{r.events}</td>
                    <td className="px-4 py-2.5 text-right tabular-nums">{r.attended}</td>
                    <td className="px-4 py-2.5 text-right tabular-nums">{r.bestPercent !== null ? `${r.bestPercent}%` : '—'}</td>
                    <td className="px-4 py-2.5">{r.leadId ? <Badge className="bg-brand-50 text-brand-700">Lid</Badge> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
      {rows && rows.length > 0 && <p className="text-xs text-slate-400">Jami: {rows.length}</p>}

      {open && <CardDrawer key={open} id={open} onClose={() => setOpen(null)} onEdit={(c) => setEditing(c)} />}
      {editing && (
        <EditParticipantModal
          participantId={editing.participant.id}
          initial={{
            fullName: editing.participant.fullName,
            phone: editing.participant.phone,
            grade: editing.participant.grade,
            school: editing.participant.school,
          }}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setOpen(null)
            setToast('Saqlandi')
            load()
          }}
        />
      )}
      {result && (
        <Modal open onClose={() => setResult(null)} title="Lidga aylantirish natijasi" footer={<Button onClick={() => setResult(null)}>Yopish</Button>}>
          <div className="space-y-2 text-sm text-slate-700">
            <p>
              Yangi lid: <b>{result.created}</b> · Mavjud lidga bog'landi: <b>{result.linked}</b> · O'tkazib yuborildi: <b>{result.skipped}</b>
            </p>
            {result.messages.length > 0 && (
              <ul className="list-disc space-y-1 pl-5 text-slate-500">
                {result.messages.map((m) => (
                  <li key={m}>{m}</li>
                ))}
              </ul>
            )}
            <p className="text-xs text-slate-400">Lidlar «Marketing → Lidlar» doskasining birinchi ustuniga «Olimpiada» manbasi bilan tushdi.</p>
          </div>
        </Modal>
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
