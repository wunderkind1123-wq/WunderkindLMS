import { Navigate } from 'react-router-dom'

/**
 * `/admin/bilimlar` — KIRISH NUQTASI: har doim «Eslatmalarim» (u har bir xodimga ruxsat kalitisiz ochiq).
 * «Xodimlar» tabi u yerdan, faqat rahbariyatga.
 */
export function WikiEntry() {
  return <Navigate to="/admin/bilimlar/eslatmalar" replace />
}
