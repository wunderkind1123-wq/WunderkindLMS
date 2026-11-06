import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { ChevronDown } from 'lucide-react'
import type { SalaryRecipient } from '@/api/services/cashBoxes'
import { cn } from '@/lib/utils'
import { filterRecipients } from './recipientSearch'

const keyOf = (r: SalaryRecipient) => `${r.kind}:${r.id}`

interface Props {
  recipients: SalaryRecipient[]
  value: string
  onChange: (key: string) => void
}

/**
 * Staff picker of the cash-out form — one field that is both the list and its search (client,
 * 2026-11-06: the search goes in place of "Tanlang...", not above it). Clicking opens the whole
 * list; typing "anv" or "bek" keeps the people whose name or surname starts with it.
 *
 * Only an explicit pick (click, Enter) changes the choice — this form pays out money. The one
 * exception is a pasted name that matches exactly one person, which was the reason for the search.
 */
export function SalaryRecipientSelect({ recipients, value, onChange }: Props) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  const filtered = useMemo(() => filterRecipients(recipients, query), [recipients, query])
  const selected = recipients.find((r) => keyOf(r) === value)
  const disabled = recipients.length === 0

  const close = () => {
    setOpen(false)
    setQuery('')
  }

  const pick = (r: SalaryRecipient) => {
    onChange(keyOf(r))
    close()
    inputRef.current?.blur()
  }

  /* Click outside closes the list without changing the choice. */
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (rootRef.current?.contains(e.target as Node)) return
      setOpen(false)
      setQuery('')
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  /* Keep the highlighted row in view while moving with the arrow keys. */
  useEffect(() => {
    if (!open) return
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [active, open])

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) setOpen(true)
      else setActive((i) => Math.min(i + 1, filtered.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      if (!open) return
      e.preventDefault()
      const r = filtered[active]
      if (r) pick(r)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      close()
    }
  }

  return (
    <div ref={rootRef} className="relative">
      <label className="block">
        <span className="mb-1 block text-sm font-medium text-slate-600">
          Xodim<span className="text-red-500"> *</span>
        </span>
        <span className="relative block">
          <input
            ref={inputRef}
            type="text"
            role="combobox"
            aria-expanded={open}
            aria-autocomplete="list"
            autoComplete="off"
            disabled={disabled}
            value={open ? query : selected ? selected.name : ''}
            placeholder={
              disabled
                ? "Xodimlar ro'yxati yuklanmadi"
                : open
                  ? 'Ism-familiyani yozing yoki joylang'
                  : 'Tanlang...'
            }
            onFocus={() => {
              setOpen(true)
              setActive(0)
            }}
            onClick={() => setOpen(true)}
            onChange={(e) => {
              setQuery(e.target.value)
              setActive(0)
              setOpen(true)
            }}
            onPaste={(e) => {
              const matches = filterRecipients(recipients, e.clipboardData.getData('text'))
              if (matches.length === 1) {
                e.preventDefault()
                pick(matches[0])
              }
            }}
            onKeyDown={onKeyDown}
            className={'w-full rounded-lg border border-slate-200 bg-white py-2 pl-3 pr-9 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50'}
          />
          <ChevronDown
            className={cn(
              'pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400 transition-transform',
              open && 'rotate-180',
            )}
          />
        </span>
      </label>

      {open && !disabled && (
        <ul
          ref={listRef}
          role="listbox"
          className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg"
        >
          {filtered.length === 0 ? (
            <li className="px-3 py-2 text-sm text-amber-600">Bunday xodim topilmadi.</li>
          ) : (
            filtered.map((r, i) => {
              const isSelected = keyOf(r) === value
              return (
                <li
                  key={keyOf(r)}
                  data-index={i}
                  role="option"
                  aria-selected={isSelected}
                  /* mousedown, not click: the input must not lose focus and close the list first */
                  onMouseDown={(e) => {
                    e.preventDefault()
                    pick(r)
                  }}
                  onMouseEnter={() => setActive(i)}
                  className={cn(
                    'cursor-pointer px-3 py-2 text-sm text-slate-800',
                    i === active && 'bg-slate-100',
                    isSelected && 'font-medium text-brand-600',
                  )}
                >
                  {r.name}
                  {r.kind === 'teacher' && <span className="text-slate-400"> — o'qituvchi</span>}
                </li>
              )
            })
          )}
        </ul>
      )}
    </div>
  )
}
