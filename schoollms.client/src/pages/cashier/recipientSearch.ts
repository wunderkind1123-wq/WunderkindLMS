import type { SalaryRecipient } from '@/api/services/cashBoxes'

/**
 * Spelling-tolerant form of a name: case and apostrophes are dropped and the letters people swap
 * when typing Uzbek names (x/h, q/k) are unified, so "Gulomova", "G'ulomova" and "Gʻulomova" match.
 */
function normalize(text: string): string {
  return text
    .toLowerCase()
    .replace(/['`ʻʼ‘’]/g, '')
    .replace(/x/g, 'h')
    .replace(/q/g, 'k')
}

function words(text: string): string[] {
  return normalize(text).split(/\s+/).filter(Boolean)
}

/**
 * Word order does not matter, because a name pasted from a message may be in either order.
 * A pasted full name may also carry a patronymic the stored name lacks, so from three words up
 * two matching words are enough.
 */
export function filterRecipients(recipients: SalaryRecipient[], query: string): SalaryRecipient[] {
  const wanted = words(query)
  if (wanted.length === 0) return recipients
  const required = wanted.length >= 3 ? 2 : wanted.length
  return recipients.filter((r) => {
    const name = normalize(r.name)
    return wanted.filter((w) => name.includes(w)).length >= required
  })
}
