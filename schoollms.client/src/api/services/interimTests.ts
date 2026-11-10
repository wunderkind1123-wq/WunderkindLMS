/**
 * Oraliq testlar (2026-11-10) — yo'nalish guruhlarining haftalik testi. O'qituvchi savollar sonini
 * belgilaydi va har o'quvchining to'g'ri javoblarini kiritadi; natija foizda (InterimTestService).
 */
import { api, USE_MOCK } from '../client'

export interface InterimSubject {
  id: string
  name: string
}

export interface InterimOption {
  groupId: string
  groupName: string
  subjects: InterimSubject[]
}

export interface InterimTestRow {
  id: string
  groupId: string
  groupName: string
  subjectId: string
  subjectName: string
  date: string
  /** Oy ichida shu yo'nalish·fan bo'yicha nechanchi test. */
  number: number
  questionCount: number
  topic: string | null
  pupilCount: number
  doneCount: number
  averagePercent: number | null
  createdBy: string
}

export interface InterimEntryRow {
  studentId: string
  fullName: string
  className: string
  correct: number | null
  absent: boolean
  percent: number | null
  /** Test kuni guruhda bo'lganmi (yo'q — keyin chiqib ketgan, natijasi qolgan). */
  member: boolean
}

export interface InterimEntry {
  test: InterimTestRow
  rows: InterimEntryRow[]
}

export interface InterimTestInput {
  groupId?: string
  subjectId?: string
  date: string
  questionCount: number
  topic?: string | null
}

export interface InterimResultInput {
  studentId: string
  correct: number | null
  absent: boolean
}

const BASE = '/admin/interim-tests'

export async function getInterimOptions(): Promise<InterimOption[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<InterimOption[]>(`${BASE}/options`)
  return data
}

export async function getInterimTests(filters: { groupId?: string; subjectId?: string; month?: string } = {}): Promise<InterimTestRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<InterimTestRow[]>(BASE, {
    params: {
      groupId: filters.groupId || undefined,
      subjectId: filters.subjectId || undefined,
      month: filters.month || undefined,
    },
  })
  return data
}

export async function createInterimTest(body: InterimTestInput): Promise<InterimTestRow> {
  const { data } = await api.post<InterimTestRow>(BASE, body)
  return data
}

export async function updateInterimTest(id: string, body: InterimTestInput): Promise<InterimTestRow> {
  const { data } = await api.put<InterimTestRow>(`${BASE}/${id}`, body)
  return data
}

export async function deleteInterimTest(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`)
}

export async function getInterimEntry(id: string): Promise<InterimEntry> {
  const { data } = await api.get<InterimEntry>(`${BASE}/${id}/entry`)
  return data
}

export async function saveInterimEntry(id: string, rows: InterimResultInput[]): Promise<{ saved: number }> {
  const { data } = await api.post<{ saved: number }>(`${BASE}/${id}/entry`, { rows })
  return data
}
