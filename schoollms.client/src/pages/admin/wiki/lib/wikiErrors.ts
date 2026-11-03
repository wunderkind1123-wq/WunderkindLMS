/** Serverning `{ message }` javobidan foydalanuvchiga ko'rsatiladigan matn; bo'lmasa — `fallback`. */
export function apiErrorMessage(err: unknown, fallback: string): string {
  const e = err as { response?: { data?: { message?: unknown } } }
  const m = e?.response?.data?.message
  return typeof m === 'string' && m.trim() ? m : fallback
}
