import { api } from '../client'
import type { LessonOwnerKind } from '@/types'

/**
 * Jadval tuzish vositalari (2026-11-21): xonalar, yuklama, ustoz bandligi, avtomatik jadval va Excel.
 * Server: `TimetableBuilderController` (`/api/admin/schedule/...`).
 */

export interface ScheduleRoom {
  id: string
  name: string
  kind: string
  capacity: number
}

export interface ScheduleRooms {
  rooms: ScheduleRoom[]
  /** Sinf id → o'z xonasi (rooms.id). */
  homeRooms: Record<string, string>
}

export async function getScheduleRooms(): Promise<ScheduleRooms> {
  const { data } = await api.get<ScheduleRooms>('/admin/schedule/rooms')
  return data
}

export interface TeachingLoad {
  id?: string | null
  ownerKind: LessonOwnerKind
  ownerId: string
  subjectId: string
  /** 0 — butun sinf; 1/2 — yarmlari. */
  subGroup: number
  teacherId: string
  hoursPerWeek: number
  roomId: string | null
}

export async function getTeachingLoads(): Promise<TeachingLoad[]> {
  const { data } = await api.get<TeachingLoad[]>('/admin/schedule/loads')
  return data
}

export async function saveOwnerLoads(ownerId: string, rows: TeachingLoad[]): Promise<void> {
  await api.put(`/admin/schedule/loads/${ownerId}`, { rows })
}

export interface FillLoadsResult {
  owners: number
  rows: number
  skipped: number
}

export async function fillLoadsFromSchedule(templateName: string | null, overwrite: boolean): Promise<FillLoadsResult> {
  const { data } = await api.post<FillLoadsResult>('/admin/schedule/loads/fill', { templateName, overwrite })
  return data
}

export interface UnavailabilitySlot {
  day: number
  /** 0 — butun kun. */
  period: number
}

export interface TeacherUnavailability {
  teacherId: string
  slots: UnavailabilitySlot[]
}

export async function getUnavailability(): Promise<TeacherUnavailability[]> {
  const { data } = await api.get<TeacherUnavailability[]>('/admin/schedule/unavailability')
  return data
}

export async function saveUnavailability(teacherId: string, slots: UnavailabilitySlot[]): Promise<void> {
  await api.put(`/admin/schedule/unavailability/${teacherId}`, { slots })
}

export interface GenerateRequest {
  name: string
  noGaps: boolean
  respectUnavailability: boolean
  ownerIds?: string[] | null
}

export interface GenerateUnplaced {
  ownerId: string
  ownerName: string
  subjectName: string
  teacherNames: string
  missing: number
}

export interface GenerateResult {
  name: string
  owners: number
  placed: number
  total: number
  ownerGaps: number
  teacherGaps: number
  subjectRepeats: number
  unplaced: GenerateUnplaced[]
  warnings: string[]
}

export async function generateSchedule(req: GenerateRequest): Promise<GenerateResult> {
  // Hisoblash bir necha soniya oladi.
  const { data } = await api.post<GenerateResult>('/admin/schedule/generate', req, { timeout: 120_000 })
  return data
}

/** Jadvalni Excel'da yuklab olish (har kun varag'i + o'qituvchilar). */
export async function downloadScheduleExcel(name: string): Promise<void> {
  const { data } = await api.get<Blob>('/admin/schedule/export', { params: { name }, responseType: 'blob' })
  const url = URL.createObjectURL(data)
  const a = document.createElement('a')
  a.href = url
  a.download = `Dars jadvali — ${name}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export interface OwnerStudyDays {
  ownerId: string
  /** 5 — dushanba–juma, 6 — dushanba–shanba. */
  days: number
  /** Saqlanmagan — sukut qoidasi (1–3-sinf 5 kun, qolganlari 6 kun). */
  isDefault: boolean
}

export async function getStudyDays(): Promise<OwnerStudyDays[]> {
  const { data } = await api.get<OwnerStudyDays[]>('/admin/schedule/study-days')
  return data
}

export async function saveStudyDays(ownerId: string, days: number): Promise<void> {
  await api.put(`/admin/schedule/study-days/${ownerId}`, { days })
}
