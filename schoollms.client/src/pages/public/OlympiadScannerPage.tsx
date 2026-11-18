import { useEffect, useMemo, useRef, useState } from 'react'
import axios from 'axios'
import { Camera, ShieldCheck, Square } from 'lucide-react'
import {
  fmtDay,
  hm,
  type CheckinEvent,
  type CheckinResult,
  type CheckinSummary,
  type OlympiadRegistration,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { CheckinPanel, type CheckinApi } from '@/pages/admin/olympiad/components/CheckinPanel'
import { errText } from '@/pages/admin/olympiad/components/lib'

/*
 * Olimpiada botidagi QR skaner (mijoz, 2026-11-18) — Telegram Mini App.
 * Kirish joyidagi admin botda «📷 QR skanerni ochish» ni bosadi; sahifa Telegramning o'z QR oynasini
 * ochadi, har skanerlangan o'quvchi «Kirdi» bo'ladi va skaner keyingisi uchun yana ochiladi.
 * Login yo'q: har so'rov Telegram `initData` ni olib boradi, server uni bot tokeni va admin raqamlari
 * ro'yxati bilan tekshiradi (OlympiadScannerController).
 */

interface TgWebApp {
  initData: string
  platform: string
  ready: () => void
  expand: () => void
  isVersionAtLeast: (v: string) => boolean
  showScanQrPopup: (params: { text?: string }, cb: (text: string) => boolean | void) => void
  closeScanQrPopup: () => void
  onEvent: (event: string, cb: () => void) => void
  offEvent: (event: string, cb: () => void) => void
  HapticFeedback?: { notificationOccurred: (type: 'success' | 'warning' | 'error') => void }
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TgWebApp }
  }
}

const SDK_URL = 'https://telegram.org/js/telegram-web-app.js'

function loadTelegram(): Promise<TgWebApp | null> {
  if (window.Telegram?.WebApp) return Promise.resolve(window.Telegram.WebApp)
  return new Promise((resolve) => {
    const s = document.createElement('script')
    s.src = SDK_URL
    s.onload = () => resolve(window.Telegram?.WebApp ?? null)
    s.onerror = () => resolve(null)
    document.head.appendChild(s)
  })
}

const todayIso = () => new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10)

/** Skanerdan keyin natija shuncha ko'rinib turadi, so'ng skaner yana ochiladi. */
const REOPEN_MS = 1500

export default function OlympiadScannerPage() {
  const [tg, setTg] = useState<TgWebApp | null | undefined>(undefined)
  const [me, setMe] = useState('')
  const [events, setEvents] = useState<CheckinEvent[] | null>(null)
  const [eventId, setEventId] = useState('')
  const [fatal, setFatal] = useState('')

  useEffect(() => {
    loadTelegram().then((w) => {
      if (w) {
        w.ready()
        w.expand()
      }
      setTg(w)
    })
  }, [])

  const initData = tg?.initData ?? ''

  const client = useMemo(
    () =>
      axios.create({
        baseURL: (import.meta.env.VITE_API_BASE_URL ?? '/api') + '/public/olympiad-scanner',
        headers: { 'Content-Type': 'application/json', 'X-Telegram-Init-Data': initData },
      }),
    [initData],
  )

  const api = useMemo<CheckinApi>(
    () => ({
      summary: async (id) => (await client.get<CheckinSummary>(`/events/${id}/summary`)).data,
      search: async (id, q) => (await client.get<OlympiadRegistration[]>(`/events/${id}/search`, { params: { q } })).data,
      scan: async (id, token) => (await client.post<CheckinResult>('/scan', { eventId: id, token })).data,
      checkin: async (rid) => (await client.post<CheckinResult>(`/registrations/${rid}/checkin`)).data,
      undo: async (rid) => (await client.post<OlympiadRegistration>(`/registrations/${rid}/undo`)).data,
    }),
    [client],
  )

  useEffect(() => {
    if (!initData) return
    Promise.all([client.get<{ name: string }>('/me'), client.get<CheckinEvent[]>('/events')])
      .then(([m, list]) => {
        setMe(m.data.name)
        setEvents(list.data)
        const today = todayIso()
        const pick = list.data.find((e) => e.date === today) ?? list.data.find((e) => e.date > today) ?? list.data[list.data.length - 1]
        if (pick) setEventId(pick.id)
      })
      .catch((e) => setFatal(errText(e, "Yuklab bo'lmadi")))
  }, [client, initData])

  if (tg === undefined) return <Shell><Loader label="Yuklanmoqda..." /></Shell>
  if (!initData)
    return (
      <Shell>
        <Notice>Bu sahifa olimpiada botidagi «📷 QR skanerni ochish» tugmasi orqali ochiladi.</Notice>
      </Shell>
    )
  if (fatal) return <Shell><Notice tone="error">{fatal}</Notice></Shell>

  const current = events?.find((e) => e.id === eventId)
  const native = !!tg && tg.isVersionAtLeast('6.4') && (tg.platform === 'android' || tg.platform === 'ios')

  return (
    <Shell>
      <div className="flex items-center gap-2">
        <ShieldCheck className="h-5 w-5 text-brand-600" />
        <div className="min-w-0">
          <h1 className="text-lg font-semibold text-slate-800">Kirishni belgilash</h1>
          {me && <p className="truncate text-xs text-slate-400">{me}</p>}
        </div>
      </div>
      {events === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : events.length === 0 ? (
        <Notice>E'lon qilingan tadbir yo'q</Notice>
      ) : (
        <>
          <Select value={eventId} onChange={(e) => setEventId(e.target.value)} className="py-3 text-base">
            {events.map((e) => (
              <option key={e.id} value={e.id}>
                {fmtDay(e.date)} {hm(e.startTime)} — {e.title}
              </option>
            ))}
          </Select>
          {current && (
            <CheckinPanel
              key={current.id}
              eventId={current.id}
              closed={!!current.checkinClosedAt}
              api={api}
              Scanner={native ? TelegramScanner : undefined}
            />
          )}
        </>
      )}
    </Shell>
  )
}

/**
 * Telegramning o'z QR oynasi (Bot API 6.4+, faqat mobil ilovada). Har skanerdan keyin oyna yopiladi,
 * natija ~1,5 soniya ko'rinadi va skaner o'zi yana ochiladi — admin «To'xtatish»ni bosguncha.
 */
function TelegramScanner({ onScan }: { onScan: (text: string) => Promise<CheckinResult | null> }) {
  // `native` tekshiruvidan keyingina render qilinadi — SDK yuklangan.
  const tg = window.Telegram!.WebApp!
  const [active, setActive] = useState(false)
  const activeRef = useRef(false)
  const closingByUs = useRef(false)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  const stop = () => {
    activeRef.current = false
    setActive(false)
    clearTimeout(timer.current)
  }

  const open = () => {
    if (!activeRef.current) return
    // Dasturiy yopishda hodisa kelmasligi ham mumkin — bayroq har ochilishda tozalanadi.
    closingByUs.current = false
    tg.showScanQrPopup({ text: "O'quvchining QR kodini ramkaga to'g'rilang" }, (text) => {
      closingByUs.current = true
      void onScan(text).then((r) => {
        const kind = !r || r.outcome === 'error' ? 'error' : r.outcome === 'already' ? 'warning' : 'success'
        tg.HapticFeedback?.notificationOccurred(kind)
        timer.current = setTimeout(open, REOPEN_MS)
      })
      return true
    })
  }

  const start = () => {
    activeRef.current = true
    setActive(true)
    open()
  }

  useEffect(() => {
    // Admin oynani o'zi yopsa — uzluksiz rejim to'xtaydi.
    const onClosed = () => {
      if (closingByUs.current) {
        closingByUs.current = false
        return
      }
      stop()
    }
    tg.onEvent('scanQrPopupClosed', onClosed)
    return () => {
      tg.offEvent('scanQrPopupClosed', onClosed)
      clearTimeout(timer.current)
    }
  }, [tg])

  return active ? (
    <Button variant="secondary" className="w-full py-4 text-base" onClick={stop}>
      <Square className="h-5 w-5" /> To'xtatish
    </Button>
  ) : (
    <Button className="w-full py-4 text-base" onClick={start}>
      <Camera className="h-5 w-5" /> QR skanerlash
    </Button>
  )
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-50">
      <div className="mx-auto max-w-xl space-y-4 px-4 py-5">{children}</div>
    </div>
  )
}

function Notice({ children, tone }: { children: React.ReactNode; tone?: 'error' }) {
  return (
    <Card>
      <p className={tone === 'error' ? 'py-6 text-center text-red-600' : 'py-6 text-center text-slate-500'}>{children}</p>
    </Card>
  )
}
