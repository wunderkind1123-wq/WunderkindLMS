import { api } from '../client'

// Olimpiada (docs/modules/olympiad.md, 2026-11-16) — `/api/admin/olympiad*`, kalitlar `olympiad` va `olympiadCheckin`.
// Sana: DateOnly "yyyy-MM-dd", TimeOnly "HH:mm:ss", DateTime — mahalliy ISO (zonasiz).

export type EventStatus = 'draft' | 'published' | 'finished' | 'archived'
export type OnFull = 'waitlist' | 'close'
export type RegStatus = 'registered' | 'waitlist' | 'cancelled' | 'attended' | 'no_show'
export type RulesDisplay = 'text' | 'pdf' | 'both'
export type AttachmentKind = 'photo' | 'document' | 'video' | 'video_note'
export type ReminderTarget = 'registered' | 'waitlist' | 'all'

export const EVENT_STATUS: Record<EventStatus, string> = {
  draft: 'Qoralama',
  published: "E'lon qilingan",
  finished: 'Yakunlangan',
  archived: 'Arxiv',
}

export const REG_STATUS: Record<RegStatus, string> = {
  registered: "Ro'yxatda",
  waitlist: 'Kutishda',
  cancelled: 'Bekor qilingan',
  attended: 'Keldi',
  no_show: 'Kelmadi',
}

export const REG_STATUS_TONE: Record<RegStatus, string> = {
  registered: 'bg-brand-50 text-brand-700',
  waitlist: 'bg-amber-50 text-amber-700',
  cancelled: 'bg-slate-100 text-slate-400',
  attended: 'bg-emerald-50 text-emerald-700',
  no_show: 'bg-red-50 text-red-600',
}

export const TARGET_LABEL: Record<ReminderTarget, string> = {
  registered: "Ro'yxatdagilar",
  waitlist: 'Kutishdagilar',
  all: 'Hammasi',
}

export interface OlympiadAttachment {
  kind: AttachmentKind
  url: string
  name: string
}

export interface OlympiadButton {
  text: string
  url: string
}

export interface OlympiadEventInput {
  title: string
  description: string
  date: string
  startTime: string
  endTime: string
  location: string
  locationMapUrl: string
  capacity: number | null
  onFull: OnFull
  registrationOpensAt: string | null
  registrationClosesAt: string | null
  maxScore: number
  showScore: boolean
  showRank: boolean
  showCertificate: boolean
  showInvite: boolean
  rulesText: string
  rulesFileUrl: string
  rulesFileName: string
  rulesDisplay: RulesDisplay
}

export interface OlympiadEvent extends OlympiadEventInput {
  id: string
  code: number
  startsAt: string
  status: EventStatus
  rulesVersion: number
  registered: number
  waitlist: number
  attended: number
  noShow: number
  cancelled: number
  withScore: number
  registrationOpen: boolean
  checkinClosedAt: string | null
  resultsSentAt: string | null
  deepLink: string
  createdAt: string
}

export interface OlympiadRegistration {
  id: string
  eventId: string
  participantId: string
  number: string
  numberValue: number
  status: RegStatus
  waitlistPosition: number
  fullName: string
  phone: string
  grade: number
  school: string
  parentName: string
  parentUsername: string
  hasTelegram: boolean
  createdAt: string
  rulesAcceptedAt: string | null
  rulesVersion: number
  checkedInAt: string | null
  checkedInBy: string
  checkinMethod: string
  score: number | null
  percent: number | null
  rank: number | null
  scoreUpdatedAt: string | null
  scoreUpdatedBy: string
  scoreSource: string
  resultSentAt: string | null
  resultChanged: boolean
  leadId: string | null
  createdVia: string
}

export interface ParticipantInput {
  fullName: string
  phone: string
  grade: number
  school: string
}

export interface OlympiadParticipant {
  id: string
  fullName: string
  phone: string
  grade: number
  school: string
  parentName: string
  parentUsername: string
  hasTelegram: boolean
  events: number
  attended: number
  bestPercent: number | null
  leadId: string | null
  leadConvertedAt: string | null
  createdAt: string
}

export interface ParticipantReg {
  registrationId: string
  eventId: string
  eventTitle: string
  date: string
  number: string
  status: RegStatus
  score: number | null
  maxScore: number
  percent: number | null
  rank: number | null
}

export interface ParticipantCard {
  participant: OlympiadParticipant
  registrations: ParticipantReg[]
}

export interface ImportRow {
  row: number
  number: string
  name: string
  scoreText: string
  registrationId: string | null
  foundName: string | null
  score: number | null
  status: 'ok' | 'warning' | 'error'
  error: string | null
}

export interface ReminderInput {
  offsetMinutes: number | null
  sendAt: string | null
  text: string
  attachments: OlympiadAttachment[]
  target: ReminderTarget
}

export interface OlympiadReminder extends ReminderInput {
  id: string
  kind: 'standard1' | 'standard2' | 'custom'
  dueAt: string | null
  status: 'scheduled' | 'sent' | 'cancelled'
  sentAt: string | null
  sentCount: number
}

export interface Audience {
  eventIds?: string[] | null
  statuses?: string[] | null
  grades?: number[] | null
  schools?: string[] | null
  lead?: 'any' | 'converted' | 'not' | null
}

export type BroadcastStatus = 'draft' | 'scheduled' | 'sending' | 'done' | 'cancelled'

export const BROADCAST_STATUS: Record<BroadcastStatus, string> = {
  draft: 'Qoralama',
  scheduled: 'Rejalashtirilgan',
  sending: 'Yuborilmoqda',
  done: 'Yuborildi',
  cancelled: 'Bekor qilingan',
}

export interface BroadcastInput {
  title: string
  text: string
  attachments: OlympiadAttachment[]
  buttons: OlympiadButton[]
  audience: Audience
}

export interface OlympiadBroadcast extends BroadcastInput {
  id: string
  scheduledAt: string | null
  status: BroadcastStatus
  total: number
  sent: number
  failed: number
  blocked: number
  pending: number
  createdBy: string
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
}

export interface DeliveryRow {
  status: string
  error: string
  sentAt: string | null
  firstName: string
  lastName: string
  username: string
}

export interface OlympiadSettings {
  botConfigured: boolean
  botTokenMasked: string
  botUsername: string
  botName: string
  gradeFrom: number
  gradeTo: number
  defaultOnFull: OnFull
  rulesText: string
  rulesFileUrl: string
  rulesFileName: string
  rulesDisplay: RulesDisplay
  rulesVersion: number
  reminder1Enabled: boolean
  reminder1Minutes: number
  reminder2Enabled: boolean
  reminder2Minutes: number
  resultShowScore: boolean
  resultShowRank: boolean
  resultShowCertificate: boolean
  resultShowInvite: boolean
  inviteText: string
  inviteUrl: string
  certificateFormat: 'pdf' | 'png'
  adminLink: string
  updatedAt: string | null
}

export type SettingsInput = Omit<OlympiadSettings, 'botConfigured' | 'botTokenMasked' | 'botUsername' | 'botName' | 'rulesVersion' | 'updatedAt'>

export interface BotCheck {
  ok: boolean
  error: string | null
  username: string | null
  name: string | null
  mode: string
  pendingUpdates: number
  botUsers: number
  blocked: number
}

export interface OlympiadChannel {
  id: string
  title: string
  chatRef: string
  inviteLink: string
  sortOrder: number
  isActive: boolean
  lastError: string
}

export type ChannelInput = Omit<OlympiadChannel, 'id' | 'lastError'>

export interface OlympiadTemplate {
  key: string
  title: string
  tokens: string
  text: string
  defaultText: string
  customized: boolean
  attachments: OlympiadAttachment[]
}

export interface CheckinEvent {
  id: string
  code: number
  title: string
  date: string
  startTime: string
  endTime: string
  location: string
  checkinClosedAt: string | null
}

export interface CheckinSummary {
  registered: number
  attended: number
  notYet: number
}

export interface CheckinResult {
  outcome: 'ok' | 'already' | 'error'
  message: string
  registration: OlympiadRegistration | null
}

export interface EventStats {
  total: number
  registered: number
  waitlist: number
  attended: number
  attendancePercent: number | null
  byDay: { date: string; count: number }[]
  byGrade: { grade: number; count: number }[]
  bySchool: { school: string; count: number }[]
}

export interface ResultSample {
  registrationId: string
  html: string | null
  buttons: OlympiadButton[] | null
  withCertificate: boolean
  recipients: number
  noTelegram: number
}

export interface ConvertLeadsResult {
  created: number
  linked: number
  skipped: number
  messages: string[]
}

const B = '/admin/olympiad'
const C = '/admin/olympiad-checkin'

/* ---------- yordamchilar ---------- */

function clean<T extends object>(q: T): Partial<T> {
  return Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== null && v !== '')) as Partial<T>
}

/** Fayl nomi `Content-Disposition` dan, bo'lmasa — `fallback`. */
function fileNameOf(header: unknown, fallback: string): string {
  const h = typeof header === 'string' ? header : ''
  const star = /filename\*=UTF-8''([^;]+)/i.exec(h)
  if (star) return decodeURIComponent(star[1])
  const plain = /filename="?([^";]+)"?/i.exec(h)
  return plain ? plain[1] : fallback
}

async function download(url: string, fallback: string, params?: object): Promise<void> {
  const res = await api.get(url, { params: params ? clean(params) : undefined, responseType: 'blob' })
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = fileNameOf(res.headers['content-disposition'], fallback)
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(href)
}

/** "HH:mm:ss" → "HH:mm" */
export const hm = (t: string | null | undefined) => (t ?? '').slice(0, 5)

/** "2026-11-20" → "20.11.2026" */
export function fmtDay(d: string | null | undefined): string {
  if (!d) return '—'
  const [y, m, day] = d.slice(0, 10).split('-')
  return `${day}.${m}.${y}`
}

/** Mahalliy ISO → "20.11.2026 10:00" */
export function fmtDateTime(d: string | null | undefined): string {
  if (!d) return '—'
  return `${fmtDay(d)} ${d.slice(11, 16)}`
}

/** `<input type="datetime-local">` qiymati (soniyasiz) ↔ server. */
export const toLocalInput = (d: string | null | undefined) => (d ? d.slice(0, 16) : '')
export const fromLocalInput = (v: string) => (v ? (v.length === 16 ? `${v}:00` : v) : null)

/* ---------- tadbirlar ---------- */

export async function getEvents(): Promise<OlympiadEvent[]> {
  return (await api.get<OlympiadEvent[]>(`${B}/events`)).data
}
export async function getEvent(id: string): Promise<OlympiadEvent> {
  return (await api.get<OlympiadEvent>(`${B}/events/${id}`)).data
}
export async function createEvent(input: OlympiadEventInput): Promise<OlympiadEvent> {
  return (await api.post<OlympiadEvent>(`${B}/events`, input)).data
}
export async function updateEvent(id: string, input: OlympiadEventInput): Promise<OlympiadEvent> {
  return (await api.put<OlympiadEvent>(`${B}/events/${id}`, input)).data
}
export async function duplicateEvent(id: string): Promise<OlympiadEvent> {
  return (await api.post<OlympiadEvent>(`${B}/events/${id}/duplicate`)).data
}
export async function setEventStatus(id: string, status: EventStatus): Promise<OlympiadEvent> {
  return (await api.post<OlympiadEvent>(`${B}/events/${id}/status`, { status })).data
}
export async function deleteEvent(id: string): Promise<void> {
  await api.delete(`${B}/events/${id}`)
}
export async function getEventStats(id: string): Promise<EventStats> {
  return (await api.get<EventStats>(`${B}/events/${id}/stats`)).data
}

export interface RegFilter {
  status?: string
  grade?: number | string
  school?: string
  q?: string
}

export async function getRegistrations(eventId: string, f: RegFilter = {}): Promise<OlympiadRegistration[]> {
  return (await api.get<OlympiadRegistration[]>(`${B}/events/${eventId}/registrations`, { params: clean(f) })).data
}
export async function addRegistration(
  eventId: string,
  body: { participantId?: string; participant?: ParticipantInput },
): Promise<OlympiadRegistration> {
  return (await api.post<OlympiadRegistration>(`${B}/events/${eventId}/registrations`, body)).data
}
export async function cancelRegistration(id: string): Promise<void> {
  await api.post(`${B}/registrations/${id}/cancel`)
}
export async function promoteRegistration(id: string): Promise<void> {
  await api.post(`${B}/registrations/${id}/promote`)
}
export function exportEvent(eventId: string, kind: 'registrations' | 'results' | 'checkin', f: RegFilter = {}) {
  return download(`${B}/events/${eventId}/export`, `olimpiada-${kind}.xlsx`, { kind, ...f })
}

/* ---------- natijalar ---------- */

export async function setScore(registrationId: string, score: number | null): Promise<OlympiadRegistration> {
  return (await api.put<OlympiadRegistration>(`${B}/registrations/${registrationId}/score`, { score })).data
}
export function downloadResultTemplate(eventId: string) {
  return download(`${B}/events/${eventId}/results/template`, 'natija-shablon.xlsx')
}
export async function previewImport(eventId: string, file: File): Promise<ImportRow[]> {
  const form = new FormData()
  form.append('file', file)
  return (await api.post<ImportRow[]>(`${B}/events/${eventId}/results/preview`, form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })).data
}
export async function confirmImport(eventId: string, rows: { registrationId: string; score: number }[]): Promise<{ saved: number }> {
  return (await api.post<{ saved: number }>(`${B}/events/${eventId}/results/import`, { rows })).data
}
export async function getResultSample(eventId: string): Promise<ResultSample> {
  return (await api.get<ResultSample>(`${B}/events/${eventId}/results/sample`)).data
}
export function downloadCertificate(registrationId: string) {
  return download(`${B}/registrations/${registrationId}/certificate`, 'sertifikat.pdf')
}
export async function sendResults(eventId: string, changedOnly = false): Promise<{ queued: number }> {
  return (await api.post<{ queued: number }>(`${B}/events/${eventId}/results/send`, null, { params: { changedOnly } })).data
}
export async function testResult(registrationId: string): Promise<void> {
  await api.post(`${B}/registrations/${registrationId}/result-test`)
}

/* ---------- eslatmalar ---------- */

export async function getReminders(eventId: string): Promise<OlympiadReminder[]> {
  return (await api.get<OlympiadReminder[]>(`${B}/events/${eventId}/reminders`)).data
}
export async function addReminder(eventId: string, input: ReminderInput): Promise<OlympiadReminder> {
  return (await api.post<OlympiadReminder>(`${B}/events/${eventId}/reminders`, input)).data
}
export async function updateReminder(id: string, input: ReminderInput): Promise<OlympiadReminder> {
  return (await api.put<OlympiadReminder>(`${B}/reminders/${id}`, input)).data
}
export async function toggleReminder(id: string): Promise<void> {
  await api.post(`${B}/reminders/${id}/toggle`)
}
export async function deleteReminder(id: string): Promise<void> {
  await api.delete(`${B}/reminders/${id}`)
}

/* ---------- ishtirokchilar ---------- */

export interface ParticipantFilter {
  q?: string
  grade?: number | string
  school?: string
  lead?: string
  eventId?: string
}

export async function getParticipants(f: ParticipantFilter = {}): Promise<OlympiadParticipant[]> {
  return (await api.get<OlympiadParticipant[]>(`${B}/participants`, { params: clean(f) })).data
}
export async function getSchools(): Promise<string[]> {
  return (await api.get<string[]>(`${B}/participants/schools`)).data
}
export async function getParticipantCard(id: string): Promise<ParticipantCard> {
  return (await api.get<ParticipantCard>(`${B}/participants/${id}`)).data
}
export async function updateParticipant(id: string, input: ParticipantInput): Promise<void> {
  await api.put(`${B}/participants/${id}`, input)
}
export async function convertLeads(participantIds: string[]): Promise<ConvertLeadsResult> {
  return (await api.post<ConvertLeadsResult>(`${B}/participants/convert-leads`, { participantIds })).data
}
export function exportParticipants(f: ParticipantFilter = {}) {
  return download(`${B}/participants/export`, 'olimpiada-ishtirokchilar.xlsx', f)
}

/* ---------- rassilka ---------- */

export async function getBroadcasts(): Promise<OlympiadBroadcast[]> {
  return (await api.get<OlympiadBroadcast[]>(`${B}/broadcasts`)).data
}
export async function createBroadcast(input: BroadcastInput): Promise<OlympiadBroadcast> {
  return (await api.post<OlympiadBroadcast>(`${B}/broadcasts`, input)).data
}
export async function updateBroadcast(id: string, input: BroadcastInput): Promise<OlympiadBroadcast> {
  return (await api.put<OlympiadBroadcast>(`${B}/broadcasts/${id}`, input)).data
}
export async function deleteBroadcast(id: string): Promise<void> {
  await api.delete(`${B}/broadcasts/${id}`)
}
export async function audienceCount(audience: Audience): Promise<number> {
  return (await api.post<{ count: number }>(`${B}/broadcasts/audience-count`, audience)).data.count
}
export async function sendBroadcast(id: string, scheduledAt: string | null): Promise<OlympiadBroadcast> {
  return (await api.post<OlympiadBroadcast>(`${B}/broadcasts/${id}/send`, { scheduledAt })).data
}
export async function cancelBroadcast(id: string): Promise<OlympiadBroadcast> {
  return (await api.post<OlympiadBroadcast>(`${B}/broadcasts/${id}/cancel`)).data
}
export async function getDeliveries(id: string, status?: string): Promise<DeliveryRow[]> {
  return (await api.get<DeliveryRow[]>(`${B}/broadcasts/${id}/deliveries`, { params: clean({ status }) })).data
}

/* ---------- sozlamalar ---------- */

export async function getSettings(): Promise<OlympiadSettings> {
  return (await api.get<OlympiadSettings>(`${B}/settings`)).data
}
export async function saveSettings(input: SettingsInput): Promise<OlympiadSettings> {
  return (await api.put<OlympiadSettings>(`${B}/settings`, input)).data
}
export async function saveBotToken(token: string): Promise<OlympiadSettings> {
  return (await api.put<OlympiadSettings>(`${B}/settings/token`, { token })).data
}
export async function checkBot(): Promise<BotCheck> {
  return (await api.post<BotCheck>(`${B}/settings/check`)).data
}
export async function getChannels(): Promise<OlympiadChannel[]> {
  return (await api.get<OlympiadChannel[]>(`${B}/settings/channels`)).data
}
export async function addChannel(input: ChannelInput): Promise<OlympiadChannel> {
  return (await api.post<OlympiadChannel>(`${B}/settings/channels`, input)).data
}
export async function updateChannel(id: string, input: ChannelInput): Promise<OlympiadChannel> {
  return (await api.put<OlympiadChannel>(`${B}/settings/channels/${id}`, input)).data
}
export async function deleteChannel(id: string): Promise<void> {
  await api.delete(`${B}/settings/channels/${id}`)
}
export async function getTemplates(): Promise<OlympiadTemplate[]> {
  return (await api.get<OlympiadTemplate[]>(`${B}/settings/templates`)).data
}
export async function saveTemplate(key: string, text: string, attachments: OlympiadAttachment[] = []): Promise<void> {
  await api.put(`${B}/settings/templates/${key}`, { text, attachments })
}
/** Matnni joriy xodimning Telegramiga sinov uchun. */
export async function testMessage(text: string, attachments: OlympiadAttachment[] = [], buttons: OlympiadButton[] = []): Promise<void> {
  await api.post(`${B}/settings/test-message`, { text, attachments, buttons })
}
/** Fayl yuklash: rasm ≤ 10 MB, video/fayl ≤ 50 MB. */
export async function uploadMedia(file: File, asKind?: 'video_note' | 'document'): Promise<OlympiadAttachment> {
  const form = new FormData()
  form.append('file', file)
  return (await api.post<OlympiadAttachment>(`${B}/media`, form, {
    params: asKind ? { asKind } : undefined,
    headers: { 'Content-Type': 'multipart/form-data' },
  })).data
}

/* ---------- kirish ---------- */

export async function getCheckinEvents(): Promise<CheckinEvent[]> {
  return (await api.get<CheckinEvent[]>(`${C}/events`)).data
}
export async function getCheckinSummary(eventId: string): Promise<CheckinSummary> {
  return (await api.get<CheckinSummary>(`${C}/events/${eventId}/summary`)).data
}
export async function searchCheckin(eventId: string, q: string): Promise<OlympiadRegistration[]> {
  return (await api.get<OlympiadRegistration[]>(`${C}/events/${eventId}/search`, { params: { q } })).data
}
export async function scanQr(eventId: string, token: string): Promise<CheckinResult> {
  return (await api.post<CheckinResult>(`${C}/scan`, { eventId, token })).data
}
export async function checkinManual(registrationId: string): Promise<CheckinResult> {
  return (await api.post<CheckinResult>(`${C}/registrations/${registrationId}/checkin`)).data
}
export async function undoCheckin(registrationId: string): Promise<OlympiadRegistration> {
  return (await api.post<OlympiadRegistration>(`${C}/registrations/${registrationId}/undo`)).data
}
export async function finishCheckin(eventId: string): Promise<CheckinSummary> {
  return (await api.post<CheckinSummary>(`${C}/events/${eventId}/finish`)).data
}
