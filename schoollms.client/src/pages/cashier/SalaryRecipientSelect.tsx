import { useMemo, useState } from 'react'
import { Search } from 'lucide-react'
import type { SalaryRecipient } from '@/api/services/cashBoxes'
import { Select } from '@/components/ui/Input'
import { filterRecipients } from './recipientSearch'

const keyOf = (r: SalaryRecipient) => `${r.kind}:${r.id}`

interface Props {
  recipients: SalaryRecipient[]
  value: string
  onChange: (key: string) => void
}

/**
 * Staff picker of the cash-out form: a search box over the list (client, 2026-11-05) — a name
 * pasted from a message finds the person. The same shape as `StudentSelect`.
 */
export function SalaryRecipientSelect({ recipients, value, onChange }: Props) {
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => filterRecipients(recipients, query), [recipients, query])

  const selected = recipients.find((r) => keyOf(r) === value)
  const visible =
    selected && !filtered.some((r) => keyOf(r) === value) ? [selected, ...filtered] : filtered

  /* A single match is picked at once. Otherwise a search clears the choice: this form pays out
     money, so a person picked for an earlier search must not stay selected unnoticed. */
  const search = (next: string) => {
    setQuery(next)
    if (next.trim() === '') return
    const matches = filterRecipients(recipients, next)
    onChange(matches.length === 1 ? keyOf(matches[0]) : '')
  }

  const noMatch = query.trim() !== '' && filtered.length === 0

  return (
    <div className="space-y-2">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          type="search"
          value={query}
          disabled={recipients.length === 0}
          onChange={(e) => search(e.target.value)}
          placeholder="Ism-familiyani yozing yoki joylang"
          className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50"
        />
      </div>
      <Select
        label="Xodim"
        required
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={recipients.length === 0}
      >
        <option value="" disabled>
          {recipients.length === 0 ? "Xodimlar ro'yxati yuklanmadi" : 'Tanlang...'}
        </option>
        {visible.map((r) => (
          <option key={keyOf(r)} value={keyOf(r)}>
            {r.name}
            {r.kind === 'teacher' ? " — o'qituvchi" : ''}
          </option>
        ))}
      </Select>
      {noMatch && <p className="text-xs text-amber-600">Bunday xodim topilmadi.</p>}
    </div>
  )
}
