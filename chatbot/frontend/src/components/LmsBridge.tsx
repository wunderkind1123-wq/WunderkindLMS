import { useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'

/**
 * True when the panel runs inside the LMS "Chatbot" page (an iframe on the LMS host). Then LMS owns
 * the menu: its sidebar lists our pages as submenus, so our own sidebar is hidden (Layout).
 */
export const EMBEDDED = (() => {
  try {
    return window.self !== window.top
  } catch {
    return true // a cross-origin parent throws — still framed
  }
})()

/**
 * Keeps our route and the LMS address in step (docs/modules/chatbot.md §4):
 * - LMS → panel: `{ type: 'wk-chatbot:navigate', path }` when a submenu is clicked;
 * - panel → LMS: `{ type: 'wk-chatbot:location', path }` on every route change (the first one also
 *   tells LMS the panel is ready).
 * Only the same-origin parent is listened to and written to — the panel is served under the LMS host.
 */
export function LmsBridge() {
  const location = useLocation()
  const navigate = useNavigate()

  useEffect(() => {
    if (!EMBEDDED) return
    const onMessage = (e: MessageEvent) => {
      if (e.source !== window.parent || e.origin !== window.location.origin) return
      const data = e.data as { type?: unknown; path?: unknown } | null
      if (data?.type !== 'wk-chatbot:navigate' || typeof data.path !== 'string') return
      if (!data.path.startsWith('/') || data.path.startsWith('//')) return
      navigate(data.path)
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [navigate])

  useEffect(() => {
    if (!EMBEDDED) return
    window.parent.postMessage(
      { type: 'wk-chatbot:location', path: location.pathname + location.search },
      window.location.origin,
    )
  }, [location.pathname, location.search])

  return null
}
