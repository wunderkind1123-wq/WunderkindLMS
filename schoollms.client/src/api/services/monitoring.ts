import { api } from '../client'
import { MONTH_NAMES } from '@/pages/admin/seasonal-marks/periods'

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
  /** Variant rasmlari ("" — rasmsiz) yoki null. */
  optionImages: string[] | null
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
  optionImages?: string[] | null
  correctIndex: number
  topicId?: string | null
  weight?: number
}

/** "Noyabr 2026" — the month an oylik test's result is written to (Mavsumiy baho). */
export function seasonalLabel(year: number | null, month: number | null): string | null {
  return year && month ? `${MONTH_NAMES[month - 1] ?? ''} ${year}` : null
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
  /** Oylik test: natija shu oyning Mavsumiy bahosiga yoziladi. */
  seasonalYear: number | null
  seasonalMonth: number | null
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
  seasonalYear?: number | null
  seasonalMonth?: number | null
  /** Onlayn: har o'quvchiga savollar va variantlar o'z tartibida. */
  shuffle?: boolean
  /** Onlayn: test oynasidan necha marta chiqishga ruxsat; null — cheklanmagan (faqat qayd). */
  maxLeaves?: number | null
}

export interface MonitoringItem {
  id: string
  order: number
  text: string | null
  imageUrl: string | null
  options: string[]
  optionImages: string[] | null
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
  /** Test oynasidan chiqishlar soni. */
  leaves: number
  /** "2026-11-24T09:15:02Z blur" ko'rinishidagi jurnal. */
  events: string[] | null
  finishReason: 'time' | 'leaves' | null
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
  seasonalYear: number | null
  seasonalMonth: number | null
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
  shuffle: boolean
  maxLeaves: number | null
  roomPin: string | null
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

/* ------------------------------------------------ savollar banki: o'quv bo'limi va ustoz (2026-11-24) */

/** Bank sahifalari kim uchun: o'quv bo'limi (`/admin/monitoring`) yoki ustoz (`/teacher/savollar`). */
export type BankScope = 'admin' | 'teacher'

export interface MonitoringImportPreview {
  /** Hali saqlanmagan savollar — `topicId`/`weight` bo'sh. */
  questions: MonitoringQuestionPayload[]
  /** Tushib qolgan savollar va sababi, o'zbekcha. */
  problems: string[]
}

export function bankApi(scope: BankScope) {
  const banks = scope === 'admin' ? `${BASE}/banks` : '/teacher/question-banks'
  const optionsUrl = scope === 'admin' ? `${BASE}/options` : `${banks}/options`
  return {
    /** Ro'yxat va bitta bank sahifasining manzili (SPA). */
    listPath: scope === 'admin' ? '/admin/monitoring/banks' : '/teacher/savollar',
    templatePath: scope === 'admin' ? `${BASE}/question-template` : `${banks}/question-template`,
    /** Ustoz test tuzadigan Word namuna (.docx). */
    wordTemplatePath: scope === 'admin' ? `${BASE}/word-template` : `${banks}/word-template`,
    getOptions: async () => (await api.get<MonitoringOptions>(optionsUrl)).data,
    getBanks: async () => (await api.get<MonitoringBank[]>(banks)).data,
    getBank: async (id: string) => (await api.get<MonitoringBankDetail>(`${banks}/${id}`)).data,
    saveBank: async (id: string | null, p: { title: string; subjectId: string; grade?: number | null; groupId?: string | null }) =>
      (id ? await api.put<{ id: string }>(`${banks}/${id}`, p) : await api.post<{ id: string }>(banks, p)).data,
    deleteBank: async (id: string) => { await api.delete(`${banks}/${id}`) },
    saveQuestion: async (bankId: string, id: string | null, p: MonitoringQuestionPayload) =>
      (id ? await api.put(`${banks}/${bankId}/questions/${id}`, p) : await api.post(`${banks}/${bankId}/questions`, p)).data,
    deleteQuestion: async (bankId: string, id: string) => { await api.delete(`${banks}/${bankId}/questions/${id}`) },
    importXlsx: async (bankId: string, file: File) => {
      const fd = new FormData()
      fd.append('file', file)
      return (await api.post<{ added: number }>(`${banks}/${bankId}/import`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })).data
    },
    /** Word (.docx) / .txt fayl yoki qo'yilgan matn → oldindan ko'rish (saqlanmaydi). */
    previewImport: async (bankId: string, input: { file?: File; text?: string }) => {
      const fd = new FormData()
      if (input.file) fd.append('file', input.file)
      if (input.text) fd.append('text', input.text)
      return (await api.post<MonitoringImportPreview>(`${banks}/${bankId}/import/preview`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })).data
    },
    /** Savol rasmi (PNG/JPG/GIF/WEBP, 5 MB gacha) → /uploads manzili. */
    uploadImage: async (bankId: string, file: File) => {
      const fd = new FormData()
      fd.append('file', file)
      return (await api.post<{ url: string }>(`${banks}/${bankId}/images`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })).data.url
    },
    addQuestions: async (bankId: string, rows: MonitoringQuestionPayload[]) =>
      (await api.post<{ added: number }>(`${banks}/${bankId}/questions/bulk`, rows)).data,
  }
}

export type BankApi = ReturnType<typeof bankApi>

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
export const reopenParticipant = async (participantId: string) => { await api.post(`${BASE}/participants/${participantId}/reopen`) }

export const EVENT_LABEL: Record<string, string> = {
  hidden: 'Boshqa tab / oyna',
  blur: 'Boshqa dastur',
  fullscreen: "To'liq ekrandan chiqdi",
  copy: 'Nusxa olishga urindi',
  paste: "Qo'yishga urindi",
  print: 'Chop etish / skrinshot',
  devtools: 'Dasturchi oynasi',
  reopened: "O'quv bo'limi davom ettirishga ruxsat berdi",
}

export function monitoringError(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  const res = (err as { response?: { status?: number; data?: { message?: string } } })?.response
  if (!res) return 'Serverga ulanib bo‘lmadi — internetni tekshirib, qayta bosing'
  if (res.status === 429) return 'Hozir juda ko‘p o‘quvchi kirmoqda — 10 soniyadan keyin qayta bosing'
  if (res.status && res.status >= 500 && !res.data?.message) return 'Serverda vaqtinchalik xatolik — bir ozdan keyin qayta bosing'
  return res.data?.message ?? fallback
}

export const letter = (i: number) => String.fromCharCode(65 + i)

/* ------------------------------------------------ public (o'quvchi, kod bilan) */

export interface MonitoringSession {
  studentName: string
  title: string
  subjectName: string
  deadline: string
  answers: string
  /** Ekrandagi tartibda. options/optionImages/keys — o'quvchining variant tartibi; keys[k] — asl harf. */
  items: {
    order: number
    text: string | null
    imageUrl: string | null
    options: string[]
    optionCount: number
    optionImages: string[] | null
    keys: string[] | null
  }[]
  finished: boolean
  leaves: number
  maxLeaves: number | null
  finishReason: 'time' | 'leaves' | null
  testId: string | null
  /** Sessiyadagi fan — tugagach navbatdagisi ko'rsatiladi. */
  inSitting: boolean
  /** Server vaqti — taymer kompyuter soatiga emas, shunga qarab sanaydi. */
  serverNow: string | null
  /** Topshirilgach: to'g'ri javoblar soni va foiz (qaysi savol xato — ko'rsatilmaydi). */
  resultCorrect: number | null
  resultPercent: number | null
}

/** Server vaqti va javob vaqti (ms) — "Kompyuterni tekshirish" uchun. */
export const getServerTime = async () => {
  const t0 = performance.now()
  const { data } = await api.get<{ now: string }>('/public/monitoring/time')
  return { serverNow: new Date(data.now).getTime(), roundTripMs: Math.round(performance.now() - t0) }
}

export type IntegrityEvent = 'hidden' | 'blur' | 'fullscreen' | 'copy' | 'paste' | 'print' | 'devtools'

export const sendSessionEvent = async (code: string, device: string, kind: IntegrityEvent, testId?: string | null, pin?: string) =>
  (await api.post<{ leaves: number; maxLeaves: number | null; finished: boolean }>('/public/monitoring/event', { code, device, kind, testId, pin })).data

/** "Tirikman" — 30 soniyada bir; finished — test yopilgan yoki vaqt tugagan. */
export const pingSession = async (code: string, device: string, testId?: string | null, pin?: string) =>
  (await api.post<{ leaves: number; maxLeaves: number | null; finished: boolean }>('/public/monitoring/ping', { code, device, testId, pin })).data

/* ------------------------------------------------ jonli kuzatuv */

export type LiveStatus = 'pending' | 'in_progress' | 'between' | 'done' | 'absent'

export interface MonitoringLiveRow {
  studentId: string
  studentName: string
  ownerName: string
  status: LiveStatus
  subject: string | null
  step: number
  steps: number
  answered: number
  total: number
  leaves: number
  stopped: boolean
  lastSeenAt: string | null
  deadline: string | null
}

export interface MonitoringLive {
  title: string
  now: string
  subjects: string[]
  rows: MonitoringLiveRow[]
  roomPin: string | null
}

export interface MonitoringCheck {
  level: 'error' | 'warning' | 'ok'
  message: string
}

export const getReadiness = async (kind: 'sitting' | 'test', id: string) =>
  (await api.get<MonitoringCheck[]>(`${BASE}/${kind === 'sitting' ? 'sittings' : 'tests'}/${id}/readiness`)).data

export const getSittingLive = async (id: string) => (await api.get<MonitoringLive>(`${BASE}/sittings/${id}/live`)).data
export const getTestLive = async (id: string) => (await api.get<MonitoringLive>(`${BASE}/tests/${id}/live`)).data

/* ------------------------------------------------ test sessiyasi (bir o'tirishda bir nechta fan) */

export interface MonitoringSittingTest {
  id: string
  title: string
  subjectName: string
  status: MonitoringStatus
  itemCount: number
  timeLimitMin: number | null
  participants: number
  done: number
}

export interface MonitoringSittingListItem {
  id: string
  title: string
  testDate: string
  tests: MonitoringSittingTest[]
  pupils: number
  finished: number
}

export interface MonitoringSittingRow {
  codeId: string
  studentId: string
  studentName: string
  ownerName: string
  code: string
  deviceBound: boolean
  /** Har test uchun holat yoki "" — bu fanda yo'q. */
  steps: (ParticipantStatus | '')[]
  leaves: number
}

export interface MonitoringSittingDetail {
  id: string
  title: string
  testDate: string
  tests: MonitoringSittingTest[]
  rows: MonitoringSittingRow[]
  roomPin: string | null
}

export const getSittings = async () => (await api.get<MonitoringSittingListItem[]>(`${BASE}/sittings`)).data
export const getSitting = async (id: string) => (await api.get<MonitoringSittingDetail>(`${BASE}/sittings/${id}`)).data
export const createSitting = async (p: { title: string; testDate: string; testIds: string[] }) =>
  (await api.post<{ id: string }>(`${BASE}/sittings`, p)).data
export const deleteSitting = async (id: string) => { await api.delete(`${BASE}/sittings/${id}`) }

export interface MonitoringQuickSittingPayload {
  title: string
  testDate: string
  opensAt: string
  closesAt: string
  owners: { kind: string; id: string }[]
  /** questionCount — bankdan tasodifiy shuncha savol; null — hammasi. */
  subjects: { bankId: string; questionCount: number | null; timeLimitMin: number }[]
  shuffle: boolean
  maxLeaves: number | null
  seasonalYear: number | null
  seasonalMonth: number | null
  publish: boolean
}

export const quickCreateSitting = async (p: MonitoringQuickSittingPayload) =>
  (await api.post<{ id: string }>(`${BASE}/sittings/quick`, p)).data
export const resetSittingDevice = async (codeId: string) => { await api.post(`${BASE}/sitting-codes/${codeId}/reset-device`) }
export const rotatePin = async (kind: 'sitting' | 'test', id: string) =>
  (await api.post<{ pin: string }>(`${BASE}/${kind === 'sitting' ? 'sittings' : 'tests'}/${id}/pin`)).data.pin

/* ------------------------------------------------ doimiy shaxsiy kodlar (§2.9) */

export interface MonitoringStudentCode {
  studentId: string
  studentName: string
  className: string
  code: string | null
}

export const getStudentCodes = async (className?: string) =>
  (await api.get<MonitoringStudentCode[]>(`${BASE}/student-codes`, { params: className ? { className } : {} })).data
export const generateStudentCodes = async () => (await api.post<{ added: number }>(`${BASE}/student-codes/generate`)).data.added
export const regenerateStudentCode = async (studentId: string) =>
  (await api.post<{ code: string }>(`${BASE}/student-codes/${encodeURIComponent(studentId)}/regenerate`)).data.code

export interface MonitoringPeek {
  studentName: string
  ownerName: string
  title: string
  subjectName: string
  itemCount: number
  /** Qolgan (yoki boshlansa beriladigan) daqiqa. */
  timeLimitMin: number
  inProgress: boolean
  maxLeaves: number | null
  testId: string | null
  /** Sessiya kodi: bir o'tirishda bir nechta fan. */
  sittingTitle: string | null
  steps: { testId: string; title: string; subjectName: string; status: 'done' | 'current' | 'waiting' | 'absent'; percent: number | null }[] | null
  /** Sessiyadagi hamma fan topshirilgan. */
  allDone: boolean
}

/** pin — doimiy kod bilan kirganda doskadagi xona PIN'i (bir martalik kodda bo'sh). */
export const checkSession = async (code: string, device: string, pin?: string) =>
  (await api.post<MonitoringPeek>('/public/monitoring/check', { code, device, pin })).data
export const startSession = async (code: string, device: string, testId?: string | null, pin?: string) =>
  (await api.post<MonitoringSession>('/public/monitoring/start', { code, device, testId, pin })).data
/** Javob serverga yetmadi (yoki javobni bizning server emas, boshqa narsa qaytardi) — navbatda qoladi. */
export class AnswerNotSavedError extends Error {}

export const saveSessionAnswer = async (code: string, device: string, order: number, choice: string | null, testId?: string | null, pin?: string) => {
  const res = await api.put<{ saved?: number }>('/public/monitoring/answer', { code, device, order, choice, testId, pin })
  // only our server's acknowledgement counts — a Wi-Fi login page or a proxy can answer "200" too
  if (res.data?.saved !== order) throw new AnswerNotSavedError('no acknowledgement')
}
export const finishSession = async (code: string, device: string, testId?: string | null, pin?: string) =>
  (await api.post<MonitoringSession>('/public/monitoring/finish', { code, device, testId, pin })).data
