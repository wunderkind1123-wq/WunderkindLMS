import { Component, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { isAxiosError } from 'axios'
// wunderkindedu.uz shriftlari — o'zimizning serverdan (CSP `font-src 'self'`), fayl faqat ishlatilganda yuklanadi
import '@fontsource/nunito/400.css'
import '@fontsource/nunito/600.css'
import '@fontsource/nunito/700.css'
import '@fontsource/nunito/800.css'
import '@fontsource/baloo-2/700.css'
import '@fontsource/baloo-2/800.css'
import { AlertTriangle, ArrowRight, CloudOff, Loader2, ChevronLeft, ChevronRight, CheckCircle2, Clock, Maximize } from 'lucide-react'
import {
  checkSession,
  AnswerNotSavedError,
  finishSession,
  getServerTime,
  letter,
  pingSession,
  monitoringError,
  saveSessionAnswer,
  sendSessionEvent,
  startSession,
  type IntegrityEvent,
  type MonitoringPeek,
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

const canFullscreen = () => typeof document !== 'undefined' && document.fullscreenEnabled === true
const isFullscreen = () => document.fullscreenElement != null

/** false — the browser refused (policy, embedded view): the test then goes on without fullscreen. */
async function enterFullscreen(): Promise<boolean> {
  if (!canFullscreen() || isFullscreen()) return true
  try {
    await document.documentElement.requestFullscreen({ navigationUI: 'hide' })
    return true
  } catch {
    return false
  }
}

/**
 * Ichki monitoring — o'quvchi shaxsiy kodi bilan (akkauntsiz, mijoz tanlovi 2026-11-06). Javob har
 * bosishda saqlanadi; vaqt tugasa test o'zi yakunlanadi. Topshirgach o'quvchi o'z foizini ko'radi, qaysi
 * savol xato ekanini — yo'q (yonidagiga ko'chirish yo'li bo'lardi; §2.7a). Ko'rinish — wunderkindedu.uz uslubi.
 *
 * Halollik (2026-11-24, curriculum-monitoring.md §2.3): test to'liq ekranda; boshqa tab/dasturga
 * o'tish va to'liq ekrandan chiqish serverga yuboriladi va sanaladi (chegaradan oshsa server testni
 * yakunlaydi); oyna fokusda bo'lmasa savollar xiralashadi (ekrandan o'qiydigan AI yordamchilar va
 * skrinshot uchun); nusxa olish, qo'yish, sichqonchaning o'ng tugmasi, chop etish bloklanadi va
 * urinishlar qayd qilinadi; ekranda o'quvchi ismi suv belgisi — rasmga olingan savol kimniki ekani bilinadi.
 */
export function MonitoringPublicPage() {
  return (
    <PageErrorBoundary>
      <MonitoringTestScreen />
    </PageErrorBoundary>
  )
}

/**
 * Kutilmagan xato (brauzer kengaytmasi, eski brauzer) oq ekran qoldirmasin: javoblar serverda va
 * kompyuterda saqlangan, sahifani yangilab davom etish mumkin.
 */
class PageErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <KidShell>
        <div className={cn(card, 'w-full max-w-sm p-7 text-center')}>
          <p className="text-5xl" aria-hidden>🛠️</p>
          <h1 className="wk-h mt-2 text-2xl font-extrabold">Sahifada xatolik</h1>
          <p className="mt-1 text-[#7A6055]">Javoblaringiz saqlangan. Sahifani yangilang va kodingizni qayta kiriting — test davom etadi.</p>
          <button type="button" onClick={() => window.location.reload()} className={cn(btnPrimary, 'mt-5 w-full py-3')}>
            Sahifani yangilash
          </button>
        </div>
      </KidShell>
    )
  }
}

function MonitoringTestScreen() {
  const [device] = useState(deviceId)
  const [code, setCode] = useState('')
  // doimiy kod bilan kirganda — doskadagi xona PIN'i (§2.9); bir martalik kodda bo'sh
  const [pin, setPin] = useState('')
  const [session, setSession] = useState<MonitoringSession | null>(null)
  const [answers, setAnswers] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  // server time − this PC's clock: lab PCs are often minutes off, the timer must not finish early
  const skew = useRef(0)
  const [checking, setChecking] = useState(false)
  const [away, setAway] = useState(false)
  const [warning, setWarning] = useState<string | null>(null)
  const [fullscreen, setFullscreen] = useState(() => typeof document !== 'undefined' && isFullscreen())
  // a refused fullscreen must not lock the pupil out — leaving the window is still counted
  const [fullscreenRefused, setFullscreenRefused] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [peek, setPeek] = useState<MonitoringPeek | null>(null)
  const [current, setCurrent] = useState(0)

  const active = session != null && !session.finished
  const activeRef = useRef(false)
  useEffect(() => {
    activeRef.current = active
  }, [active])
  // the subject being sat (a sitting code reaches several tests) — sent with every request
  const testIdRef = useRef<string | null>(null)
  useEffect(() => {
    testIdRef.current = session?.testId ?? null
  }, [session?.testId])
  const lastSent = useRef<Record<string, number>>({})
  const lastLeave = useRef(0)

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now() + skew.current), 1000)
    return () => clearInterval(t)
  }, [])

  const left = session ? Math.max(0, Math.floor((new Date(session.deadline).getTime() - now) / 1000)) : 0

  // ---------------------------------------------------------- javoblar navbati
  // Javob avval shu kompyuterda (localStorage) saqlanadi va serverga navbat bilan ketadi: Wi-Fi bir
  // lahzaga uzilsa ham bosilgan javob yo'qolmaydi, internet qaytgach o'zi yuboriladi.
  const pending = useRef<Map<number, string>>(new Map())
  const flushing = useRef(false)
  const [pendingCount, setPendingCount] = useState(0)
  const [offline, setOffline] = useState(false)

  const persist = useCallback(() => {
    setPendingCount(pending.current.size)
    try {
      const key = `wk-monitoring-q:${code}:${testIdRef.current ?? ''}`
      if (pending.current.size === 0) localStorage.removeItem(key)
      else localStorage.setItem(key, JSON.stringify(Object.fromEntries(pending.current)))
    } catch {
      // private mode — the queue still lives in memory
    }
  }, [code])

  /** Navbatni yuboradi; true — hammasi serverda. Tarmoq yo'q bo'lsa to'xtaydi, keyin qayta uriniladi. */
  const flush = useCallback(async (): Promise<boolean> => {
    if (flushing.current) return pending.current.size === 0
    flushing.current = true
    try {
      while (pending.current.size > 0) {
        for (const [order, choice] of [...pending.current]) {
          try {
            await saveSessionAnswer(code, device, order, choice, testIdRef.current, pin)
            if (pending.current.get(order) === choice) pending.current.delete(order)
            setOffline(false)
          } catch (e) {
            if ((isAxiosError(e) && !e.response) || e instanceof AnswerNotSavedError) {
              setOffline(true)
              return false
            }
            // the server refused it (time is up, the test was closed) — it cannot be saved any more
            pending.current.delete(order)
            setError(monitoringError(e, 'Javob saqlanmadi'))
          }
        }
      }
      return true
    } finally {
      flushing.current = false
      persist()
    }
  }, [code, device, pin, persist])

  const finish = useCallback(async (): Promise<void> => {
    await flush()
    try {
      const s = await finishSession(code, device, testIdRef.current, pin)
      pending.current = new Map()
      persist()
      setSession(s)
      // in a sitting the next subject follows — stay fullscreen
      if (!s.inSitting && isFullscreen()) void document.exitFullscreen().catch(() => undefined)
    } catch (e) {
      setError(monitoringError(e))
    }
  }, [code, device, pin, flush, persist])

  /** O'quvchi o'zi yakunlaydi: avval hamma javob serverga yetishi kerak. */
  async function finishManual(): Promise<void> {
    if (!(await flush())) {
      setError(`Internet yo‘q — ${pending.current.size} ta javob hali yuborilmagan. Internet qaytishini kuting, keyin yakunlang.`)
      return
    }
    await finish()
  }

  // tirikman (30 s) va navbatni qayta yuborish (4 s, internet qaytganda darhol)
  useEffect(() => {
    if (!active) return
    const ping = setInterval(() => {
      pingSession(code, device, testIdRef.current, pin)
        .then((r) => {
          setOffline(false)
          if (r.finished) void finish()
        })
        .catch((e) => {
          if (isAxiosError(e) && !e.response) setOffline(true)
        })
    }, 30_000)
    const retry = setInterval(() => {
      if (pending.current.size > 0) void flush()
    }, 4_000)
    const onOnline = () => void flush()
    window.addEventListener('online', onOnline)
    return () => {
      clearInterval(ping)
      clearInterval(retry)
      window.removeEventListener('online', onOnline)
    }
  }, [active, code, device, pin, finish, flush])

  // time is up: the attempt is finished (the server would refuse further answers anyway)
  const deadline = session && !session.finished ? session.deadline : null
  useEffect(() => {
    if (!deadline) return
    const t = setTimeout(() => void finish(), Math.max(0, new Date(deadline).getTime() - (Date.now() + skew.current)))
    return () => clearTimeout(t)
  }, [deadline, finish])

  /** Hodisani serverga yuboradi (bir turdagisi 1,5 s ichida bir marta). Server test tugaganini aytsa — yakun ekrani. */
  const report = useCallback(
    async (kind: IntegrityEvent): Promise<void> => {
      if (!activeRef.current) return
      const at = Date.now()
      const isLeave = kind === 'hidden' || kind === 'blur' || kind === 'fullscreen'
      if (isLeave) {
        // one trip away fires blur + hidden + fullscreen together — count it once
        if (at - lastLeave.current < 1500) return
        lastLeave.current = at
      } else {
        if (at - (lastSent.current[kind] ?? 0) < 1500) return
        lastSent.current[kind] = at
      }
      try {
        const r = await sendSessionEvent(code, device, kind, testIdRef.current, pin)
        if (r.finished) {
          await finish()
          return
        }
        if (isLeave) {
          setSession((s) => (s ? { ...s, leaves: r.leaves } : s))
          setWarning(
            r.maxLeaves == null
              ? `Siz test oynasidan chiqdingiz (${r.leaves}-marta). Bu qayd qilindi va o'qituvchingiz ko'radi.`
              : `Siz test oynasidan chiqdingiz: ${r.leaves} / ${r.maxLeaves}. Yana ${r.maxLeaves - r.leaves + 1} marta chiqsangiz, test yakunlanadi.`,
          )
        }
      } catch {
        // offline — the next event or answer will surface the error
      }
    },
    [code, device, pin, finish],
  )

  // ------------------------------------------------------------ integrity listeners
  useEffect(() => {
    if (!active) return
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') {
        setAway(true)
        void report('hidden')
      }
    }
    const onBlur = () => {
      setAway(true)
      void report('blur')
    }
    const onFocus = () => setAway(false)
    const onFullscreen = () => {
      const fs = isFullscreen()
      setFullscreen(fs)
      if (!fs && canFullscreen()) void report('fullscreen')
    }
    const block = (e: Event) => e.preventDefault()
    const onCopy = (e: Event) => {
      e.preventDefault()
      void report('copy')
    }
    const onPaste = (e: Event) => {
      e.preventDefault()
      void report('paste')
    }
    const onKey = (e: KeyboardEvent) => {
      const k = e.key.toLowerCase()
      const mod = e.ctrlKey || e.metaKey
      if (e.key === 'F12' || (mod && e.shiftKey && ['i', 'j', 'c'].includes(k)) || (mod && k === 'u')) {
        e.preventDefault()
        void report('devtools')
      } else if (mod && (k === 'p' || k === 's')) {
        e.preventDefault()
        void report('print')
      } else if (mod && ['c', 'x', 'a'].includes(k)) {
        e.preventDefault()
        void report('copy')
      } else if (mod && k === 'v') {
        e.preventDefault()
        void report('paste')
      }
    }
    const onKeyUp = (e: KeyboardEvent) => {
      if (e.key === 'PrintScreen') {
        void navigator.clipboard?.writeText('').catch(() => undefined)
        void report('print')
      }
    }
    const onPrint = () => void report('print')

    document.addEventListener('visibilitychange', onVisibility)
    window.addEventListener('blur', onBlur)
    window.addEventListener('focus', onFocus)
    document.addEventListener('fullscreenchange', onFullscreen)
    document.addEventListener('copy', onCopy)
    document.addEventListener('cut', onCopy)
    document.addEventListener('paste', onPaste)
    document.addEventListener('contextmenu', block)
    document.addEventListener('dragstart', block)
    document.addEventListener('selectstart', block)
    document.addEventListener('keydown', onKey)
    document.addEventListener('keyup', onKeyUp)
    window.addEventListener('beforeprint', onPrint)
    return () => {
      document.removeEventListener('visibilitychange', onVisibility)
      window.removeEventListener('blur', onBlur)
      window.removeEventListener('focus', onFocus)
      document.removeEventListener('fullscreenchange', onFullscreen)
      document.removeEventListener('copy', onCopy)
      document.removeEventListener('cut', onCopy)
      document.removeEventListener('paste', onPaste)
      document.removeEventListener('contextmenu', block)
      document.removeEventListener('dragstart', block)
      document.removeEventListener('selectstart', block)
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('keyup', onKeyUp)
      window.removeEventListener('beforeprint', onPrint)
    }
  }, [active, report])

  /** 1-qadam: kod kimniki va qaysi test — hech narsa boshlanmaydi. */
  async function check(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      setPeek(await checkSession(code, device, pin))
    } catch (e) {
      setError(monitoringError(e, 'Kod tekshirilmadi'))
    } finally {
      setBusy(false)
    }
  }

  /** 2-qadam: o'quvchi "Bu men" deb boshlaydi. */
  // klaviatura: ← → savollar orasida, A–F javob (ekrandagi harf)
  useEffect(() => {
    if (!active || !session || confirming || warning) return
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return
      const it = session.items[current]
      if (e.key === 'ArrowRight') setCurrent((c) => Math.min(c + 1, session.items.length - 1))
      else if (e.key === 'ArrowLeft') setCurrent((c) => Math.max(c - 1, 0))
      else if (it && /^[a-f]$/i.test(e.key)) {
        const k = e.key.toUpperCase().charCodeAt(0) - 65
        const key = (it.keys ?? [])[k] ?? (k < it.optionCount ? letter(k) : null)
        if (key) choose(it.order, key)
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  })

  async function start(): Promise<void> {
    setBusy(true)
    setError(null)
    // fullscreen needs this click's user gesture — ask before the network call
    setFullscreenRefused(!(await enterFullscreen()))
    setFullscreen(isFullscreen())
    try {
      const s = await startSession(code, device, peek?.testId, pin)
      if (s.serverNow) {
        skew.current = new Date(s.serverNow).getTime() - Date.now()
        setNow(Date.now() + skew.current)
      }
      setSession(s)
      testIdRef.current = s.testId
      // answers this computer kept while offline (e.g. the page was reloaded) go on top and are re-sent
      let restored = s.answers
      try {
        const saved = JSON.parse(localStorage.getItem(`wk-monitoring-q:${code}:${s.testId ?? ''}`) ?? '{}') as Record<string, string>
        pending.current = s.finished ? new Map() : new Map(Object.entries(saved).map(([k, v]) => [Number(k), v]))
      } catch {
        pending.current = new Map()
      }
      if (pending.current.size > 0) {
        const chars = restored.split('')
        pending.current.forEach((v, k) => { chars[k - 1] = v })
        restored = chars.join('')
      }
      setAnswers(restored)
      persist()
      if (pending.current.size > 0) void flush()
      // resume at the first unanswered question
      const firstOpen = s.items.findIndex((it) => (s.answers[it.order - 1] ?? '-') === '-')
      setCurrent(firstOpen < 0 ? 0 : firstOpen)
      if (s.finished && isFullscreen()) void document.exitFullscreen().catch(() => undefined)
    } catch (e) {
      if (isFullscreen()) void document.exitFullscreen().catch(() => undefined)
      setError(monitoringError(e, 'Kirib bo‘lmadi'))
    } finally {
      setBusy(false)
    }
  }

  function choose(order: number, choice: string): void {
    const chars = answers.padEnd(order, '-').split('')
    chars[order - 1] = chars[order - 1] === choice ? '-' : choice
    setAnswers(chars.join(''))
    pending.current.set(order, chars[order - 1])
    persist()
    void flush()
  }

  /** Sessiya: fan tugadi — navbatdagisini ko'rsatadi (yoki "hammasi topshirildi"). */
  async function nextSubject(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const p = await checkSession(code, device, pin)
      setPeek(p)
      setSession(null)
      pending.current = new Map()
      setPendingCount(0)
      setAnswers('')
      setCurrent(0)
      if (p.allDone && isFullscreen()) void document.exitFullscreen().catch(() => undefined)
    } catch (e) {
      setError(monitoringError(e, 'Keyingi fan ochilmadi'))
    } finally {
      setBusy(false)
    }
  }

  async function backToTest(): Promise<void> {
    setWarning(null)
    setAway(false)
    setFullscreenRefused(!(await enterFullscreen()))
    setFullscreen(isFullscreen())
  }

  if (!session && peek?.allDone) {
    return (
      <KidShell>
        <div className={cn(card, 'w-full max-w-md p-7 text-center')}>
          <p className="text-5xl" aria-hidden>🎉</p>
          <h1 className="wk-h mt-2 text-3xl font-extrabold">Barcha fanlar topshirildi!</h1>
          <p className="mt-1 text-[#7A6055]">{peek.studentName}, javoblaringiz saqlandi. Rahmat!</p>
          {peek.steps && <Steps steps={peek.steps} />}
        </div>
      </KidShell>
    )
  }

  if (!session && peek) {
    const sitting = peek.steps != null
    const anyDone = peek.steps?.some((x) => x.status === 'done') ?? false
    return (
      <KidShell>
        <div className={cn(card, 'w-full max-w-md p-6 sm:p-7')}>
          <div className="flex items-center gap-3">
            <span className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-[#FFE5D0] text-3xl" aria-hidden>🧑‍🎓</span>
            <div className="min-w-0">
              <p className="text-xs font-bold uppercase tracking-wide text-[#FF7B54]">Siz</p>
              <p className="wk-h text-xl font-extrabold leading-tight">{peek.studentName}</p>
              <p className="text-sm font-semibold text-[#7A6055]">{peek.ownerName}</p>
            </div>
          </div>
          {sitting && (
            <div className="mt-5">
              <p className="text-xs font-bold uppercase tracking-wide text-[#7A6055]">{peek.sittingTitle}</p>
              <Steps steps={peek.steps!} />
            </div>
          )}
          <div className="mt-5 rounded-2xl bg-[#FFF8F2] p-3 text-sm">
            <p className="px-1 font-bold">{peek.title}</p>
            <div className="mt-2 grid grid-cols-3 gap-2 text-center">
              <Tile tint="#D4EEFF" emoji="📘" label="Fan" value={peek.subjectName} />
              <Tile tint="#D6F5E3" emoji="📝" label="Savollar" value={`${peek.itemCount} ta`} />
              <Tile tint="#E8DEFF" emoji="⏱️" label={peek.inProgress ? 'Qolgan' : 'Vaqt'} value={`${peek.timeLimitMin} daq`} />
            </div>
          </div>
          <ul className="mt-4 space-y-1.5 text-sm text-[#7A6055]">
            <li>🖥️ Test to‘liq ekranda ochiladi, vaqt «Boshlash» bosilganda boshlanadi.</li>
            <li>🚪 Boshqa oyna, tab yoki dasturga o‘tish qayd qilinadi.{peek.maxLeaves != null && ` ${peek.maxLeaves} martadan ko‘p chiqsangiz, test o‘zi yakunlanadi.`}</li>
            <li>💾 Javob bosilgan zahoti saqlanadi.</li>
          </ul>
          {error && <p className="mt-3 rounded-2xl bg-[#FFD6E0] px-3 py-2 text-sm font-semibold text-[#B4233C]">{error}</p>}
          <button type="button" disabled={busy} onClick={() => void start()} className={cn(btnPrimary, 'mt-5 w-full py-3.5 text-lg')}>
            {busy ? 'Ochilmoqda…' : peek.inProgress ? 'Testni davom ettirish ▶' : anyDone ? `«${peek.subjectName}» ni boshlash 🚀` : 'Ha, bu men — boshlash 🚀'}
          </button>
          {!anyDone && (
            <button type="button" disabled={busy} onClick={() => { setPeek(null); setCode(''); setError(null) }}
              className="mt-2 w-full rounded-full py-2.5 text-sm font-bold text-[#7A6055] hover:bg-[#FFE5D0]">
              Bu men emasman
            </button>
          )}
        </div>
      </KidShell>
    )
  }

  if (!session) {
    return (
      <KidShell>
        <div className="flex w-full max-w-md flex-col items-center">
          <span className="rounded-full border-2 border-[#FFE08A] bg-[#FFF3C4] px-4 py-1 text-sm font-bold text-[#8A6A00]">🏫 Wunderkind Xalqaro Maktabi</span>
          <h1 className="wk-h mt-4 text-center text-4xl font-extrabold leading-tight sm:text-5xl">
            Salom! <span className="inline-block rounded-2xl bg-[#FF7B54] px-3 text-white">Testga</span>{' '}
            <span className="inline-block rounded-2xl bg-[#4A9EFF] px-3 text-white">xush kelibsiz</span> 🎉
          </h1>
          <p className="mt-3 text-center text-[#7A6055]">O‘qituvchingiz bergan shaxsiy kodni kiriting</p>
          <div className={cn(card, 'mt-6 w-full p-6')}>
            <input
              className="w-full rounded-2xl border-2 border-[#FFCBA4] bg-[#FFF8F2] px-4 py-3.5 text-center font-mono text-2xl font-bold uppercase tracking-[0.3em] text-[#3D2C1E] outline-none placeholder:text-[#FFCBA4] focus:border-[#FF7B54]"
              value={code}
              maxLength={10}
              autoFocus
              placeholder="XXXXXXXX"
              onChange={(e) => setCode(e.target.value.toUpperCase())}
              onKeyDown={(e) => e.key === 'Enter' && code.trim().length >= 8 && void check()}
            />
            <label className="mt-4 block">
              <span className="text-xs font-bold uppercase tracking-wide text-[#7A6055]">Xona PIN — doskadagi 4 raqam</span>
              <input
                className="mt-1 w-full rounded-2xl border-2 border-[#D4EEFF] bg-[#F4FAFF] px-4 py-3 text-center font-mono text-2xl font-bold tracking-[0.5em] text-[#3D2C1E] outline-none placeholder:text-[#B9DCFF] focus:border-[#4A9EFF]"
                value={pin}
                inputMode="numeric"
                maxLength={4}
                placeholder="••••"
                onChange={(e) => setPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
                onKeyDown={(e) => e.key === 'Enter' && code.trim().length >= 8 && void check()}
              />
            </label>
            <p className="mt-1 text-center text-xs text-[#7A6055]">Bir martalik kartochka kodi bo‘lsa, PIN shart emas</p>
            {error && <p className="mt-3 rounded-2xl bg-[#FFD6E0] px-3 py-2 text-sm font-semibold text-[#B4233C]">{error}</p>}
            <button type="button" disabled={busy || code.trim().length < 8} onClick={() => void check()} className={cn(btnPrimary, 'mt-4 w-full py-3.5 text-lg')}>
              {busy ? 'Tekshirilmoqda…' : 'Davom etish 🚀'}
            </button>
            <button type="button" onClick={() => setChecking(!checking)} className="mt-3 w-full rounded-full py-2 text-sm font-bold text-[#7A6055] hover:bg-[#FFE5D0]">
              {checking ? 'Tekshiruvni yopish' : '🖥️ Kompyuterni tekshirish'}
            </button>
            {checking && <PcCheck />}
          </div>
          <div className="mt-5 grid w-full gap-2 sm:grid-cols-2">
            <Rule tint="#FFE5D0" emoji="🖥️" text="Test to‘liq ekranda ochiladi" />
            <Rule tint="#D4EEFF" emoji="🚪" text="Boshqa oynaga o‘tish qayd qilinadi" />
            <Rule tint="#D6F5E3" emoji="🔒" text="Nusxa olish va skrinshot yopiq" />
            <Rule tint="#E8DEFF" emoji="🔀" text="Har kimda savollar tartibi boshqacha" />
          </div>
        </div>
      </KidShell>
    )
  }

  if (session.finished) {
    const stopped = session.finishReason === 'leaves'
    return (
      <KidShell>
        <div className={cn(card, 'w-full max-w-md p-7 text-center')}>
          <p className="text-5xl" aria-hidden>{stopped ? '⚠️' : '🎉'}</p>
          <h1 className="wk-h mt-2 text-3xl font-extrabold">{stopped ? 'Test to‘xtatildi' : 'Test topshirildi!'}</h1>
          <p className="mt-1 text-[#7A6055]">
            {stopped
              ? `${session.studentName}, siz test oynasidan ruxsat etilganidan ko‘p chiqdingiz. Shu paytgacha belgilagan javoblaringiz saqlandi. O‘qituvchingizga murojaat qiling.`
              : session.inSitting
                ? `«${session.subjectName}» bo‘yicha javoblaringiz saqlandi.`
                : `${session.studentName}, javoblaringiz saqlandi. Rahmat!`}
          </p>
          {session.resultPercent != null && (
            <Score percent={session.resultPercent} correct={session.resultCorrect ?? 0} total={session.items.length} />
          )}
          {session.inSitting && (
            <>
              {error && <p className="mt-3 rounded-2xl bg-[#FFD6E0] px-3 py-2 text-sm font-semibold text-[#B4233C]">{error}</p>}
              <button type="button" disabled={busy} onClick={() => void nextSubject()} className={cn(btnPrimary, 'mt-6 inline-flex w-full items-center justify-center gap-2 py-3.5 text-lg')}>
                {busy ? 'Ochilmoqda…' : <>Keyingi fanga o‘tish <ArrowRight className="h-5 w-5" /></>}
              </button>
            </>
          )}
        </div>
      </KidShell>
    )
  }

  const answered = answers.replace(/-/g, '').length
  const mm = String(Math.floor(left / 60)).padStart(2, '0')
  const ss = String(left % 60).padStart(2, '0')
  const needFullscreen = canFullscreen() && !fullscreen && !fullscreenRefused
  const watermark = `${session.studentName} · ${code}`
  const total = session.items.length
  const item = session.items[Math.min(current, total - 1)]

  return (
    <div className="wk-kid min-h-screen select-none" style={{ WebkitUserSelect: 'none', WebkitTouchCallout: 'none' }}>
      <style>{KID_CSS}</style>
      {/* suv belgisi: ekran rasmga olinsa, kimniki ekani ko'rinadi */}
      <div aria-hidden className="pointer-events-none fixed inset-0 z-0 overflow-hidden opacity-[0.06] print:hidden">
        <div className="absolute -inset-1/2 flex rotate-[-24deg] flex-wrap content-start gap-x-16 gap-y-20 text-lg font-bold text-[#3D2C1E]">
          {Array.from({ length: 120 }, (_, i) => <span key={i} className="whitespace-nowrap">{watermark}</span>)}
        </div>
      </div>

      <header className="sticky top-0 z-10 border-b border-[#FFE5D0] bg-[#FFF8F2]/95 px-4 py-3 print:hidden">
        <div className="mx-auto flex max-w-3xl items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-3">
            <Logo small />
            <div className="min-w-0">
              <p className="wk-h truncate text-lg font-extrabold leading-tight">{session.subjectName || session.title}</p>
              <p className="truncate text-xs font-semibold text-[#7A6055]">
                {session.studentName} · {answered}/{session.items.length} javob
                {session.leaves > 0 && <span className="ml-1 text-[#E0632F]">· oynadan chiqish: {session.leaves}{session.maxLeaves != null && ` / ${session.maxLeaves}`}</span>}
              </p>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <SaveState offline={offline} pending={pendingCount} />
            <span className={cn('inline-flex items-center gap-1 rounded-full px-3.5 py-1.5 font-mono text-base font-bold tabular-nums',
              left < 300 ? 'bg-[#FF7B54] text-white shadow-[0_4px_12px_rgba(255,123,84,0.35)]' : 'bg-[#FFE5D0] text-[#3D2C1E]')}>
              <Clock className="h-4 w-4" /> {mm}:{ss}
            </span>
          </div>
        </div>
      </header>

      <p className="hidden p-8 text-center print:block">Testni chop etib bo‘lmaydi.</p>

      <main className={cn('relative z-[1] mx-auto max-w-3xl space-y-4 p-4 transition-[filter] print:hidden', (away || needFullscreen || warning) && 'blur-md')}>
        {error && <p className="rounded-2xl bg-[#FFD6E0] px-4 py-2 text-sm font-semibold text-[#B4233C]">{error}</p>}
        {item && (() => {
          const chosen = answers[item.order - 1]
          const keys = item.keys ?? Array.from({ length: item.optionCount }, (_, k) => letter(k))
          const hasImages = item.optionImages?.some((x) => x) ?? false
          return (
            <section className={cn(card, 'p-5 sm:p-7')}>
              <span className="inline-block rounded-full bg-[#FFE5D0] px-3 py-1 text-sm font-extrabold text-[#E0632F]">{current + 1}-savol / {total}</span>
              <p className="mt-3 whitespace-pre-wrap text-xl font-semibold leading-relaxed">{item.text}</p>
              {item.imageUrl && <img src={item.imageUrl} alt="" draggable={false} className="mt-3 max-h-72 rounded-2xl" />}
              <div className={cn('mt-5 grid gap-3', hasImages ? 'grid-cols-2' : 'sm:grid-cols-2')}>
                {keys.map((key, k) => {
                  const on = chosen === key
                  return (
                    <button key={key} type="button" onClick={() => choose(item.order, key)}
                      className={cn('flex items-start gap-3 rounded-[18px] border-2 px-4 py-3.5 text-left text-lg transition outline-none focus-visible:ring-4 focus-visible:ring-[#FFCBA4]',
                        on ? 'border-[#FF7B54] bg-[#FFE5D0] font-bold shadow-[0_4px_14px_rgba(255,123,84,0.25)]' : 'border-[#F1E4DA] bg-white hover:-translate-y-0.5 hover:border-[#FFCBA4]')}>
                      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-base font-extrabold"
                        style={on ? { background: '#FF7B54', color: '#fff' } : { background: OPTION_TINTS[k % OPTION_TINTS.length], color: '#3D2C1E' }}>
                        {letter(k)}
                      </span>
                      <span className="min-w-0 pt-1">
                        {item.options[k] ?? ''}
                        {item.optionImages?.[k] && <img src={item.optionImages[k]} alt="" draggable={false} className="mt-1.5 max-h-40 rounded-xl" />}
                      </span>
                    </button>
                  )
                })}
              </div>
              <div className="mt-6 flex items-center justify-between gap-2">
                <button type="button" disabled={current === 0} onClick={() => setCurrent(current - 1)}
                  className={cn(btnGhost, 'inline-flex items-center gap-1 px-5 py-2.5 disabled:opacity-40')}>
                  <ChevronLeft className="h-5 w-5" /> Oldingi
                </button>
                {current < total - 1 ? (
                  <button type="button" onClick={() => setCurrent(current + 1)} className={cn(btnPrimary, 'inline-flex items-center gap-1 px-6 py-2.5')}>
                    Keyingi <ChevronRight className="h-5 w-5" />
                  </button>
                ) : (
                  <button type="button" onClick={() => setConfirming(true)}
                    className="rounded-full bg-[#3DBE7A] px-6 py-2.5 font-bold text-white shadow-[0_4px_16px_rgba(61,190,122,0.35)]">
                    Testni yakunlash ✓
                  </button>
                )}
              </div>
            </section>
          )
        })()}

        {/* savollar paneli: javob berilgani to'ldirilgan, joriysi ramkada */}
        <nav aria-label="Savollar" className={cn(card, 'p-4')}>
          <div className="flex flex-wrap gap-2">
            {session.items.map((it, n) => {
              const done = (answers[it.order - 1] ?? '-') !== '-'
              return (
                <button key={it.order} type="button" onClick={() => setCurrent(n)} aria-current={n === current ? 'step' : undefined}
                  className={cn('h-10 w-10 rounded-xl text-sm font-extrabold tabular-nums transition',
                    done ? 'bg-[#FF7B54] text-white' : 'bg-[#FFF8F2] text-[#7A6055] hover:bg-[#FFE5D0]',
                    n === current && 'ring-[3px] ring-[#4A9EFF] ring-offset-2')}>
                  {n + 1}
                </button>
              )
            })}
          </div>
          <div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-xs font-semibold text-[#7A6055]">
            <span>{answered} / {total} javob berildi · klaviatura: ← → va A, B, C, D</span>
            <button type="button" onClick={() => setConfirming(true)} className="font-bold text-[#FF7B54] hover:underline">Testni yakunlash</button>
          </div>
        </nav>
      </main>

      {left === 0 && (
        <Overlay>
          <p className="text-5xl" aria-hidden>⏰</p>
          <h2 className="wk-h mt-2 text-2xl font-extrabold">Vaqt tugadi</h2>
          <p className="mt-1 text-[#7A6055]">
            {pendingCount > 0 ? `${pendingCount} ta javob yuborilmoqda — sahifani yopmang.` : 'Javoblaringiz saqlandi. Natija tayyorlanmoqda…'}
          </p>
        </Overlay>
      )}

      {/* window.confirm would blur the window and count as leaving — an in-page dialog instead */}
      {confirming && (
        <Overlay>
          <p className="text-5xl" aria-hidden>🏁</p>
          <h2 className="wk-h mt-2 text-2xl font-extrabold">Testni yakunlaysizmi?</h2>
          <p className="mt-1 text-[#7A6055]">
            {answered}/{session.items.length} savolga javob berdingiz. Yakunlagandan keyin javoblarni o‘zgartirib bo‘lmaydi.
          </p>
          {answered < session.items.length && (
            <p className="mt-3 rounded-2xl bg-[#FFF3C4] px-3 py-2 text-sm font-bold text-[#8A6A00]">
              ⚠️ {session.items.length - answered} ta savol javobsiz qoldi
            </p>
          )}
          <div className="mt-5 flex gap-2">
            <button type="button" onClick={() => setConfirming(false)} className={cn(btnGhost, 'flex-1 py-3')}>Ortga</button>
            <button type="button" onClick={() => { setConfirming(false); void finishManual() }} className={cn(btnPrimary, 'flex-1 py-3')}>Yakunlash</button>
          </div>
        </Overlay>
      )}

      {!confirming && (warning || needFullscreen) && (
        <Overlay>
          <p className="text-5xl" aria-hidden>🙈</p>
          <p className="mt-3 font-semibold">
            {warning ?? 'Test faqat to‘liq ekranda davom etadi.'}
          </p>
          <button type="button" onClick={() => void backToTest()} className={cn(btnPrimary, 'mt-5 inline-flex w-full items-center justify-center gap-2 py-3')}>
            <Maximize className="h-5 w-5" /> Testga qaytish
          </button>
        </Overlay>
      )}
    </div>
  )
}

type CheckRow = { label: string; ok: boolean | null; note: string }

/**
 * Laboratoriya kompyuterini testdan oldin tekshirish (§2.8): brauzer, xotira, to'liq ekran,
 * serverga aloqa va tezlik, kompyuter soati, ekran o'lchami. Hech narsa yozilmaydi.
 */
function PcCheck() {
  const [rows, setRows] = useState<CheckRow[] | null>(null)

  useEffect(() => {
    let alive = true
    void (async () => {
      const list: CheckRow[] = []
      const chrome = /(?:Chrome|CriOS)\/(\d+)/.exec(navigator.userAgent)
      const version = chrome ? Number(chrome[1]) : null
      list.push(version == null
        ? { label: 'Brauzer', ok: null, note: 'Chrome tavsiya etiladi' }
        : { label: 'Brauzer', ok: version >= 109, note: version >= 109 ? `Chrome ${version}` : `Chrome ${version} — eski, yangilang` })
      const storage = (() => {
        try {
          localStorage.setItem('wk-check', '1')
          const ok = localStorage.getItem('wk-check') === '1'
          localStorage.removeItem('wk-check')
          return ok
        } catch {
          return false
        }
      })()
      list.push({ label: 'Brauzer xotirasi', ok: storage, note: storage ? 'ishlaydi' : 'yopiq (inkognito?) — sahifa yangilansa test qulflanadi' })
      list.push({ label: 'To‘liq ekran', ok: document.fullscreenEnabled === true ? true : null, note: document.fullscreenEnabled ? 'ishlaydi' : 'yo‘q — test baribir ishlaydi' })
      try {
        const { serverNow, roundTripMs } = await getServerTime()
        list.push({ label: 'Serverga aloqa', ok: roundTripMs < 1500, note: `${roundTripMs} ms${roundTripMs >= 1500 ? ' — internet sekin' : ''}` })
        const off = Math.round((serverNow - Date.now()) / 60_000)
        list.push({ label: 'Kompyuter soati', ok: Math.abs(off) < 2 ? true : null,
          note: Math.abs(off) < 2 ? 'to‘g‘ri' : `${Math.abs(off)} daqiqa ${off > 0 ? 'orqada' : 'oldinda'} — test to‘g‘ri vaqtni ishlatadi, lekin soatni to‘g‘rilang` })
      } catch {
        list.push({ label: 'Serverga aloqa', ok: false, note: 'server javob bermadi — internetni tekshiring' })
      }
      const wide = window.screen.width >= 1024
      list.push({ label: 'Ekran', ok: wide ? true : null, note: `${window.screen.width}×${window.screen.height}${wide ? '' : ' — kichik'}` })
      if (alive) setRows(list)
    })()
    return () => { alive = false }
  }, [])

  if (!rows) return <p className="mt-2 text-center text-xs font-semibold text-[#7A6055]">Tekshirilmoqda…</p>
  const bad = rows.some((r) => r.ok === false)
  return (
    <div className="mt-2 rounded-2xl border-2 border-[#FFE5D0] bg-[#FFF8F2] p-3 text-xs">
      <ul className="space-y-1">
        {rows.map((r) => (
          <li key={r.label} className="flex items-start gap-2">
            {r.ok === true ? <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-500" />
              : r.ok === false ? <AlertTriangle className="h-4 w-4 shrink-0 text-red-500" />
                : <AlertTriangle className="h-4 w-4 shrink-0 text-amber-500" />}
            <span><b>{r.label}:</b> <span className="text-[#7A6055]">{r.note}</span></span>
          </li>
        ))}
      </ul>
      <p className={cn('mt-2 font-bold', bad ? 'text-[#B4233C]' : 'text-[#23874F]')}>
        {bad ? 'Bu kompyuterda muammo bor — o‘qituvchiga ayting' : 'Kompyuter testga tayyor'}
      </p>
    </div>
  )
}

/** Javoblar holati: hammasi saqlangan / yuborilmoqda / internet yo'q (kompyuterda saqlanmoqda). */
function SaveState({ offline, pending }: { offline: boolean; pending: number }) {
  if (offline)
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-[#FFD6E0] px-3 py-1 text-xs font-bold text-[#B4233C]" role="status">
        <CloudOff className="h-4 w-4" /> Internet yo‘q{pending > 0 && ` · ${pending} javob kompyuterda saqlanmoqda`}
      </span>
    )
  if (pending > 0)
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-[#FFF3C4] px-3 py-1 text-xs font-bold text-[#8A6A00]" role="status">
        <Loader2 className="h-3.5 w-3.5 animate-spin" /> Saqlanmoqda…
      </span>
    )
  return (
    <span className="hidden items-center gap-1 rounded-full bg-[#D6F5E3] px-3 py-1 text-xs font-bold text-[#23874F] sm:inline-flex" role="status">
      <CheckCircle2 className="h-3.5 w-3.5" /> Saqlandi
    </span>
  )
}

/** Natija: foiz va to'g'ri javoblar soni. Qaysi savol xato bo'lgani ko'rsatilmaydi (server ham bermaydi). */
function Score({ percent, correct, total }: { percent: number; correct: number; total: number }) {
  const value = Math.round(percent)
  const [stroke, note] = value >= 86 ? ['#3DBE7A', 'Ajoyib natija! 🌟']
    : value >= 71 ? ['#4A9EFF', 'Yaxshi natija! 👏']
      : value >= 56 ? ['#F5A524', 'Yomon emas — yana harakat qiling 💪']
        : ['#FF7B54', 'Keyingi safar albatta uddalaysiz 🌱']
  const r = 42
  const c = 2 * Math.PI * r
  return (
    <div className="mt-6">
      <div className="relative mx-auto h-36 w-36">
        <svg viewBox="0 0 100 100" className="h-full w-full -rotate-90">
          <circle cx="50" cy="50" r={r} fill="none" stroke="#FFE5D0" strokeWidth="10" />
          <circle cx="50" cy="50" r={r} fill="none" stroke={stroke} strokeWidth="10" strokeLinecap="round"
            strokeDasharray={c} strokeDashoffset={c * (1 - Math.min(100, Math.max(0, value)) / 100)} />
        </svg>
        <span className="wk-h absolute inset-0 flex items-center justify-center text-4xl font-extrabold tabular-nums" style={{ color: stroke }}>{value}%</span>
      </div>
      <p className="mt-2 font-bold">{correct} / {total} to‘g‘ri javob</p>
      <p className="mt-1 text-sm font-semibold text-[#7A6055]">{note}</p>
    </div>
  )
}

/** Sessiyadagi fanlar: ✓ topshirildi (foiz bilan), → hozirgisi, ○ keyingilari. */
function Steps({ steps }: { steps: NonNullable<MonitoringPeek['steps']> }) {
  return (
    <ol className="mt-2 space-y-2 text-left">
      {steps.map((x, i) => (
        <li key={x.testId} className={cn('flex items-center gap-3 rounded-2xl border-2 px-3 py-2.5',
          x.status === 'current' ? 'border-[#FF7B54] bg-[#FFE5D0] font-bold'
            : x.status === 'done' ? 'border-[#BFEBD2] bg-[#D6F5E3]' : 'border-[#F1E4DA] bg-white text-[#7A6055]')}>
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-white text-sm font-extrabold">
            {x.status === 'done' ? '✓' : x.status === 'current' ? '▶' : i + 1}
          </span>
          <span className="min-w-0 flex-1 truncate">{x.subjectName || x.title}</span>
          {x.status === 'done' && (
            <span className="wk-h text-lg font-extrabold tabular-nums text-[#23874F]">{x.percent != null ? `${Math.round(x.percent)}%` : '✓'}</span>
          )}
          {x.status === 'absent' && <span className="text-xs font-bold text-[#7A6055]">yopilgan</span>}
        </li>
      ))}
    </ol>
  )
}

function Overlay({ children }: { children: React.ReactNode }) {
  return (
    <div className="fixed inset-0 z-20 flex items-center justify-center bg-[#3D2C1E]/40 p-4 print:hidden">
      <div role="dialog" aria-modal className="w-full max-w-sm rounded-[26px] bg-white p-7 text-center text-[#3D2C1E] shadow-[0_16px_48px_rgba(61,44,30,0.25)]">{children}</div>
    </div>
  )
}

// ================================================================== wunderkindedu.uz uslubi (2026-11-25)
// Maktabning test portali ko'rinishi: iliq krem fon, marjon (coral) asosiy rang, pastel kartochkalar,
// yumaloq "tabletka" tugmalar, Baloo 2 sarlavhalar / Nunito matn. Fon dog'lari radial-gradient — `filter: blur`
// kuchsiz laboratoriya kompyuterida sahifani sekinlatardi. Faqat o'quvchi sahifasi —
// o'quv bo'limi paneli LMS ko'rinishida qoladi. Shrift serverdan (CSP `font-src 'self'`), bo'lmasa
// tizim shrifti.

const KID_CSS = `
.wk-kid{font-family:'Nunito','Segoe UI',system-ui,sans-serif;background:#FFF8F2;color:#3D2C1E}
.wk-kid .wk-h{font-family:'Baloo 2','Nunito','Segoe UI',system-ui,sans-serif;letter-spacing:-.01em}
.wk-blob{position:fixed;border-radius:9999px;opacity:.75;pointer-events:none;z-index:0}
`

const card = 'rounded-[22px] bg-white shadow-[0_4px_18px_rgba(0,0,0,0.05)]'
const btnPrimary =
  'rounded-full bg-[#FF7B54] font-bold text-white shadow-[0_4px_16px_rgba(255,123,84,0.35)] transition hover:-translate-y-0.5 hover:shadow-[0_8px_22px_rgba(255,123,84,0.45)] disabled:translate-y-0 disabled:opacity-50 disabled:shadow-none'
const btnGhost = 'rounded-full border-2 border-[#FFCBA4] bg-white font-bold text-[#7A6055] transition hover:bg-[#FFE5D0]'
/** Variant harfi pufakchalari — sayt kartochkalari ranglari. */
const OPTION_TINTS = ['#FFE5D0', '#D4EEFF', '#D6F5E3', '#E8DEFF', '#FFF3C4', '#FFD6E0']

function Logo({ small = false }: { small?: boolean }) {
  return (
    <span className="inline-flex items-center gap-2">
      <span className={cn('flex shrink-0 items-center justify-center rounded-[14px] bg-[#FF7B54] shadow-[0_4px_12px_rgba(255,123,84,0.3)]',
        small ? 'h-10 w-10 text-xl' : 'h-12 w-12 text-2xl')} aria-hidden>🎓</span>
      {!small && <span className="wk-h text-2xl font-extrabold">Wunder<span className="text-[#FF7B54]">kind</span></span>}
    </span>
  )
}

/** Sahifa qobig'i: krem fon, pastel dog'lar, logotip va markazdagi mazmun. */
function KidShell({ children }: { children: ReactNode }) {
  return (
    <div className="wk-kid relative min-h-screen overflow-hidden">
      <style>{KID_CSS}</style>
      <span className="wk-blob -left-40 -top-40 h-[30rem] w-[30rem]" style={{ background: 'radial-gradient(circle, #FFCBA4 0%, transparent 70%)' }} aria-hidden />
      <span className="wk-blob -right-40 top-1/4 h-[28rem] w-[28rem]" style={{ background: 'radial-gradient(circle, #D4EEFF 0%, transparent 70%)' }} aria-hidden />
      <span className="wk-blob -bottom-40 left-1/4 h-[28rem] w-[28rem]" style={{ background: 'radial-gradient(circle, #D6F5E3 0%, transparent 70%)' }} aria-hidden />
      <header className="relative z-[1] mx-auto flex max-w-5xl items-center px-5 py-4">
        <Logo />
      </header>
      <main className="relative z-[1] flex min-h-[calc(100vh-88px)] items-start justify-center px-4 pb-10 pt-2 sm:items-center">
        {children}
      </main>
    </div>
  )
}

function Tile({ tint, emoji, label, value }: { tint: string; emoji: string; label: string; value: string }) {
  return (
    <div className="rounded-2xl px-2 py-2.5" style={{ background: tint }}>
      <p className="text-lg" aria-hidden>{emoji}</p>
      <p className="text-[11px] font-bold uppercase tracking-wide text-[#7A6055]">{label}</p>
      <p className="truncate text-sm font-extrabold" title={value}>{value}</p>
    </div>
  )
}

function Rule({ tint, emoji, text }: { tint: string; emoji: string; text: string }) {
  return (
    <div className="flex items-center gap-3 rounded-2xl border-2 bg-white px-3 py-2.5 text-sm font-semibold" style={{ borderColor: tint }}>
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl text-lg" style={{ background: tint }} aria-hidden>{emoji}</span>
      {text}
    </div>
  )
}
