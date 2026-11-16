import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Archive, CalendarDays, CheckCircle2, Copy, Link2, MapPin, Pencil, Plus, Rocket, Trash2, Users } from 'lucide-react'
import {
  EVENT_STATUS,
  deleteEvent,
  duplicateEvent,
  fmtDay,
  getEvents,
  hm,
  setEventStatus,
  type OlympiadEvent,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { Badge, Confirm, ErrorBox } from './components/common'
import { EventFormModal } from './components/EventFormModal'
import { EVENT_TONE, copyText, errText } from './components/lib'

/** Olimpiada → Tadbirlar (docs/modules/olympiad.md §11.1). */
export default function EventsPage() {
  const [events, setEvents] = useState<OlympiadEvent[] | null>(null)
  const [editing, setEditing] = useState<OlympiadEvent | 'new' | null>(null)
  const [removing, setRemoving] = useState<OlympiadEvent | null>(null)
  const [showArchive, setShowArchive] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    getEvents()
      .then(setEvents)
      .catch((e) => {
        setEvents([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [])
  useEffect(load, [load])

  const run = async (action: () => Promise<unknown>, done: string) => {
    setBusy(true)
    setError('')
    try {
      await action()
      setToast(done)
      load()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  const visible = (events ?? []).filter((e) => showArchive || e.status !== 'archived')
  const archivedCount = (events ?? []).filter((e) => e.status === 'archived').length

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Olimpiadalar</h1>
          <p className="text-sm text-slate-400">Tashqi bolalar uchun bepul olimpiadalar — ro'yxat bot orqali, kirish QR bilan</p>
        </div>
        <Button onClick={() => setEditing('new')}>
          <Plus className="h-4 w-4" /> Yangi olimpiada
        </Button>
      </div>

      <ErrorBox message={error} />

      {events === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : visible.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali olimpiada yo'q — «Yangi olimpiada» tugmasini bosing</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {visible.map((e) => (
            <Card key={e.id} className="flex flex-col gap-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Link to={`/admin/olimpiada/${e.id}`} className="text-base font-semibold text-slate-800 hover:text-brand-600">
                      {e.title}
                    </Link>
                    <Badge className={EVENT_TONE[e.status]}>{EVENT_STATUS[e.status]}</Badge>
                    {e.status === 'published' && (
                      <Badge className={e.registrationOpen ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'}>
                        {e.registrationOpen ? "Ro'yxat ochiq" : "Ro'yxat yopiq"}
                      </Badge>
                    )}
                    <Badge className="bg-slate-100 text-slate-500">M{e.code}</Badge>
                  </div>
                  <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm text-slate-500">
                    <span className="inline-flex items-center gap-1">
                      <CalendarDays className="h-4 w-4" /> {fmtDay(e.date)}, {hm(e.startTime)}–{hm(e.endTime)}
                    </span>
                    {e.location && (
                      <span className="inline-flex items-center gap-1">
                        <MapPin className="h-4 w-4" /> {e.location}
                      </span>
                    )}
                  </div>
                </div>
                <div className="flex flex-wrap gap-2">
                  {e.status === 'draft' && (
                    <Button disabled={busy} onClick={() => run(() => setEventStatus(e.id, 'published'), "E'lon qilindi — botda ko'rinadi")}>
                      <Rocket className="h-4 w-4" /> E'lon qilish
                    </Button>
                  )}
                  {e.deepLink && (
                    <Button variant="secondary" onClick={() => copyText(e.deepLink, setToast)} title={e.deepLink}>
                      <Link2 className="h-4 w-4" /> Havola
                    </Button>
                  )}
                  <Button variant="secondary" onClick={() => setEditing(e)}>
                    <Pencil className="h-4 w-4" />
                  </Button>
                  <Button variant="secondary" disabled={busy} title="Nusxa olish" onClick={() => run(() => duplicateEvent(e.id), 'Nusxa yaratildi (qoralama)')}>
                    <Copy className="h-4 w-4" />
                  </Button>
                  {e.status === 'published' && (
                    <Button variant="secondary" disabled={busy} title="Yakunlash" onClick={() => run(() => setEventStatus(e.id, 'finished'), 'Yakunlandi')}>
                      <CheckCircle2 className="h-4 w-4" />
                    </Button>
                  )}
                  {e.status === 'finished' && (
                    <Button variant="secondary" disabled={busy} title="Arxivlash" onClick={() => run(() => setEventStatus(e.id, 'archived'), 'Arxivlandi')}>
                      <Archive className="h-4 w-4" />
                    </Button>
                  )}
                  {e.status === 'archived' && (
                    <Button variant="secondary" disabled={busy} onClick={() => run(() => setEventStatus(e.id, 'finished'), 'Arxivdan chiqarildi')}>
                      Arxivdan chiqarish
                    </Button>
                  )}
                  {e.status === 'draft' && e.registered + e.waitlist + e.cancelled === 0 && (
                    <Button variant="ghost" title="O'chirish" onClick={() => setRemoving(e)}>
                      <Trash2 className="h-4 w-4 text-red-500" />
                    </Button>
                  )}
                </div>
              </div>

              <Link to={`/admin/olimpiada/${e.id}`} className="grid grid-cols-2 gap-2 sm:grid-cols-5">
                <Stat label="Ro'yxatda" value={e.capacity ? `${e.registered} / ${e.capacity}` : String(e.registered)} icon />
                <Stat label="Kutishda" value={e.waitlist} />
                <Stat label="Keldi" value={e.attended} />
                <Stat label="Kelmadi" value={e.noShow} />
                <Stat label="Natija kiritilgan" value={e.withScore} />
              </Link>
            </Card>
          ))}
        </div>
      )}

      {archivedCount > 0 && (
        <button className="text-sm text-slate-400 hover:text-slate-600" onClick={() => setShowArchive((v) => !v)}>
          {showArchive ? 'Arxivni yashirish' : `Arxiv (${archivedCount})`}
        </button>
      )}

      {editing && (
        <EventFormModal
          event={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setToast('Saqlandi')
            load()
          }}
        />
      )}

      <Confirm
        open={!!removing}
        title="Tadbirni o'chirish"
        danger
        confirmLabel="O'chirish"
        busy={busy}
        onClose={() => setRemoving(null)}
        onConfirm={() => {
          const e = removing!
          setRemoving(null)
          run(() => deleteEvent(e.id), "O'chirildi")
        }}
      >
        «{removing?.title}» o'chirilsinmi?
      </Confirm>

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}

function Stat({ label, value, icon }: { label: string; value: string | number; icon?: boolean }) {
  return (
    <div className="rounded-xl bg-slate-50 px-3 py-2">
      <p className="text-xs text-slate-400">{label}</p>
      <p className="flex items-center gap-1 text-lg font-semibold text-slate-800">
        {icon && <Users className="h-4 w-4 text-slate-400" />}
        {value}
      </p>
    </div>
  )
}
