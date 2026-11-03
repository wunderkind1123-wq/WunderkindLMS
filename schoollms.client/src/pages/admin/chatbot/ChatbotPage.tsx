import { useEffect, useState } from 'react'
import { api } from '@/api/client'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

/**
 * Chatbot — the Instagram/Telegram AI sales agent (docs/modules/chatbot.md). Its own panel runs at
 * `/chatbot/` next to LMS; this page embeds it and signs the user in with a one-time token, so
 * nobody logs in twice. A fresh token on every open: a role change in LMS applies at once.
 */
export function ChatbotPage() {
  const [src, setSrc] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    api
      .get<{ token: string }>('/admin/chatbot/sso')
      .then(({ data }) => alive && setSrc(`/chatbot/#sso=${encodeURIComponent(data.token)}`))
      .catch((e) => {
        if (!alive) return
        const status = e?.response?.status
        setError(status === 503 ? 'Chatbot hali ulanmagan.' : "Chatbotni ochib bo'lmadi.")
      })
    return () => {
      alive = false
    }
  }, [])

  if (error)
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">{error}</p>
      </Card>
    )
  if (!src) return <Loader />

  return (
    <iframe
      title="Chatbot"
      src={src}
      className="-m-6 block h-[calc(100%+3rem)] w-[calc(100%+3rem)] border-0"
      // Instagram ulash tugmasi butun oynani olib ketadi (Instagram ramkada ochilmaydi).
      allow="clipboard-write"
    />
  )
}
