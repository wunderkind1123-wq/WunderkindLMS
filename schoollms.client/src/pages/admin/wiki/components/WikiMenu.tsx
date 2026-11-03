import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { MoreHorizontal, type LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface WikiMenuItem {
  label: string
  icon?: LucideIcon
  onClick: () => void
  /** Qizil (o'chirish) */
  danger?: boolean
}

const MENU_WIDTH = 248

/**
 * Daraxt qatoridagi ⋯ menyusi. Ro'yxat `document.body` ga `fixed` bilan chiziladi — chap panel `overflow-y-auto`
 * bo'lgani uchun oddiy `absolute` menyu pastki qatorlarda kesilib qolardi. Tashqariga bosish, Esc yoki aylantirish
 * menyuni yopadi.
 */
export function WikiMenu({ items, triggerClassName }: { items: WikiMenuItem[]; triggerClassName?: string }) {
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!pos) return
    const close = (e: Event) => {
      const t = e.target as Node
      if (menuRef.current?.contains(t) || triggerRef.current?.contains(t)) return
      setPos(null)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPos(null)
    }
    const onScroll = () => setPos(null)
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onScroll)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onScroll)
    }
  }, [pos])

  const toggle = () => {
    if (pos) return setPos(null)
    const r = triggerRef.current?.getBoundingClientRect()
    if (!r) return
    const height = items.length * 34 + 8
    const top = r.bottom + 4 + height > window.innerHeight ? Math.max(8, r.top - 4 - height) : r.bottom + 4
    const left = Math.min(Math.max(8, r.right - MENU_WIDTH), window.innerWidth - MENU_WIDTH - 8)
    setPos({ top, left })
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={toggle}
        aria-label="Amallar"
        aria-haspopup="menu"
        aria-expanded={!!pos}
        className={cn('flex items-center justify-center rounded text-slate-400 hover:bg-slate-200 hover:text-slate-600', triggerClassName)}
      >
        <MoreHorizontal size={15} />
      </button>
      {pos &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            style={{ top: pos.top, left: pos.left, width: MENU_WIDTH }}
            className="fixed z-50 rounded-lg border border-slate-200 bg-white p-1 shadow-lg"
          >
            {items.map((item) => {
              const Icon = item.icon
              return (
                <button
                  key={item.label}
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setPos(null)
                    item.onClick()
                  }}
                  className={cn(
                    'flex h-[34px] w-full items-center gap-2 rounded-md px-2 text-left text-[13px]',
                    item.danger ? 'text-[#d32f2f] hover:bg-red-50' : 'text-slate-700 hover:bg-slate-100',
                  )}
                >
                  {Icon && <Icon size={14} className="shrink-0" />}
                  <span className="truncate">{item.label}</span>
                </button>
              )
            })}
          </div>,
          document.body,
        )}
    </>
  )
}
