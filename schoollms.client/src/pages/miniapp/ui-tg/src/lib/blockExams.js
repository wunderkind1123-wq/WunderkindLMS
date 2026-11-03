/**
 * Blok test natijalari — o'qituvchi ekranining yordamchilari (mijoz, 2026-11-03).
 *
 * `pages/teacher/ui-web/src/lib/blockExams.js` ning nusxasi (har ilova alohida yig'iladi, `seasonal.js`
 * kabi). Faqat kiritish gigiyenasi — ballni server hisoblaydi.
 */

/** DTM bo'limi (30 × 3,1): katakka to'g'ri javoblar soni yoziladi, ball emas. */
export function isWeighted(column) {
  return column.pointsPerCorrect != null && column.questionCount != null
}

function storedValue(row, column) {
  const value = isWeighted(column) ? row.correct?.[column.sectionId] : row.scores?.[column.sectionId]
  return value ?? null
}

export function formatNumber(value) {
  return String(value).replace('.', ',')
}

/** "1-blok · Tarix", ustun tagiga "30 savol × 3,1". */
export function columnTitle(column) {
  return column.block ? `${column.block}-blok · ${column.name}` : column.name
}

export function columnHint(column) {
  return isWeighted(column)
    ? `${column.questionCount} savol × ${formatNumber(column.pointsPerCorrect)}`
    : `maks. ${formatNumber(column.maxScore)}`
}

function parseCell(text) {
  const t = String(text ?? '').trim().replace(',', '.')
  if (!t) return null
  return /^\d+(\.\d{1,2})?$/.test(t) ? Number(t) : Number.NaN
}

export function cellText(row, column, drafts) {
  const typed = drafts[row.participantId]?.[column.sectionId]
  if (typed !== undefined) return typed
  const stored = storedValue(row, column)
  return stored === null ? '' : formatNumber(stored)
}

export const rowLocked = (row) => row.status === 'absent' || row.status === 'cancelled' || row.status === 'in_progress'

export const creditOf = (row, column) => row.credits?.[column.sectionId] ?? null

/** Katakni nega saqlab bo'lmaydi — yoki `null`. */
export function cellProblem(row, column, text) {
  const value = parseCell(text)
  if (value === null) {
    return storedValue(row, column) === null ? null : "Bo'sh qoldirib bo'lmaydi — 0 kiriting"
  }
  if (Number.isNaN(value)) return 'Son kiriting'
  if (isWeighted(column)) {
    return Number.isInteger(value) && value <= column.questionCount
      ? null
      : `To'g'ri javoblar 0 dan ${column.questionCount} gacha butun son`
  }
  return value <= column.maxScore ? null : `Ball 0 dan ${formatNumber(column.maxScore)} gacha`
}

/** Tahrir holati: xatolar soni va faqat o'zgargan qatorlardan iborat so'rov. */
export function analyse(table, drafts) {
  const problems = new Map()
  const rows = []
  for (const row of table.rows) {
    if (rowLocked(row)) continue
    const scores = []
    for (const column of table.columns) {
      if (creditOf(row, column) || drafts[row.participantId]?.[column.sectionId] === undefined) continue
      const text = drafts[row.participantId][column.sectionId]
      const problem = cellProblem(row, column, text)
      if (problem) {
        problems.set(`${row.participantId}:${column.sectionId}`, problem)
        continue
      }
      const value = parseCell(text)
      if (value === null || value === storedValue(row, column)) continue
      scores.push(isWeighted(column) ? { sectionId: column.sectionId, correct: value } : { sectionId: column.sectionId, points: value })
    }
    if (scores.length > 0) rows.push({ participantId: row.participantId, scores, absent: false })
  }
  return { problems, rows }
}

function serverMessage(err) {
  const m = err && err.body && typeof err.body.message === 'string' ? err.body.message.trim() : ''
  if (!m || m.startsWith('<') || m.length > 300) return null
  return m
}

/** `ApiError` → bitta o'zbekcha gap; serverning `{ message }` i ustun. */
export function blockExamErrorMessage(err) {
  const status = err && typeof err.status === 'number' ? err.status : -1
  if (status === 0) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
  if (status >= 500) return "Serverda xatolik. Birozdan so'ng qayta urinib ko'ring."
  return serverMessage(err) || err?.message || 'Xatolik yuz berdi'
}
