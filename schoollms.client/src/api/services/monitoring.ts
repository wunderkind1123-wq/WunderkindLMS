import { api } from '../client'

/** Ichki monitoring (docs/modules/curriculum-monitoring.md §2). Tiplar `MonitoringDtos.cs` dan. */

export type MonitoringDelivery = 'paper' | 'online'
export type MonitoringScoring = 'simple' | 'national'
export type MonitoringMethod = 'rasch' | 'weights'
export type MonitoringStatus = 'draft' | 'published' | 'closed'
export type ParticipantStatus = 'pending' | 'in_progress' | 'done' | 'absent'

export const STATUS_LABEL: Record<MonitoringStatus, string> = {
  draft: 'Qoralama',
  published: 'O‘tkazilmoqda',
  closed: 'Yakunlangan',
}

export const PARTICIPANT_LABEL: Record<ParticipantStatus, string> = {
  pending: 'Kutilmoqda',
  in_progress: 'Yechmoqda',
  done: 'Topshirdi',
  absent: 'Kelmadi',
}

export const LEVELS = ['A+', 'A', 'B+', 'B', 'C+', 'C', '—']

export interface MonitoringOptions {
  subjects: { id: string; name: string }[]
  grades: number[]
  tracks: { id: string; name: string }[]
}

export interface MonitoringOwnerOption {
  kind: 'class' | 'group'
  id: string
  name: string
  grade: number | null
  isTrack: boolean
}

export interface MonitoringBank {
  id: string
  title: string
  subjectId: string
  subjectName: string
  grade: number | null
  groupId: string | null
  groupName: string | null
  questionCount: number
  planId: string | null
}

export interface MonitoringQuestion {
  id: string
  order: number
  text: string
  imageUrl: string | null
  options: string[]
  correctIndex: number
  topicId: string | null
  topicTitle: string | null
  weight: number
}

export interface MonitoringTopicRef {
  id: string
  order: number
  quarter: number | null
  title: string
}

export interface MonitoringBankDetail {
  bank: MonitoringBank
  questions: MonitoringQuestion[]
  topics: MonitoringTopicRef[]
}

export interface MonitoringQuestionPayload {
  text: string
  imageUrl?: string | null
  options: string[]
  correctIndex: number
  topicId?: string | null
  weight?: number
}

export interface MonitoringTestListItem {
  id: string
  title: string
  subjectName: string
  grade: number | null
  groupName: string | null
  delivery: MonitoringDelivery
  scoring: MonitoringScoring
  nationalMethod: MonitoringMethod | null
  testDate: string
  status: MonitoringStatus
  itemCount: number
  participantCount: number
  doneCount: number
  averagePercent: number | null
  averagePoints: number | null
  owners: string[]
}

export interface MonitoringTestPayload {
  title: string
  subjectId: string
  grade?: number | null
  groupId?: string | null
  delivery: MonitoringDelivery
  scoring: MonitoringScoring
  nationalMethod?: MonitoringMethod | null
  testDate: string
  opensAt?: string | null
  closesAt?: string | null
  timeLimitMin?: number | null
  owners: { kind: string; id: string }[]
  questionIds?: string[]
  key?: string
  optionCount?: number
}

export interface MonitoringItem {
  id: string
  order: number
  text: string | null
  imageUrl: string | null
  options: string[]
  optionCount: number
  correctIndex: number
  topicId: string | null
  topicTitle: string | null
  weight: number
}

export interface MonitoringParticipant {
  id: string
  studentId: string
  studentName: string
  ownerKind: string
  ownerId: string
  ownerName: string
  status: ParticipantStatus
  answers: string | null
  correct: number
  percent: number
  points: number | null
  level: string | null
  accessCode: string | null
  deviceBound: boolean
  startedAt: string | null
  finishedAt: string | null
}

export interface MonitoringTopicStat {
  topicId: string | null
  title: string
  itemCount: number
  correctRate: number
  byOwner: { ownerId: string; ownerName: string; correctRate: number }[]
}

export interface MonitoringItemStat {
  order: number
  topicTitle: string | null
  correct: string
  correctRate: number
  choices: Record<string, number>
}

export interface MonitoringOwnerSummary {
  ownerId: string
  ownerName: string
  participants: number
  done: number
  averagePercent: number | null
  averagePoints: number | null
}

export interface MonitoringTestDetail {
  id: string
  title: string
  subjectId: string
  subjectName: string
  grade: number | null
  groupId: string | null
  groupName: string | null
  delivery: MonitoringDelivery
  scoring: MonitoringScoring
  nationalMethod: MonitoringMethod | null
  testDate: string
  opensAt: string | null
  closesAt: string | null
  timeLimitMin: number | null
  status: MonitoringStatus
  items: MonitoringItem[]
  participants: MonitoringParticipant[]
  owners: MonitoringOwnerSummary[]
  topics: MonitoringTopicStat[]
  itemStats: MonitoringItemStat[]
  levels: Record<string, number>
  smallSampleWarning: boolean
  planTopics: MonitoringTopicRef[] | null
}

const BASE = '/admin/monitoring'

export const getMonitoringOptions = async () => (await api.get<MonitoringOptions>(`${BASE}/options`)).data
export const getMonitoringOwners = async () => (await api.get<MonitoringOwnerOption[]>(`${BASE}/owners`)).data
export const getBanks = async () => (await api.get<MonitoringBank[]>(`${BASE}/banks`)).data
export const getBank = async (id: string) => (await api.get<MonitoringBankDetail>(`${BASE}/banks/${id}`)).data
export const saveBank = async (id: string | null, p: { title: string; subjectId: string; grade?: number | null; groupId?: string | null }) =>
  (id ? await api.put<{ id: string }>(`${BASE}/banks/${id}`, p) : await api.post<{ id: string }>(`${BASE}/banks`, p)).data
export const deleteBank = async (id: string) => { await api.delete(`${BASE}/banks/${id}`) }
export const saveQuestion = async (bankId: string, id: string | null, p: MonitoringQuestionPayload) =>
  (id ? await api.put(`${BASE}/banks/${bankId}/questions/${id}`, p) : await api.post(`${BASE}/banks/${bankId}/questions`, p)).data
export const deleteQuestion = async (bankId: string, id: string) => { await api.delete(`${BASE}/banks/${bankId}/questions/${id}`) }

export async function importQuestions(bankId: string, file: File): Promise<{ added: number }> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<{ added: number }>(`${BASE}/banks/${bankId}/import`, fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

export const getTests = async () => (await api.get<MonitoringTestListItem[]>(`${BASE}/tests`)).data
export const getTest = async (id: string) => (await api.get<MonitoringTestDetail>(`${BASE}/tests/${id}`)).data
export const createTest = async (p: MonitoringTestPayload) => (await api.post<{ id: string }>(`${BASE}/tests`, p)).data
export const deleteTest = async (id: string) => { await api.delete(`${BASE}/tests/${id}`) }
export const setTestStatus = async (id: string, status: 'published' | 'closed') => { await api.post(`${BASE}/tests/${id}/status/${status}`) }
export const saveTestItems = async (id: string, rows: { order: number; correctIndex: number; optionCount?: number; topicId?: string | null; weight?: number }[]) => {
  await api.put(`${BASE}/tests/${id}/items`, rows)
}
export const saveAnswers = async (id: string, rows: { participantId: string; answers: string | null; absent: boolean }[]) => {
  await api.put(`${BASE}/tests/${id}/answers`, rows)
}
export const resetDevice = async (participantId: string) => { await api.post(`${BASE}/participants/${participantId}/reset-device`) }

export function monitoringError(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

export const letter = (i: number) => String.fromCharCode(65 + i)

/* ------------------------------------------------ public (o'quvchi, kod bilan) */

export interface MonitoringSession {
  studentName: string
  title: string
  subjectName: string
  deadline: string
  answers: string
  items: { order: number; text: string | null; imageUrl: string | null; options: string[]; optionCount: number }[]
  finished: boolean
}

export const startSession = async (code: string, device: string) =>
  (await api.post<MonitoringSession>('/public/monitoring/start', { code, device })).data
export const saveSessionAnswer = async (code: string, device: string, order: number, choice: string | null) => {
  await api.put('/public/monitoring/answer', { code, device, order, choice })
}
export const finishSession = async (code: string, device: string) =>
  (await api.post<MonitoringSession>('/public/monitoring/finish', { code, device })).data
