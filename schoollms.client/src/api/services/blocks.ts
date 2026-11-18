import { api } from '../client'

/**
 * Bloklar — birlashgan va daraja guruhlari (`docs/modules/blocks.md`).
 *
 * Blok bitta fan bo'yicha bir nechta sinf yoki yo'nalishni bir soatda birlashtiradi va aniq
 * 2 ta guruhga bo'ladi (Katta / Kichik yoki "Iqt-1 guruhi" / "Iqt-2 guruhi"). A'zolik va
 * ustoz SANALI: ko'chirish eski a'zolikni yopadi, yangisini ochadi.
 */

/** `level` — daraja bo'yicha (Katta/Kichik); `base_class` — manba sinf bo'yicha. */
export type BlockType = 'level' | 'base_class'
/** `odd` — Du/Chor/Ju; `even` — Se/Pay/Sha; `custom` — cheklovsiz. */
export type BlockDayPattern = 'odd' | 'even' | 'custom'
/** `class` — sinf; `group` — yo'nalish guruhi. */
export type BlockSourceKind = 'class' | 'group'

export interface BlockSourceRef {
  kind: BlockSourceKind
  id: string
}

export interface BlockSource extends BlockSourceRef {
  name: string
  studentCount: number
  unassignedCount: number
}

export interface BlockGroup {
  id: string
  studyGroupId: string
  name: string
  code: string
  sortOrder: number
  baseOwnerKind: BlockSourceKind | null
  baseOwnerId: string | null
  roomId: string | null
  teacherId: string | null
  teacherName: string | null
  teacherSince: string | null
  memberCount: number
}

export interface Block {
  id: string
  name: string
  subjectId: string
  subjectName: string
  academicYear: string
  blockType: BlockType
  dayPattern: BlockDayPattern
  isActive: boolean
  sources: BlockSource[]
  groups: BlockGroup[]
  studentCount: number
  unassignedCount: number
}

export interface SaveBlockGroupPayload {
  name: string
  /** Faqat yaratishda o'qiladi — keyin ustoz sana bilan almashtiriladi. */
  teacherId?: string | null
  baseOwnerKind?: BlockSourceKind | null
  baseOwnerId?: string | null
  roomId?: string | null
}

export interface SaveBlockPayload {
  name: string
  subjectId: string
  blockType: BlockType
  dayPattern: BlockDayPattern
  sources: BlockSourceRef[]
  groups: SaveBlockGroupPayload[]
}

export interface BlockMember {
  studentId: string
  fullName: string
  sourceKind: string
  sourceId: string
  sourceName: string
  groupId: string | null
  membershipId: string | null
  joinedOn: string | null
}

export interface BlockRoster {
  date: string
  groups: { groupId: string; name: string; members: BlockMember[] }[]
  unassigned: BlockMember[]
}

export interface BlockTeacherPeriod {
  teacherId: string
  teacherName: string
  validFrom: string
  validTo: string | null
}

export interface BlockMoveItem {
  studentId: string
  fullName: string
  blockId: string
  blockName: string
  fromGroup: string
  toGroup: string
  date: string
  reason: string | null
  movedBy: string | null
}

export interface BlockHistoryItem {
  blockId: string
  blockName: string
  subjectName: string
  groupId: string
  /** Blok guruhining o'quv guruhi — "Sinf va guruhlar" ro'yxatidan ajratish uchun. */
  studyGroupId: string
  groupName: string
  joinedOn: string
  leftOn: string | null
  joinReason: string | null
  leaveReason: string | null
  movedBy: string | null
}

export interface BlockImportResult {
  assigned: number
  moved: number
  unchanged: number
}

export interface BlockImportError {
  row: number
  message: string
}

export const DAY_PATTERN_LABELS: Record<BlockDayPattern, string> = {
  odd: 'Toq kunlar (Du, Chor, Ju)',
  even: 'Juft kunlar (Se, Pay, Sha)',
  custom: 'Cheklovsiz',
}

export const BLOCK_TYPE_LABELS: Record<BlockType, string> = {
  level: "Daraja bo'yicha (Katta / Kichik)",
  base_class: "Manba sinf bo'yicha",
}

/** Server xatosining matni (`{ message }`), bo'lmasa — `fallback`. */
export function apiError(e: unknown, fallback: string): string {
  return (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

const base = '/admin/blocks'

export async function getBlocks(): Promise<Block[]> {
  const { data } = await api.get<Block[]>(base)
  return data
}

export async function getBlock(id: string): Promise<Block> {
  const { data } = await api.get<Block>(`${base}/${id}`)
  return data
}

export async function createBlock(payload: SaveBlockPayload): Promise<Block> {
  const { data } = await api.post<Block>(base, payload)
  return data
}

export async function updateBlock(id: string, payload: SaveBlockPayload): Promise<Block> {
  const { data } = await api.put<Block>(`${base}/${id}`, payload)
  return data
}

export async function deleteBlock(id: string): Promise<void> {
  await api.delete(`${base}/${id}`)
}

/**
 * Faollashtirish. To'siq bo'lsa server 422 va xatolar ro'yxatini qaytaradi — ular bu yerda
 * natija sifatida beriladi (istisno emas).
 */
export async function activateBlock(id: string): Promise<{ isActive: boolean; errors: string[] }> {
  const res = await api.post<{ isActive: boolean; errors: string[] }>(`${base}/${id}/activate`, {}, {
    validateStatus: (s) => s === 200 || s === 422,
  })
  return res.data
}

export async function deactivateBlock(id: string): Promise<void> {
  await api.post(`${base}/${id}/deactivate`, {})
}

export async function getBlockRoster(id: string, date?: string): Promise<BlockRoster> {
  const { data } = await api.get<BlockRoster>(`${base}/${id}/roster`, { params: { date: date || undefined } })
  return data
}

export async function assignToBlock(id: string, studentIds: string[], groupId: string, date?: string): Promise<BlockRoster> {
  const { data } = await api.post<BlockRoster>(`${base}/${id}/assign`, { studentIds, groupId, date: date || null })
  return data
}

export async function moveInBlock(
  id: string, studentIds: string[], groupId: string, date: string, reason?: string,
): Promise<BlockRoster> {
  const { data } = await api.post<BlockRoster>(`${base}/${id}/move`, { studentIds, groupId, date, reason: reason || null })
  return data
}

export async function unassignFromBlock(id: string, studentIds: string[], date: string, reason?: string): Promise<BlockRoster> {
  const { data } = await api.post<BlockRoster>(`${base}/${id}/unassign`, { studentIds, date, reason: reason || null })
  return data
}

export async function changeBlockTeacher(groupId: string, teacherId: string, date: string): Promise<Block> {
  const { data } = await api.post<Block>(`${base}/groups/${groupId}/teacher`, { teacherId, date })
  return data
}

export async function getBlockTeacherHistory(groupId: string): Promise<BlockTeacherPeriod[]> {
  const { data } = await api.get<BlockTeacherPeriod[]>(`${base}/groups/${groupId}/teachers`)
  return data
}

export async function getBlockMoves(from: string, to: string): Promise<BlockMoveItem[]> {
  const { data } = await api.get<BlockMoveItem[]>(`${base}/moves`, { params: { from, to } })
  return data
}

export async function getStudentBlockHistory(studentId: string): Promise<BlockHistoryItem[]> {
  const { data } = await api.get<BlockHistoryItem[]>(`${base}/students/${studentId}/history`)
  return data
}

/** Blok ro'yxati import shaklida (O'quvchi ID, Blok, Guruh, F.I.SH, Manba). */
export async function downloadBlockRoster(id: string, date?: string): Promise<void> {
  const res = await api.get(`${base}/${id}/export`, { params: { date: date || undefined }, responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `blok_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/**
 * Excel'dan taqsimlash — hammasi yoki hech narsa. Xato bo'lsa `errors` qator raqamlari bilan keladi.
 */
export async function importBlocks(
  file: File, date?: string,
): Promise<{ ok: true; result: BlockImportResult } | { ok: false; message: string; errors: BlockImportError[] }> {
  const fd = new FormData()
  fd.append('file', file)
  const res = await api.post(`${base}/import`, fd, {
    params: { date: date || undefined },
    headers: { 'Content-Type': 'multipart/form-data' },
    validateStatus: (s) => s === 200 || s === 400,
  })
  if (res.status === 200) return { ok: true, result: res.data as BlockImportResult }
  const body = res.data as { message?: string; errors?: BlockImportError[] }
  return { ok: false, message: body.message ?? "Import qilib bo'lmadi", errors: body.errors ?? [] }
}

/* ---------------------------------------------------------------------------
 *  Jadval (TZ 6.3): blok kataklari ikkala guruhda bir xil, manba sinf qatorlarida ko'rinadi.
 * ------------------------------------------------------------------------ */

/** Blok katagi: 0 = Dushanba ... 5 = Shanba; dars raqami 1 dan. */
export interface BlockSlot {
  day: number
  period: number
}

/** Manba sinf (yoki yo'nalish) qatorida ko'rinadigan blok katagi. */
export interface BlockOverlayItem extends BlockSlot {
  ownerId: string
  blockId: string
  blockName: string
  subjectName: string
  groups: { name: string; teacherName: string | null }[]
}

export async function getBlockSchedule(id: string): Promise<BlockSlot[]> {
  const { data } = await api.get<BlockSlot[]>(`${base}/${id}/schedule`)
  return data
}

/** Qo'shish va olib tashlash bitta tranzaksiyada. Band bo'lsa server 409 va sababni qaytaradi. */
export async function setBlockSchedule(id: string, add: BlockSlot[], remove: BlockSlot[]): Promise<BlockSlot[]> {
  const { data } = await api.put<BlockSlot[]>(`${base}/${id}/schedule`, { add, remove })
  return data
}

/** Bitta soatni toq yoki juft kunlarga qo'llash. */
export async function applyBlockPattern(id: string, period: number, pattern: 'odd' | 'even'): Promise<BlockSlot[]> {
  const { data } = await api.post<BlockSlot[]>(`${base}/${id}/schedule/pattern`, { period, pattern })
  return data
}

export async function getBlockOverlay(): Promise<BlockOverlayItem[]> {
  const { data } = await api.get<BlockOverlayItem[]>(`${base}/schedule-overlay`)
  return data
}
