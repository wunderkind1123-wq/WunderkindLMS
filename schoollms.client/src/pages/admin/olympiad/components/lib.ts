import { hrErrorMessage } from '@/api/services/hrTasks'
import type { EventStatus } from '@/api/services/olympiad'

export const EVENT_TONE: Record<EventStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  published: 'bg-emerald-50 text-emerald-700',
  finished: 'bg-brand-50 text-brand-700',
  archived: 'bg-slate-100 text-slate-400',
}

/** Havolani nusxalash; xavfsiz kontekst bo'lmasa — qo'lda nusxalash uchun ko'rsatiladi. */
export async function copyText(text: string, done: (m: string) => void) {
  try {
    await navigator.clipboard.writeText(text)
    done('Havola nusxalandi')
  } catch {
    window.prompt('Havolani nusxalang', text)
  }
}

export const errText = (e: unknown, fallback = "Bajarib bo'lmadi") => hrErrorMessage(e, fallback)

/** Belgilarli matnning taxminiy ko'rinadigan uzunligi (belgilar olib tashlanadi). */
export function visibleLength(markup: string): number {
  return markup.replace(/\*\*|__|\+\+|~~|\|\||`/g, '').replace(/\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/^> /gm, '').length
}

export const TH = 'whitespace-nowrap bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400'