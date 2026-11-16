import { useEffect, useState } from 'react'
import { fmtDay, getCheckinEvents, hm, type CheckinEvent } from '@/api/services/olympiad'
import { Card } from '@/components/ui/Card'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { CheckinPanel } from './components/CheckinPanel'
import { ErrorBox } from './components/common'
import { errText } from './components/lib'

const todayIso = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10)

/** Olimpiada → Kirish (QR): telefonda ochiladigan sahifa (docs/modules/olympiad.md §8). */
export default function CheckinPage() {
  const [events, setEvents] = useState<CheckinEvent[] | null>(null)
  const [eventId, setEventId] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    getCheckinEvents()
      .then((list) => {
        setEvents(list)
        // Standart: bugungi, bo'lmasa eng yaqin kelgusi, bo'lmasa oxirgisi.
        const today = todayIso()
        const pick = list.find((e) => e.date === today) ?? list.find((e) => e.date > today) ?? list[list.length - 1]
        if (pick) setEventId(pick.id)
      })
      .catch((e) => {
        setEvents([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [])

  const current = events?.find((e) => e.id === eventId)

  return (
    <div className="mx-auto max-w-xl space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Kirishni belgilash</h1>
        <p className="text-sm text-slate-400">QR kodni skanerlang yoki ism/raqam bo'yicha qidiring</p>
      </div>
      <ErrorBox message={error} />
      {events === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : events.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-slate-400">E'lon qilingan tadbir yo'q</p>
        </Card>
      ) : (
        <>
          <Select value={eventId} onChange={(e) => setEventId(e.target.value)} className="py-3 text-base">
            {events.map((e) => (
              <option key={e.id} value={e.id}>
                {fmtDay(e.date)} {hm(e.startTime)} — {e.title}
              </option>
            ))}
          </Select>
          {current && <CheckinPanel key={current.id} eventId={current.id} closed={!!current.checkinClosedAt} />}
        </>
      )}
    </div>
  )
}
