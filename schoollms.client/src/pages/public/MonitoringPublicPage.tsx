import { useEffect, useState } from 'react'
import { CheckCircle2, Clock } from 'lucide-react'
import {
  finishSession,
  letter,
  monitoringError,
  saveSessionAnswer,
  startSession,
  type MonitoringSession,
} from '@/api/services/monitoring'
import { cn } from '@/lib/utils'

const DEVICE_KEY = 'wk-monitoring-device'

/** Shu brauzerning qurilma siri — test boshqa qurilmada davom ettirilmasin (server tekshiradi). */
function deviceId(): string {
  try {
    const saved = localStorage.getItem(DEVICE_KEY)
    if (saved) return saved
    const fresh = Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('')
    localStorage.setItem(DEVICE_KEY, fresh)
    return fresh
  } catch {
    return Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('')
  }
}

/**
 * Ichki monitoring — o'quvchi shaxsiy kodi bilan (akkauntsiz, mijoz tanlovi 2026-11-06). Javob har
 * bosishda saqlanadi; vaqt tugasa test o'zi yakunlanadi. Natija ko'rsatilmaydi — uni o'quv bo'limi ko'radi.
 */
export function MonitoringPublicPage() {
  const [device] = useState(deviceId)
  const [code, setCode] = useState('')
  const [session, setSession] = useState<MonitoringSession | null>(null)
  const [answers, setAnswers] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [])

  const left = session ? Math.max(0, Math.floor((new Date(session.deadline).getTime() - now) / 1000)) : 0

  useEffect(() => {
    if (session && !session.finished && left === 0) void finish(true)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat vaqt tugaganda
  }, [left === 0, session?.finished])

  async function start(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const s = await startSession(code, device)
      setSession(s)
      setAnswers(s.answers)
    } catch (e) {
      setError(monitoringError(e, 'Kirib bo‘lmadi'))
    } finally {
      setBusy(false)
    }
  }

  async function choose(order: number, choice: string): Promise<void> {
    const prev = answers
    const chars = answers.padEnd(order, '-').split('')
    chars[order - 1] = chars[order - 1] === choice ? '-' : choice
    setAnswers(chars.join(''))
    try {
      await saveSessionAnswer(code, device, order, chars[order - 1])
    } catch (e) {
      setAnswers(prev)
      setError(monitoringError(e, 'Javob saqlanmadi — internetni tekshiring'))
    }
  }

  async function finish(auto = false): Promise<void> {
    if (!auto && !confirm('Testni yakunlaysizmi? Keyin javoblarni o‘zgartirib bo‘lmaydi.')) return
    try {
      setSession(await finishSession(code, device))
    } catch (e) {
      setError(monitoringError(e))
    }
  }

  if (!session) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 p-4">
        <div className="w-full max-w-sm rounded-2xl bg-white p-6 shadow-sm">
          <h1 className="text-lg font-semibold text-slate-800">Monitoring testi</h1>
          <p className="mt-1 text-sm text-slate-500">O‘qituvchingiz bergan shaxsiy kodni kiriting</p>
          <input
            className="mt-4 w-full rounded-xl border border-slate-200 px-4 py-3 text-center font-mono text-2xl uppercase tracking-[0.3em] outline-none focus:border-brand-400"
            value={code}
            maxLength={10}
            autoFocus
            placeholder="XXXXXXXX"
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            onKeyDown={(e) => e.key === 'Enter' && void start()}
          />
          {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
          <button type="button" disabled={busy || code.trim().length < 8} onClick={() => void start()}
            className="mt-4 w-full rounded-xl bg-brand-600 py-3 font-medium text-white disabled:opacity-50">
            {busy ? 'Tekshirilmoqda…' : 'Boshlash'}
          </button>
        </div>
      </div>
    )
  }

  if (session.finished) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 p-4">
        <div className="w-full max-w-sm rounded-2xl bg-white p-6 text-center shadow-sm">
          <CheckCircle2 className="mx-auto h-12 w-12 text-emerald-500" />
          <h1 className="mt-3 text-lg font-semibold text-slate-800">Test topshirildi</h1>
          <p className="mt-1 text-sm text-slate-500">{session.studentName}, javoblaringiz saqlandi. Rahmat!</p>
        </div>
      </div>
    )
  }

  const answered = answers.replace(/-/g, '').length
  const mm = String(Math.floor(left / 60)).padStart(2, '0')
  const ss = String(left % 60).padStart(2, '0')

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 px-4 py-3 backdrop-blur">
        <div className="mx-auto flex max-w-3xl items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="truncate font-semibold text-slate-800">{session.title}</p>
            <p className="truncate text-xs text-slate-500">{session.studentName} · {session.subjectName} · {answered}/{session.items.length} javob</p>
          </div>
          <span className={cn('inline-flex items-center gap-1 rounded-lg px-2.5 py-1 font-mono text-sm tabular-nums', left < 300 ? 'bg-red-50 text-red-600' : 'bg-slate-100 text-slate-700')}>
            <Clock className="h-4 w-4" /> {mm}:{ss}
          </span>
        </div>
      </header>
      <main className="mx-auto max-w-3xl space-y-3 p-4">
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        {session.items.map((it) => {
          const chosen = answers[it.order - 1]
          return (
            <section key={it.order} className="rounded-2xl bg-white p-4 shadow-sm">
              <p className="whitespace-pre-wrap text-slate-800"><b className="text-slate-400">{it.order}.</b> {it.text}</p>
              {it.imageUrl && <img src={it.imageUrl} alt="" className="mt-2 max-h-64 rounded-lg" />}
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {Array.from({ length: it.optionCount }, (_, k) => (
                  <button key={k} type="button" onClick={() => void choose(it.order, letter(k))}
                    className={cn('rounded-xl border px-3 py-2.5 text-left text-sm transition-colors',
                      chosen === letter(k) ? 'border-brand-500 bg-brand-50 font-medium text-brand-800' : 'border-slate-200 text-slate-700 hover:border-brand-300')}>
                    <b className="mr-1">{letter(k)})</b> {it.options[k] ?? ''}
                  </button>
                ))}
              </div>
            </section>
          )
        })}
        <button type="button" onClick={() => void finish()} className="w-full rounded-xl bg-brand-600 py-3 font-medium text-white">
          Testni yakunlash
        </button>
      </main>
    </div>
  )
}
