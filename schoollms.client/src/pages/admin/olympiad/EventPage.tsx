import { useCallback, useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, CalendarDays, Link2, MapPin, Pencil, Rocket } from 'lucide-react'
import { EVENT_STATUS, fmtDateTime, fmtDay, getEvent, hm, setEventStatus, type OlympiadEvent } from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { CheckinPanel } from './components/CheckinPanel'
import { Badge, ErrorBox } from './components/common'
import { EventFormModal } from './components/EventFormModal'
import { RegistrationsTab } from './components/RegistrationsTab'
import { RemindersTab } from './components/RemindersTab'
import { ResultsTab } from './components/ResultsTab'
import { StatsTab } from './components/StatsTab'
import { EVENT_TONE, copyText, errText } from './components/lib'

const TABS = [
  { key: 'ishtirokchilar', label: 'Ishtirokchilar' },
  { key: 'kirish', label: 'Kirish' },
  { key: 'natijalar', label: 'Natijalar' },
  { key: 'eslatmalar', label: 'Eslatmalar' },
  { key: 'statistika', label: 'Statistika' },
] as const
type Tab = (typeof TABS)[number]['key']

/** Olimpiada → tadbir sahifasi (§11.2). Tab manzilda (`?tab=`) — sahifa yangilansa ham joyida qoladi. */
export default function EventPage() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab = (TABS.find((t) => t.key === params.get('tab'))?.key ?? 'ishtirokchilar') as Tab
  const [event, setEvent] = useState<OlympiadEvent | null>(null)
  const [editing, setEditing] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    getEvent(id)
      .then(setEvent)
      .catch((e) => setError(errText(e, 'Tadbir topilmadi')))
  }, [id])
  useEffect(load, [load])

  if (error && !event) return <ErrorBox message={error} />
  if (!event) return <Loader label="Yuklanmoqda..." />

  const seats = event.capacity ? `${event.registered} / ${event.capacity}` : `${event.registered}`

  return (
    <div className="space-y-5">
      <Link to="/admin/olimpiada" className="inline-flex items-center gap-1 text-sm text-slate-400 hover:text-slate-600">
        <ArrowLeft className="h-4 w-4" /> Olimpiadalar
      </Link>

      <Card className="flex flex-col gap-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-xl font-semibold text-slate-800">{event.title}</h1>
              <Badge className={EVENT_TONE[event.status]}>{EVENT_STATUS[event.status]}</Badge>
              {event.status === 'published' && (
                <Badge className={event.registrationOpen ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'}>
                  {event.registrationOpen ? "Ro'yxat ochiq" : "Ro'yxat yopiq"}
                </Badge>
              )}
            </div>
            <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm text-slate-500">
              <span className="inline-flex items-center gap-1">
                <CalendarDays className="h-4 w-4" /> {fmtDay(event.date)}, {hm(event.startTime)}–{hm(event.endTime)}
              </span>
              {event.location && (
                <span className="inline-flex items-center gap-1">
                  <MapPin className="h-4 w-4" /> {event.location}
                </span>
              )}
              {(event.registrationOpensAt || event.registrationClosesAt) && (
                <span>
                  Ro'yxat: {event.registrationOpensAt ? fmtDateTime(event.registrationOpensAt) : 'darhol'} —{' '}
                  {event.registrationClosesAt ? fmtDateTime(event.registrationClosesAt) : 'boshlanguncha'}
                </span>
              )}
            </div>
          </div>
          <div className="flex flex-wrap gap-2">
            {event.status === 'draft' && (
              <Button
                onClick={() =>
                  setEventStatus(event.id, 'published')
                    .then((e) => {
                      setEvent(e)
                      setToast("E'lon qilindi — botda ko'rinadi")
                    })
                    .catch((e) => setError(errText(e)))
                }
              >
                <Rocket className="h-4 w-4" /> E'lon qilish
              </Button>
            )}
            {event.deepLink && (
              <Button variant="secondary" onClick={() => copyText(event.deepLink, setToast)} title={event.deepLink}>
                <Link2 className="h-4 w-4" /> Bot havolasi
              </Button>
            )}
            <Button variant="secondary" onClick={() => setEditing(true)}>
              <Pencil className="h-4 w-4" /> Tahrirlash
            </Button>
          </div>
        </div>
        <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-slate-600">
          <span>
            Ro'yxatda: <b>{seats}</b>
          </span>
          <span>
            Kutishda: <b>{event.waitlist}</b>
          </span>
          <span>
            Keldi: <b>{event.attended}</b>
          </span>
          <span>
            Kelmadi: <b>{event.noShow}</b>
          </span>
          <span>
            Bekor: <b>{event.cancelled}</b>
          </span>
          <span>
            Natija: <b>{event.withScore}</b>
          </span>
        </div>
      </Card>

      <ErrorBox message={error} />

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setParams({ tab: t.key }, { replace: true })}
            className={cn(
              '-mb-px whitespace-nowrap border-b-2 px-4 py-2 text-sm font-medium transition-colors',
              tab === t.key ? 'border-brand-600 text-brand-700' : 'border-transparent text-slate-500 hover:text-slate-700',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'ishtirokchilar' && <RegistrationsTab event={event} onChanged={load} />}
      {tab === 'kirish' && (
        <div className="max-w-xl">
          <CheckinPanel eventId={event.id} closed={!!event.checkinClosedAt} onFinished={load} />
        </div>
      )}
      {tab === 'natijalar' && <ResultsTab event={event} onChanged={load} />}
      {tab === 'eslatmalar' && <RemindersTab event={event} />}
      {tab === 'statistika' && <StatsTab eventId={event.id} />}

      {editing && (
        <EventFormModal
          event={event}
          onClose={() => setEditing(false)}
          onSaved={(e) => {
            setEditing(false)
            setEvent(e)
            setToast('Saqlandi')
          }}
        />
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
