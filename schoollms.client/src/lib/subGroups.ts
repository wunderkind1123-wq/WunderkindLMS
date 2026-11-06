/**
 * Dars yarmining nomi (`subGroup` 1/2).
 *
 * Sinfda — "1-guruh" / "2-guruh". Yo'nalish guruhida — "Katta guruh" / "Kichik guruh": yo'nalish
 * ikkiga bo'linadi va har yarmiga o'z o'qituvchisi biriktiriladi (mijoz, 2026-11-06). Yarmi —
 * o'quvchining `subGroup` qiymati, server bilan bir xil (`LessonOwner.HasHalves`).
 */
export function subGroupLabel(subGroup: number, isTrack: boolean): string {
  if (!isTrack) return `${subGroup}-guruh`
  return subGroup === 1 ? 'Katta guruh' : 'Kichik guruh'
}

/** Jadval katagidagi qisqa nishon: sinfda "G1"/"G2", yo'nalishda "Katta"/"Kichik". */
export function subGroupBadge(subGroup: number, isTrack: boolean): string {
  if (!isTrack) return `G${subGroup}`
  return subGroup === 1 ? 'Katta' : 'Kichik'
}
