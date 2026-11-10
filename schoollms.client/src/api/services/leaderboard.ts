/**
 * O'quvchilar leaderboardi (2026-11-08) — o'quv yili bo'yicha: baholar, davomat, oylik test,
 * gamifikatsiya bali va qo'lda qo'shilgan ballar. Hisob serverda (LeaderboardService).
 */
import { api, USE_MOCK } from '../client'

export type LeaderboardScopeKind = 'all' | 'class' | 'track'

export interface LeaderboardRow {
  rank: number
  studentId: string
  fullName: string
  classId: string
  className: string
  trackIds: string[]
  /** O'rtacha jurnal bahosi (2–5); yo'q — null. */
  grades: number | null
  gradeCount: number
  /** Yo'nalish o'quvchisi: "baholar" qismi — oraliq testlar o'rtacha foizi (2026-11-10). */
  isTrack: boolean
  /** Oraliq testlar o'rtachasi (0–100); yo'q — null. */
  interim: number | null
  interimCount: number
  /** Davomat foizi; dars o'tilmagan — null. */
  attendance: number | null
  lessons: number
  absent: number
  /** Oylik test o'rtachasi (0–100); yo'q — null. */
  monthlyTest: number | null
  monthlyCount: number
  /** Gamifikatsiya (intizom) bali — 100 dan boshlanadi. */
  behaviour: number
  manualPoints: number
  /** 0–100 vaznli ball. */
  base: number
  total: number
}

export interface LeaderboardScope {
  id: string
  name: string
}

export interface LeaderboardData {
  from: string
  to: string
  weights: { grades: number; attendance: number; monthly: number; behaviour: number; behaviourFull: number }
  classes: LeaderboardScope[]
  tracks: LeaderboardScope[]
  rows: LeaderboardRow[]
}

export interface LeaderboardReason {
  id: string
  name: string
  points: number
  isActive: boolean
}

export interface LeaderboardAward {
  id: string
  studentId: string
  studentName: string
  className: string
  reasonId: string | null
  reasonName: string
  points: number
  note: string | null
  date: string
  createdBy: string
}

const EMPTY: LeaderboardData = {
  from: '',
  to: '',
  weights: { grades: 40, attendance: 20, monthly: 25, behaviour: 15, behaviourFull: 150 },
  classes: [],
  tracks: [],
  rows: [],
}

export async function getLeaderboard(scope: LeaderboardScopeKind, id?: string): Promise<LeaderboardData> {
  if (USE_MOCK) return EMPTY
  const { data } = await api.get<LeaderboardData>('/admin/leaderboard', {
    params: { scope, id: scope === 'all' ? undefined : id || undefined },
  })
  return data
}

export async function getLeaderboardReasons(): Promise<LeaderboardReason[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<LeaderboardReason[]>('/admin/leaderboard/reasons')
  return data
}

export async function saveLeaderboardReason(
  input: { name: string; points: number; isActive?: boolean },
  id?: string,
): Promise<LeaderboardReason> {
  const { data } = id
    ? await api.put<LeaderboardReason>(`/admin/leaderboard/reasons/${id}`, input)
    : await api.post<LeaderboardReason>('/admin/leaderboard/reasons', input)
  return data
}

export async function deleteLeaderboardReason(id: string): Promise<void> {
  await api.delete(`/admin/leaderboard/reasons/${id}`)
}

export async function getLeaderboardAwards(studentId?: string): Promise<LeaderboardAward[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<LeaderboardAward[]>('/admin/leaderboard/awards', {
    params: { studentId: studentId || undefined },
  })
  return data
}

export async function addLeaderboardAward(input: {
  studentId: string
  reasonId?: string | null
  reasonName?: string | null
  points?: number | null
  note?: string | null
  date?: string | null
}): Promise<void> {
  await api.post('/admin/leaderboard/awards', input)
}

export async function deleteLeaderboardAward(id: string): Promise<void> {
  await api.delete(`/admin/leaderboard/awards/${id}`)
}
