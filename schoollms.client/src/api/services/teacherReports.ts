import type { TeacherReportRow, TeacherReportDetail } from '@/types'
import { api, USE_MOCK } from '../client'

/** Barcha o'qituvchilar faollik hisoboti. quarter=0 (yoki berilmasa) — barcha choraklar. */
export async function getTeacherReport(quarter = 0): Promise<TeacherReportRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherReportRow[]>('/admin/teacher-reports', {
    params: { quarter },
  })
  return data
}

/**
 * Kunlik o'quv hisoboti PDF'i (2026-11-16) — har kuni 17:00 da Telegram'ga ketadigan hujjatning o'zi.
 * `date` — "yyyy-MM-dd". Server `dailyReport` kalitini talab qiladi.
 */
export async function downloadDailyReport(date: string): Promise<void> {
  const res = await api.get('/admin/daily-report/pdf', { params: { date }, responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `Kunlik_hisobot_${date.split('-').reverse().join('.')}.pdf`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Bitta o'qituvchining batafsil hisoboti (sinf/fan yoyilmasi). */
export async function getTeacherReportDetail(
  id: string,
  quarter = 0,
): Promise<TeacherReportDetail> {
  const { data } = await api.get<TeacherReportDetail>(`/admin/teacher-reports/${id}`, {
    params: { quarter },
  })
  return data
}
