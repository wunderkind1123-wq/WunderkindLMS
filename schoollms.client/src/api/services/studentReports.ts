import { api } from '../client'

/**
 * O'quvchi hisoboti (PDF) — TZ "O'quvchi hisobotini haftalik/oylik yuklab olish", 2026-11-17.
 * Ota-onaga yakshanba kuni ketadigan hisobotning o'zi, ixtiyoriy davr uchun. Hech narsa yubormaydi.
 * Server `studentReports` kalitini talab qiladi (o'qish uchun ham).
 */

export interface StudentReportPeriod {
  key: string
  label: string
  /** "yyyy-MM-dd" */
  from: string
  to: string
}

export interface StudentReportQuarter {
  quarter: number
  from: string
  to: string
  current: boolean
}

export interface StudentReportPeriods {
  today: string
  presets: StudentReportPeriod[]
  quarters: StudentReportQuarter[]
}

export async function getStudentReportPeriods(): Promise<StudentReportPeriods> {
  const { data } = await api.get<StudentReportPeriods>('/admin/student-reports/periods')
  return data
}

/** Server bergan fayl nomi (Content-Disposition), bo'lmasa — oddiy nom. */
function fileNameOf(header: string | undefined, fallback: string): string {
  if (!header) return fallback
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (star) return decodeURIComponent(star[1])
  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain ? plain[1] : fallback
}

async function fetchPdf(studentId: string, from: string, to: string, view: boolean) {
  const res = await api.get(`/admin/student-reports/${studentId}/pdf`, {
    params: { from, to, view },
    responseType: 'blob',
  })
  const name = fileNameOf(res.headers['content-disposition'] as string | undefined, `Hisobot_${from}_${to}.pdf`)
  return { blob: res.data as Blob, name }
}

/** "Yuklab olish" — PDF fayl sifatida saqlanadi. */
export async function downloadStudentReport(studentId: string, from: string, to: string): Promise<void> {
  const { blob, name } = await fetchPdf(studentId, from, to, false)
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/**
 * "Ko'rish" — yangi tabda. Tab bosilgan zahoti ochiladi (aks holda brauzer so'rovdan keyingi
 * `window.open` ni bloklaydi), PDF tayyor bo'lgach unga yuklanadi.
 */
export async function viewStudentReport(studentId: string, from: string, to: string): Promise<void> {
  const tab = window.open('', '_blank')
  try {
    const { blob } = await fetchPdf(studentId, from, to, true)
    const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }))
    if (tab) tab.location.href = url
    else window.location.href = url
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (e) {
    tab?.close()
    throw e
  }
}
