import { api, USE_MOCK } from '../client'
import type { StageColor } from '@/types'

/* =========================================================================
 *  Arxivlash sabablari katalogi va ommaviy arxivlash — §2.2.
 *
 *  IKKI USTUN, IKKI VAZIFA: `reason` (erkin matn) MAJBURIY — tafsilot;
 *  `archiveReasonId` (katalog qatori) 2026-11-13 dan MAJBURIY — guruhlash uchun.
 * ========================================================================= */

/** Katalogdagi bitta arxivlash sababi. */
export interface ArchiveReason {
  id: string
  name: string
  isActive: boolean
  position: number
  /** Nechta o'quvchida ishlatilgan — 0 bo'lsagina o'chirish mumkin. */
  usedBy: number
  /** Qaysi guruhda (null — guruhsiz). */
  groupId: string | null
}

export interface SaveArchiveReasonInput {
  name: string
  isActive?: boolean
  position?: number
  /** Har doim yuboriladi: null — guruhdan chiqarish. */
  groupId: string | null
}

/** Qarzi borligi uchun arxivlanmagan o'quvchi. */
export interface ArchiveBlockedStudent {
  studentId: string
  fullName: string
  className: string
  /** Qarz summasi (musbat son). */
  debt: number
}

/**
 * Arxivlash natijasi. Qarzdor topilsa amal BUTUNLAY rad etiladi
 * (`archived = 0`) — server 400 qaytaradi va shu obyekt javob tanasida keladi.
 */
export interface BulkArchiveResult {
  archived: number
  blocked: ArchiveBlockedStudent[]
  /** Joriy foydalanuvchi (superadmin) qoidani chetlab o'ta oladimi. */
  canOverride: boolean
  message?: string | null
}

export async function getArchiveReasons(includeInactive = false): Promise<ArchiveReason[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ArchiveReason[]>('/admin/archive-reasons', {
    params: includeInactive ? { includeInactive: true } : undefined,
  })
  return data
}

export async function createArchiveReason(input: SaveArchiveReasonInput): Promise<ArchiveReason> {
  const { data } = await api.post<ArchiveReason>('/admin/archive-reasons', input)
  return data
}

export async function updateArchiveReason(
  id: string,
  input: SaveArchiveReasonInput,
): Promise<ArchiveReason> {
  const { data } = await api.put<ArchiveReason>(`/admin/archive-reasons/${id}`, input)
  return data
}

export async function deleteArchiveReason(id: string): Promise<void> {
  await api.delete(`/admin/archive-reasons/${id}`)
}

export interface BulkArchiveInput {
  studentIds: string[]
  /** Erkin matn — majburiy. */
  reason: string
  /** Katalog qatori — majburiy (2026-11-13). */
  archiveReasonId: string
  /** Qarzdorlik to'sig'ini chetlab o'tish (faqat superadmin). */
  force?: boolean
}

/** Bir nechta o'quvchini bitta sabab bilan arxivlaydi. */
export async function archiveManyStudents(input: BulkArchiveInput): Promise<BulkArchiveResult> {
  const { data } = await api.post<BulkArchiveResult>('/admin/students/archive-many', {
    studentIds: input.studentIds,
    reason: input.reason,
    archiveReasonId: input.archiveReasonId ?? null,
    force: input.force ?? false,
  })
  return data
}

/* =========================================================================
 *  Sabab GURUHLARI va arxiv doskasi (2026-11-13). Guruh = doskadagi ustun;
 *  maktab ularni Sozlamalarda o'zi boshqaradi. Sabab bitta guruhga kiradi,
 *  arxivlashda faqat sabab tanlanadi — guruh undan kelib chiqadi.
 *  docs/modules/archive-reason-groups.md
 * ========================================================================= */

export interface ArchiveReasonGroup {
  id: string
  name: string
  /** stageColors kaliti: slate, blue, emerald, amber, violet, rose, cyan, orange. */
  color: StageColor
  position: number
  isActive: boolean
  /** Guruhdagi sabablar — 0 bo'lsagina o'chiriladi. */
  reasonCount: number
  /** Hozir arxivda turgan, shu guruh sababi bilan ketganlar. */
  studentCount: number
}

export interface SaveArchiveReasonGroupInput {
  name: string
  color?: StageColor
  position?: number
  isActive?: boolean
}

export async function getArchiveReasonGroups(includeInactive = false): Promise<ArchiveReasonGroup[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ArchiveReasonGroup[]>('/admin/archive-reason-groups', {
    params: includeInactive ? { includeInactive: true } : undefined,
  })
  return data
}

export async function createArchiveReasonGroup(input: SaveArchiveReasonGroupInput): Promise<ArchiveReasonGroup> {
  const { data } = await api.post<ArchiveReasonGroup>('/admin/archive-reason-groups', input)
  return data
}

export async function updateArchiveReasonGroup(
  id: string,
  input: SaveArchiveReasonGroupInput,
): Promise<ArchiveReasonGroup> {
  const { data } = await api.put<ArchiveReasonGroup>(`/admin/archive-reason-groups/${id}`, input)
  return data
}

export async function reorderArchiveReasonGroups(ids: string[]): Promise<void> {
  await api.put('/admin/archive-reason-groups/order', ids)
}

export async function deleteArchiveReasonGroup(id: string): Promise<void> {
  await api.delete(`/admin/archive-reason-groups/${id}`)
}

/** Doskadagi bitta o'quvchi. */
export interface ArchiveBoardStudent {
  id: string
  fullName: string
  /** Ketgan paytdagi sinfi. */
  className: string | null
  archivedAt: string | null
  reasonId: string | null
  reasonName: string | null
  /** Arxivlashda yozilgan izoh. */
  note: string | null
  parentPhone: string | null
  /** "Lidlarga qaytarish" bilan ochilgan lid (bo'lsa). */
  leadId: string | null
}

/** Ustun. `id = null` — tizim ustuni "Sababi aniqlanmagan". */
export interface ArchiveBoardColumn {
  id: string | null
  name: string
  color: StageColor
  students: ArchiveBoardStudent[]
}

export interface ArchiveBoard {
  columns: ArchiveBoardColumn[]
  total: number
}

export async function getArchiveBoard(): Promise<ArchiveBoard> {
  if (USE_MOCK) return { columns: [], total: 0 }
  const { data } = await api.get<ArchiveBoard>('/admin/students/archive-board')
  return data
}

/** Arxivdagi o'quvchining ketish sababini o'zgartirish. `note` berilsa izoh ham almashadi. */
export async function setStudentArchiveReason(
  studentId: string,
  reasonId: string,
  note?: string,
): Promise<ArchiveBoardStudent> {
  const { data } = await api.put<ArchiveBoardStudent>(`/admin/students/${studentId}/archive-reason`, {
    reasonId,
    note: note?.trim() || null,
  })
  return data
}

/** Arxivdagi o'quvchini lidlarga qaytarish — lidlar doskasining birinchi ustuniga tushadi. */
export async function returnStudentToLeads(studentId: string, note?: string): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>(`/admin/students/${studentId}/to-lead`, {
    note: note?.trim() || null,
  })
  return data
}

/**
 * Arxivlash oynalari uchun: faol sabablar guruh bo'yicha (<optgroup>), guruh tartibida;
 * guruhsizlari oxirida "Boshqa sabablar" bo'lib.
 */
export function reasonsByGroup(
  reasons: ArchiveReason[],
  groups: ArchiveReasonGroup[],
): { label: string; reasons: ArchiveReason[] }[] {
  const active = reasons.filter((r) => r.isActive)
  const out = groups
    .filter((g) => g.isActive)
    .map((g) => ({ label: g.name, reasons: active.filter((r) => r.groupId === g.id) }))
    .filter((x) => x.reasons.length > 0)
  const known = new Set(groups.filter((g) => g.isActive).map((g) => g.id))
  const rest = active.filter((r) => !r.groupId || !known.has(r.groupId))
  if (rest.length > 0) out.push({ label: out.length > 0 ? 'Boshqa sabablar' : 'Sabablar', reasons: rest })
  return out
}
