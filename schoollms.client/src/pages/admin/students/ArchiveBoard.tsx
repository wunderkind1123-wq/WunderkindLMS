import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import { Phone, RotateCcw, Search, Tag, UserPlus } from 'lucide-react'
import {
  getArchiveBoard,
  returnStudentToLeads,
  setStudentArchiveReason,
  type ArchiveBoard as Board,
  type ArchiveBoardColumn,
  type ArchiveBoardStudent,
} from '@/api/services/archiveReasons'
import { restoreStudent } from '@/api/services/students'
import { stageColors } from '@/config/stageColors'
import { cn, formatDate } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { ArchiveReasonSelect } from './ArchiveReasonSelect'

/* =========================================================================
 *  Arxiv doskasi (mijoz, 2026-11-13): "arxivga kirganda ularni sabablarga
 *  ko'ra ajratib olishimiz uchun ... lidlarga o'xshab ustun".
 *
 *  USTUN = SABAB GURUHI ("O'zi ketdi", "Maktab chiqardi", …) — Sozlamalarda
 *  maktabning o'zi boshqaradi. Birinchi ustun tizimniki: "Sababi aniqlanmagan"
 *  (sababsiz yoki guruhsiz sabab bilan arxivlanganlar) — eski arxivni tartibga
 *  keltirish shu ustundan boshlanadi. Bo'sh bo'lsa server uni qaytarmaydi
 *  (2026-11-17) — kimdir guruhsiz sabab bilan arxivlansa, qayta chiqadi.
 *
 *  Kartochkani boshqa ustunga sudrash = sababni o'zgartirish: o'sha guruhning
 *  aniq sababi so'raladi (guruh sababdan kelib chiqadi, aksi emas). Ustun
 *  ichidagi sabab chiplari — shu ustunni sabab bo'yicha filtrlash.
 *
 *  "Lidga qaytarish" — o'quvchi lidlar doskasining birinchi ustuniga tushadi,
 *  o'zi arxivda qoladi; arxivdan qaytarilganda lid o'zi yopiladi.
 *  docs/modules/archive-reason-groups.md
 * ========================================================================= */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/** `column = null` — kartochkadagi "Sabab" tugmasi: hamma guruhlarning sabablari. */
type MoveTarget = { student: ArchiveBoardStudent; column: ArchiveBoardColumn | null }

export function ArchiveBoard({ onChanged }: { onChanged?: () => void }) {
  const [board, setBoard] = useState<Board | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [reasonFilter, setReasonFilter] = useState<Record<string, string>>({})
  const [banner, setBanner] = useState<string | null>(null)

  const [move, setMove] = useState<MoveTarget | null>(null)
  const [moveReason, setMoveReason] = useState('')
  const [moveNote, setMoveNote] = useState('')

  const [toLead, setToLead] = useState<ArchiveBoardStudent | null>(null)
  const [leadNote, setLeadNote] = useState('')

  const [busy, setBusy] = useState(false)
  const [modalError, setModalError] = useState<string | null>(null)

  const load = useCallback(() => {
    getArchiveBoard()
      .then((b) => {
        setBoard(b)
        setError(null)
      })
      .catch((e) => setError(errorText(e, "Arxivni yuklab bo'lmadi")))
  }, [])

  useEffect(load, [load])

  // Kichik siljish bosishni sudrashdan ajratadi — kartochkadagi tugmalar ishlasin.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const columnKey = (c: ArchiveBoardColumn) => c.id ?? 'unassigned'

  const onDragEnd = (e: DragEndEvent) => {
    if (!board || !e.over) return
    const target = board.columns.find((c) => columnKey(c) === e.over!.id)
    const student = board.columns.flatMap((c) => c.students).find((s) => s.id === e.active.id)
    if (!target || !student) return
    const from = board.columns.find((c) => c.students.some((s) => s.id === student.id))
    if (from && columnKey(from) === columnKey(target)) return
    // "Sababi aniqlanmagan"ga qaytarib bo'lmaydi — sababni bilib turib "bilmayman" qilish ma'nosiz.
    if (target.id === null) {
      setBanner("\"Sababi aniqlanmagan\" ustuniga o'tkazib bo'lmaydi — aniq sabab tanlang.")
      return
    }
    openMove(student, target)
  }

  const openMove = (student: ArchiveBoardStudent, column: ArchiveBoardColumn | null) => {
    setMove({ student, column })
    setMoveReason('')
    setMoveNote('')
    setModalError(null)
  }

  const saveMove = async () => {
    if (!move || !moveReason) return
    setBusy(true)
    setModalError(null)
    try {
      await setStudentArchiveReason(move.student.id, moveReason, moveNote)
      setMove(null)
      load()
      onChanged?.()
    } catch (e) {
      setModalError(errorText(e, "Sababni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const saveLead = async () => {
    if (!toLead) return
    setBusy(true)
    setModalError(null)
    try {
      await returnStudentToLeads(toLead.id, leadNote)
      setBanner(`"${toLead.fullName}" lidlarga qaytarildi — Lidlar doskasining birinchi ustunida.`)
      setToLead(null)
      load()
    } catch (e) {
      setModalError(errorText(e, "Lid yaratib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const restore = async (s: ArchiveBoardStudent) => {
    if (!confirm(`"${s.fullName}" o'quvchini arxivdan qaytarish? Login bloklangicha qoladi — keyin parol generatsiya qiling.`))
      return
    try {
      await restoreStudent(s.id)
      setBanner(
        `"${s.fullName}" arxivdan qaytarildi` + (s.leadId ? ' — uning lidi yopildi.' : '.') + ' Uni sinfga joylashtiring.',
      )
      load()
      onChanged?.()
    } catch (e) {
      alert(errorText(e, "Qaytarib bo'lmadi"))
    }
  }

  const q = query.trim().toLowerCase()
  const columns = useMemo(
    () =>
      (board?.columns ?? []).map((c) => {
        const key = columnKey(c)
        const reasons = new Map<string, number>()
        for (const s of c.students) {
          const name = s.reasonName ?? 'Sababsiz'
          reasons.set(name, (reasons.get(name) ?? 0) + 1)
        }
        const only = reasonFilter[key]
        const shown = c.students.filter(
          (s) =>
            (!q || s.fullName.toLowerCase().includes(q) || (s.className ?? '').toLowerCase().includes(q)) &&
            (!only || (s.reasonName ?? 'Sababsiz') === only),
        )
        return { column: c, key, reasons: [...reasons].sort((a, b) => b[1] - a[1]), shown }
      }),
    [board, q, reasonFilter],
  )

  if (error) return <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
  if (!board) return <Loader label="Yuklanmoqda..." />

  const unassigned = board.columns.find((c) => c.id === null)?.students.length ?? 0

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ism yoki sinf bo'yicha qidirish"
            className="w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
        </div>
        <p className="text-sm text-slate-400">
          Arxivda {board.total} ta
          {unassigned > 0 && (
            <span className="text-amber-600"> · {unassigned} tasining sababi aniqlanmagan — ularni ustunlarga suring</span>
          )}
        </p>
      </div>

      {banner && (
        <div className="flex items-center gap-2 rounded-xl bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700">
          {banner}
          <button type="button" onClick={() => setBanner(null)} className="ml-auto text-xs text-emerald-600 hover:underline">
            Yopish
          </button>
        </div>
      )}

      <DndContext sensors={sensors} onDragEnd={onDragEnd}>
        <div className="flex gap-3 overflow-x-auto pb-3">
          {columns.map(({ column, key, reasons, shown }) => (
            <BoardColumn
              key={key}
              id={key}
              column={column}
              reasons={reasons}
              activeReason={reasonFilter[key]}
              onReason={(name) =>
                setReasonFilter((f) => ({ ...f, [key]: f[key] === name ? '' : name }))
              }
            >
              {shown.map((s) => (
                <BoardCard
                  key={s.id}
                  student={s}
                  onToLead={() => {
                    setToLead(s)
                    setLeadNote('')
                    setModalError(null)
                  }}
                  onRestore={() => restore(s)}
                  onChangeReason={() => openMove(s, null)}
                />
              ))}
              {shown.length === 0 && (
                <p className="py-6 text-center text-xs text-slate-400">
                  {column.students.length === 0 ? "Bo'sh" : 'Mos kelmadi'}
                </p>
              )}
            </BoardColumn>
          ))}
        </div>
      </DndContext>

      {/* Sabab tanlash — sudralganda yoki "Sababni o'zgartirish" bosilganda */}
      <Modal
        open={move !== null}
        onClose={() => setMove(null)}
        title={move ? `Ketish sababi — ${move.student.fullName}` : ''}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setMove(null)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={saveMove} disabled={busy || !moveReason}>
              {busy ? 'Saqlanmoqda…' : 'Saqlash'}
            </Button>
          </>
        }
      >
        {move && (
          <div className="space-y-3 text-sm text-slate-600">
            {move.column?.id && (
              <p>
                <span className="font-medium text-slate-800">{move.column.name}</span> guruhidagi aniq sababni tanlang.
              </p>
            )}
            {move.student.reasonName && (
              <p className="text-xs text-slate-400">Hozirgi sabab: {move.student.reasonName}</p>
            )}
            <ArchiveReasonSelect
              value={moveReason}
              onChange={setMoveReason}
              onlyGroupId={move.column?.id ?? undefined}
              reload={move}
            />
            <Textarea
              label="Izoh (ixtiyoriy)"
              rows={2}
              value={moveNote}
              onChange={(e) => setMoveNote(e.target.value)}
              placeholder={move.student.note ?? "Bo'sh qoldirilsa eski izoh qoladi"}
            />
            {modalError && <p className="text-xs text-red-600">{modalError}</p>}
          </div>
        )}
      </Modal>

      {/* Lidlarga qaytarish */}
      <Modal
        open={toLead !== null}
        onClose={() => setToLead(null)}
        title="Lidlarga qaytarish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setToLead(null)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={saveLead} disabled={busy}>
              <UserPlus className="h-4 w-4" /> {busy ? 'Yaratilmoqda…' : 'Lid yaratish'}
            </Button>
          </>
        }
      >
        {toLead && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{toLead.fullName}</span> lidlar doskasining birinchi
              ustuniga tushadi — qayta qo'ng'iroq qilib, qaytarishga urinish uchun. O'quvchi arxivda qoladi.
            </p>
            <ul className="ml-5 list-disc space-y-0.5 text-xs text-slate-500">
              <li>Lid izohiga ketish sanasi, sinfi va sababi yoziladi.</li>
              <li>Ota-ona rozi bo'lsa, o'quvchini shu yerdan "Arxivdan qaytarish" bilan qaytaring — lid o'zi yopiladi.</li>
            </ul>
            <Textarea
              label="Lid uchun izoh (ixtiyoriy)"
              rows={2}
              value={leadNote}
              onChange={(e) => setLeadNote(e.target.value)}
              placeholder="masalan: chegirma taklif qilish mumkin"
            />
            {modalError && <p className="text-xs text-red-600">{modalError}</p>}
          </div>
        )}
      </Modal>
    </div>
  )
}

function BoardColumn({
  id,
  column,
  reasons,
  activeReason,
  onReason,
  children,
}: {
  id: string
  column: ArchiveBoardColumn
  reasons: [string, number][]
  activeReason?: string
  onReason: (name: string) => void
  children: React.ReactNode
}) {
  const c = stageColors[column.color] ?? stageColors.slate
  const { setNodeRef, isOver } = useDroppable({ id })
  const system = column.id === null

  return (
    <div className="flex w-72 shrink-0 flex-col overflow-hidden rounded-2xl border border-slate-200">
      <div className={cn('h-1', system ? 'bg-amber-400' : c.bar)} />
      <div className={cn('flex flex-1 flex-col p-3', system ? 'bg-amber-50/60' : c.tint)}>
        <div className="mb-2 flex min-w-0 items-center gap-2">
          <span className={cn('h-2.5 w-2.5 shrink-0 rounded-full', system ? 'bg-amber-400' : c.dot)} />
          <h3 className="truncate text-sm font-semibold text-slate-700" title={column.name}>
            {column.name}
          </h3>
          <span className={cn('shrink-0 rounded-full px-1.5 py-0.5 text-xs font-medium', system ? 'bg-amber-100 text-amber-700' : c.badge)}>
            {column.students.length}
          </span>
        </div>

        {reasons.length > 1 && (
          <div className="mb-2 flex flex-wrap gap-1">
            {reasons.map(([name, n]) => (
              <button
                key={name}
                type="button"
                onClick={() => onReason(name)}
                className={cn(
                  'rounded-full border px-2 py-0.5 text-[11px] transition-colors',
                  activeReason === name
                    ? 'border-slate-700 bg-slate-700 text-white'
                    : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300',
                )}
              >
                {name} · {n}
              </button>
            ))}
          </div>
        )}

        <div
          ref={setNodeRef}
          className={cn('min-h-[140px] flex-1 space-y-2 rounded-xl p-1 transition', isOver && cn('ring-2', c.ring))}
        >
          {children}
        </div>
      </div>
    </div>
  )
}

function BoardCard({
  student: s,
  onToLead,
  onRestore,
  onChangeReason,
}: {
  student: ArchiveBoardStudent
  onToLead: () => void
  onRestore: () => void
  onChangeReason: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: s.id })
  const style = transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className={cn(
        'cursor-grab rounded-xl border border-slate-200 bg-white p-3 shadow-sm active:cursor-grabbing',
        isDragging && 'relative z-10 opacity-90 shadow-lg',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <Link
          to={`/admin/students/${s.id}`}
          className="min-w-0 text-sm font-medium text-slate-800 hover:text-brand-700 hover:underline"
          onPointerDown={(e) => e.stopPropagation()}
        >
          {s.fullName}
        </Link>
        {s.leadId && (
          <span className="shrink-0 rounded-md bg-blue-50 px-1.5 py-0.5 text-[11px] font-medium text-blue-600">Lidda</span>
        )}
      </div>
      <p className="mt-0.5 text-xs text-slate-400">
        {s.className ?? 'Sinfsiz'} · {s.archivedAt ? formatDate(s.archivedAt) : '—'}
      </p>
      <p className="mt-1.5 inline-flex items-center gap-1 rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-600">
        <Tag className="h-3 w-3" /> {s.reasonName ?? 'Sababsiz'}
      </p>
      {s.note && (
        <p className="mt-1 line-clamp-2 text-xs text-slate-500" title={s.note}>
          {s.note}
        </p>
      )}
      {s.parentPhone && (
        <p className="mt-1 inline-flex items-center gap-1 text-xs text-slate-500">
          <Phone className="h-3 w-3" /> {s.parentPhone}
        </p>
      )}
      <div className="mt-2 flex flex-wrap gap-1 border-t border-slate-100 pt-2" onPointerDown={(e) => e.stopPropagation()}>
        <CardBtn onClick={onChangeReason}>Sabab</CardBtn>
        {!s.leadId && (
          <CardBtn onClick={onToLead}>
            <UserPlus className="h-3 w-3" /> Lidga
          </CardBtn>
        )}
        <CardBtn onClick={onRestore}>
          <RotateCcw className="h-3 w-3" /> Qaytarish
        </CardBtn>
      </div>
    </div>
  )
}

function CardBtn({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex items-center gap-1 rounded-md px-1.5 py-1 text-[11px] font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800"
    >
      {children}
    </button>
  )
}
