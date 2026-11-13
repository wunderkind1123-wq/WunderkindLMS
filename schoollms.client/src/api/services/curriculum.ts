import { api } from '../client'

/**
 * Ish reja (docs/modules/curriculum-monitoring.md §1). Tiplar
 * `SchoolLms.Application/Dtos/CurriculumDtos.cs` dan; sanalar — "yyyy-MM-dd" matn.
 */

export interface CurriculumPlanListItem {
  id: string
  title: string
  subjectId: string
  subjectName: string
  grade: number | null
  groupId: string | null
  groupName: string | null
  topicCount: number
  totalHours: number
  fileUrl: string | null
  fileName: string | null
  updatedAt: string
  owners: string[]
  averagePercent: number
}

export interface CurriculumOwner {
  kind: 'class' | 'group'
  id: string
  name: string
}

export interface CurriculumMark {
  ownerKind: string
  ownerId: string
  passedOn: string
}

export interface CurriculumTopic {
  id: string
  order: number
  quarter: number | null
  week: number | null
  section: string | null
  title: string
  hours: number
  goal: string | null
  homework: string | null
  /** Aniq dars sanasi (fayldagi «Sana» yoki qo'lda) yoki chorak/haftadan taxmin. */
  plannedOn: string | null
  /** `plannedOn` — aniq sana, taxmin emas. */
  plannedExact: boolean
  marks: CurriculumMark[]
}

export interface CurriculumPlanDetail {
  id: string
  title: string
  subjectId: string
  subjectName: string
  grade: number | null
  groupId: string | null
  fileUrl: string | null
  fileName: string | null
  updatedAt: string
  owners: CurriculumOwner[]
  topics: CurriculumTopic[]
}

export interface CurriculumPreviewTopic {
  order: number
  quarter: number | null
  week: number | null
  section: string | null
  title: string
  hours: number
  plannedOn: string | null
}

export interface CurriculumImportResult {
  planId: string | null
  topicCount: number
  totalHours: number
  errors: string[]
  preview: CurriculumPreviewTopic[]
  /** Bayram yoki yakshanbaga tushgan darslar — saqlashga to'sqinlik qilmaydi. */
  warnings: string[]
}

export interface CurriculumQuarter {
  quarter: number
  planned: number
  passed: number
}

export interface CurriculumReportRow {
  planId: string
  planTitle: string
  subjectName: string
  ownerKind: string
  ownerId: string
  ownerName: string
  topicCount: number
  passedCount: number
  totalHours: number
  passedHours: number
  percent: number
  expected: number | null
  delta: number | null
  forecastFinish: string | null
  lateWarning: boolean
  quarters: CurriculumQuarter[]
}

export interface CurriculumMissing {
  subjectId: string
  subjectName: string
  grade: number | null
  groupId: string | null
  groupName: string | null
  owners: string[]
}

export interface CurriculumReport {
  today: string
  yearEnd: string | null
  quartersSet: boolean
  rows: CurriculumReportRow[]
  missing: CurriculumMissing[]
}

export interface TeacherCurriculumItem {
  planId: string
  title: string
  subjectName: string
  ownerKind: 'class' | 'group'
  ownerId: string
  ownerName: string
  topicCount: number
  passedCount: number
  nextTopicId: string | null
  nextTopicTitle: string | null
}

export interface CurriculumMarkRequest {
  topicId: string
  ownerKind: string
  ownerId: string
  passed: boolean
  passedOn?: string
}

export async function getCurriculumPlans(): Promise<CurriculumPlanListItem[]> {
  const { data } = await api.get<CurriculumPlanListItem[]>('/admin/curriculum/plans')
  return data
}

export async function getCurriculumPlan(id: string, teacher = false): Promise<CurriculumPlanDetail> {
  const { data } = await api.get<CurriculumPlanDetail>(teacher ? `/teacher/curriculum/${id}` : `/admin/curriculum/plans/${id}`)
  return data
}

export async function deleteCurriculumPlan(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/plans/${id}`)
}

export interface CurriculumImportInput {
  file: File
  subjectId: string
  grade?: number
  groupId?: string
  title?: string
  originalUrl?: string
  originalName?: string
}

/** `dryRun` — faqat tekshiradi (mavzular ko'rinishi). Xatoli fayl 400 bilan `errors` qaytaradi. */
export async function importCurriculum(input: CurriculumImportInput, dryRun: boolean): Promise<CurriculumImportResult> {
  const fd = new FormData()
  fd.append('file', input.file)
  fd.append('subjectId', input.subjectId)
  if (input.grade != null) fd.append('grade', String(input.grade))
  if (input.groupId) fd.append('groupId', input.groupId)
  if (input.title) fd.append('title', input.title)
  if (input.originalUrl) fd.append('originalUrl', input.originalUrl)
  if (input.originalName) fd.append('originalName', input.originalName)
  try {
    const { data } = await api.post<CurriculumImportResult>(`/admin/curriculum/plans/import${dryRun ? '?dryRun=true' : ''}`, fd, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    return data
  } catch (err) {
    const body = (err as { response?: { data?: CurriculumImportResult & { message?: string } } })?.response?.data
    if (body?.errors) return body
    throw err
  }
}

/** Asl hujjat (Word/PDF/Excel) — umumiy yuklash endpoint'i orqali. */
export async function uploadOriginal(file: File): Promise<{ url: string; fileName: string }> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<{ fileName: string; url: string }>('/admin/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return { url: data.url, fileName: data.fileName }
}

export async function markCurriculumTopic(req: CurriculumMarkRequest, teacher = false): Promise<void> {
  await api.put(teacher ? '/teacher/curriculum/progress' : '/admin/curriculum/progress', req)
}

/** Admin: bitta mavzuning aniq reja sanasi ("yyyy-MM-dd"); null — tozalash (chorak/haftadan hisoblanadi). */
export async function setCurriculumTopicDate(topicId: string, plannedOn: string | null): Promise<void> {
  await api.put(`/admin/curriculum/topics/${topicId}/planned-on`, { plannedOn })
}

export async function getCurriculumReport(): Promise<CurriculumReport> {
  const { data } = await api.get<CurriculumReport>('/admin/curriculum/report')
  return data
}

export async function getTeacherCurriculum(): Promise<TeacherCurriculumItem[]> {
  const { data } = await api.get<TeacherCurriculumItem[]>('/teacher/curriculum')
  return data
}

/** Brauzerda fayl yuklab olish (blob + nom). */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const res = await api.get(path, { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? fallbackName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export function curriculumError(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

export const ownerKey = (kind: string, id: string) => `${kind}:${id}`

export interface CurriculumOptions {
  subjects: { id: string; name: string }[]
  grades: number[]
  tracks: { id: string; name: string }[]
}

export async function getCurriculumOptions(): Promise<CurriculumOptions> {
  const { data } = await api.get<CurriculumOptions>('/admin/curriculum/options')
  return data
}
