import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { api } from '@/api/client'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const BASE = '/admin/chatbot'

/** `/admin/chatbot/inbox?x=1` → `/inbox?x=1` (the panel's own route); the root → `/`. */
const panelPath = (pathname: string, search: string) => (pathname.slice(BASE.length) || '/') + search

/**
 * Chatbot — the Instagram/Telegram AI sales agent (docs/modules/chatbot.md). Its own panel runs at
 * `/chatbot/` next to LMS; this page embeds it and signs the user in with a one-time token, so
 * nobody logs in twice. A fresh token on every open: a role change in LMS applies at once.
 *
 * The panel's pages are LMS submenus (`/admin/chatbot/<page>`). The frame loads once; a submenu
 * click tells it where to go, and every route change inside it comes back so the LMS address (and
 * the active submenu) follow it (`chatbot/frontend/src/components/LmsBridge.tsx`).
 */
export function ChatbotPage() {
  const location = useLocation()
  const navigate = useNavigate()
  const want = panelPath(location.pathname, location.search)

  const [src, setSrc] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const frame = useRef<HTMLIFrameElement>(null)
  const firstPath = useRef(want)
  /** The panel has reported at least once — before that a navigate message would be lost. */
  const ready = useRef(false)
  /** The panel's current route, as it last reported it. */
  const shown = useRef<string | null>(null)

  useEffect(() => {
    let alive = true
    api
      .get<{ token: string }>('/admin/chatbot/sso')
      .then(({ data }) => {
        if (!alive) return
        const path = firstPath.current.replace(/^\//, '')
        setSrc(`/chatbot/${path}#sso=${encodeURIComponent(data.token)}`)
      })
      .catch((e) => {
        if (!alive) return
        const status = e?.response?.status
        setError(status === 503 ? 'Chatbot hali ulanmagan.' : "Chatbotni ochib bo'lmadi.")
      })
    return () => {
      alive = false
    }
  }, [])

  const post = (path: string) =>
    frame.current?.contentWindow?.postMessage({ type: 'wk-chatbot:navigate', path }, window.location.origin)

  // Submenu click (or back/forward) → the panel follows.
  useEffect(() => {
    if (ready.current && want !== shown.current) post(want)
  }, [want])

  // Panel route change → the LMS address follows.
  useEffect(() => {
    const onMessage = (e: MessageEvent) => {
      if (e.origin !== window.location.origin || e.source !== frame.current?.contentWindow) return
      const data = e.data as { type?: unknown; path?: unknown } | null
      if (data?.type !== 'wk-chatbot:location' || typeof data.path !== 'string') return
      shown.current = data.path
      if (!ready.current) {
        ready.current = true
        // A submenu clicked while the panel was still loading.
        const current = panelPath(window.location.pathname, window.location.search)
        if (current !== data.path) return void post(current)
      }
      const target = BASE + (data.path === '/' ? '' : data.path)
      if (target !== window.location.pathname + window.location.search) navigate(target, { replace: true })
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [navigate])

  if (error)
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">{error}</p>
      </Card>
    )
  if (!src) return <Loader />

  return (
    <iframe
      ref={frame}
      title="Chatbot"
      src={src}
      className="-m-6 block h-[calc(100%+3rem)] w-[calc(100%+3rem)] border-0"
      // Instagram ulash tugmasi butun oynani olib ketadi (Instagram ramkada ochilmaydi).
      allow="clipboard-write"
    />
  )
}
