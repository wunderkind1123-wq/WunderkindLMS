import { api } from '../client'

// O'rindosh ustoz (client, 2026-10-07) — `/api/admin/substitutions`, gated by the `teachers` key.

export interface SubstitutionLesson {
  period: number
  startTime: string | null
  endTime: string | null
  ownerKind: string
  ownerId: string
  ownerName: string
  subjectId: string
  subjectName: string
  subGroup: number
  substitutionId: string | null
  substituteTeacherId: string | null
  substituteTeacherName: string | null
  /** Tashqi o'rindosh (2026-11-11) — set instead of substituteTeacherId. */
  externalSubstituteId: string | null
}

export interface SubstitutionTeacherOption {
  id: string
  fullName: string
  /** Periods this teacher already teaches (or covers) that day. */
  busyPeriods: number[]
}

export interface SubstitutionDay {
  date: string
  teacherId: string
  teacherName: string
  lessons: SubstitutionLesson[]
  teachers: SubstitutionTeacherOption[]
}

export interface Substitution {
  id: string
  date: string
  period: number
  ownerKind: string
  ownerId: string
  ownerName: string
  subjectId: string
  subjectName: string
  subGroup: number
  absentTeacherId: string
  absentTeacherName: string
  /** null when a tashqi o'rindosh covers — then substituteTeacherName is their name. */
  substituteTeacherId: string | null
  substituteTeacherName: string
  reason: string | null
  createdByName: string
  createdAt: string
  externalSubstituteId: string | null
}

export interface SubstitutionCreateItem {
  period: number
  ownerId: string
  subjectId: string
  subGroup: number
  /** Exactly one of the two: an internal teacher or a tashqi o'rindosh. */
  substituteTeacherId: string | null
  externalSubstituteId?: string | null
}

/** Tashqi o'rindosh (client, 2026-11-11) — a card without a login, found again by name or phone. Paid in cash only. */
export interface ExternalSubstitute {
  id: string
  fullName: string
  phone: string
  subjectId: string | null
  subjectName: string | null
}

export interface ExternalSubstituteInput {
  fullName: string
  phone: string
  subjectId: string | null
}

export interface SubstitutionMonthPerson {
  kind: 'teacher' | 'external'
  id: string
  fullName: string
  /** Lessons taught in someone else's place this month. */
  covered: number
  /** Own lessons someone else taught this month. */
  missed: number
  external: ExternalSubstitute | null
}

export interface SubstitutionMonth {
  month: string
  people: SubstitutionMonthPerson[]
  rows: Substitution[]
}

export async function getSubstitutionMonth(month: string): Promise<SubstitutionMonth> {
  const { data } = await api.get<SubstitutionMonth>('/admin/substitutions/month', { params: { month } })
  return data
}

export async function getExternalSubstitutes(q?: string): Promise<ExternalSubstitute[]> {
  const { data } = await api.get<ExternalSubstitute[]>('/admin/substitutions/externals', { params: q ? { q } : {} })
  return data
}

/** Creates a card — or returns the existing one with that phone (`existed: true`). */
export async function createExternalSubstitute(
  payload: ExternalSubstituteInput,
): Promise<{ substitute: ExternalSubstitute; existed: boolean }> {
  const { data } = await api.post<{ substitute: ExternalSubstitute; existed: boolean }>('/admin/substitutions/externals', payload)
  return data
}

export async function getSubstitutionDay(teacherId: string, date: string): Promise<SubstitutionDay> {
  const { data } = await api.get<SubstitutionDay>('/admin/substitutions/day', { params: { teacherId, date } })
  return data
}

export async function getSubstitutions(params: { from?: string; to?: string; teacherId?: string }): Promise<Substitution[]> {
  const { data } = await api.get<Substitution[]>('/admin/substitutions', { params })
  return data
}

export async function createSubstitutions(payload: {
  date: string
  absentTeacherId: string
  items: SubstitutionCreateItem[]
  reason?: string | null
}): Promise<Substitution[]> {
  const { data } = await api.post<Substitution[]>('/admin/substitutions', payload)
  return data
}

export async function cancelSubstitution(id: string): Promise<void> {
  await api.post(`/admin/substitutions/${id}/cancel`)
}
