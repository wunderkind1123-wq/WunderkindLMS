import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import { AlertTriangle, ArrowLeft, CheckCircle2, Download, Lock, Pencil, UserCog, UserMinus } from 'lucide-react'
import type { Teacher } from '@/types'
import type { Block, BlockGroup, BlockMember, BlockRoster } from '@/api/services/blocks'
import {
  DAY_PATTERN_LABELS,
  activateBlock,
  apiError,
  assignToBlock,
  downloadBlockRoster,
  getBlock,
  getBlockRoster,
  moveInBlock,
  unassignFromBlock,
} from '@/api/services/blocks'
import { getTeachers } from '@/api/services/teachers'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { formatDmy, todayIso } from './blockDates'
import type { MoveRequest } from './BlockMoveModal'
import { BlockMoveModal } from './BlockMoveModal'
import { BlockTeacherModal } from './BlockTeacherModal'

/** "Taqsimlanmagan" ustunining id'si (droppable). */
const UNASSIGNED = 'unassigned'

/**
 * O'quvchilarni taqsimlash — TZ 6.2: ikki ustun (chapda 1-guruh, o'ngda 2-guruh), pastda
 * "Taqsimlanmagan". Har o'quvchi yonida manba sinfi ("Ali V. · Filologiya"). Sudrab o'tkazish
 * yoki bir nechtasini belgilab ko'chirish.
 *
 * Taqsimlanmagan o'quvchi guruhga darhol qo'shiladi (ko'rilayotgan sanadan). Guruhdagi
 * o'quvchini boshqa guruhga o'tkazish yoki blokdan chiqarish — oynada sana va sabab bilan:
 * eski a'zolik yopiladi, tarix qoladi.
 */
export function BlockRosterPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [block, setBlock] = useState<Block | null>(null)
  const [roster, setRoster] = useState<BlockRoster | null>(null)
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [date, setDate] = useState(todayIso())
  const [selected, setSelected] = useState<string[]>([])
  const [sourceFilter, setSourceFilter] = useState('')
  const [moveRequest, setMoveRequest] = useState<MoveRequest | null>(null)
  const [teacherFor, setTeacherFor] = useState<BlockGroup | null>(null)
  const [banner, setBanner] = useState<{ tone: 'error' | 'warn' | 'ok'; lines: string[] } | null>(null)
  const [busy, setBusy] = useState(false)

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const load = useCallback(() => {
    Promise.all([getBlock(id), getBlockRoster(id, date)]).then(([b, r]) => {
      setBlock(b)
      setRoster(r)
    })
  }, [id, date])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    getTeachers().then(setTeachers)
  }, [])

  const subjectTeachers = useMemo(
    () => (block ? teachers.filter((t) => t.subjectIds?.includes(block.subjectId)) : []),
    [teachers, block],
  )

  const all = useMemo(
    () => (roster ? [...roster.groups.flatMap((g) => g.members), ...roster.unassigned] : []),
    [roster],
  )
  const byId = useMemo(() => new Map(all.map((m) => [m.studentId, m])), [all])

  /** Manba sinf bo'yicha blokda — guruhi bor sinf o'quvchisi faqat o'z guruhida (8-qoida). */
  const lockedTo = (m: BlockMember): BlockGroup | undefined =>
    block?.blockType === 'base_class'
      ? block.groups.find((g) => g.baseOwnerId === m.sourceId)
      : undefined

  const toggle = (studentId: string) =>
    setSelected((prev) => (prev.includes(studentId) ? prev.filter((x) => x !== studentId) : [...prev, studentId]))

  const run = async (action: () => Promise<BlockRoster>) => {
    setBusy(true)
    try {
      setRoster(await action())
      setSelected([])
      setBanner(null)
      setBlock(await getBlock(id))
      return null
    } catch (e) {
      const msg = apiError(e, "Bajarib bo'lmadi")
      setBanner({ tone: 'error', lines: [msg] })
      return msg
    } finally {
      setBusy(false)
    }
  }

  /**
   * O'quvchilarni guruhga yuborish: taqsimlanmaganlar darhol qo'shiladi, boshqa guruhdagilar
   * uchun ko'chirish oynasi ochiladi.
   */
  const sendTo = async (studentIds: string[], group: BlockGroup) => {
    const members = studentIds.map((s) => byId.get(s)).filter((m): m is BlockMember => Boolean(m))
    const fresh = members.filter((m) => !m.groupId).map((m) => m.studentId)
    const moving = members.filter((m) => m.groupId && m.groupId !== group.id)
    if (fresh.length > 0) {
      const err = await run(() => assignToBlock(id, fresh, group.id, date))
      if (err) return
    }
    if (moving.length > 0)
      setMoveRequest({
        mode: 'move',
        studentIds: moving.map((m) => m.studentId),
        names: moving.map((m) => m.fullName),
        groupId: group.id,
        groupName: group.name,
      })
  }

  const removeFromBlock = (studentIds: string[]) => {
    const members = studentIds.map((s) => byId.get(s)).filter((m): m is BlockMember => Boolean(m?.groupId))
    if (members.length === 0) return
    setMoveRequest({ mode: 'unassign', studentIds: members.map((m) => m.studentId), names: members.map((m) => m.fullName) })
  }

  const submitMove = (when: string, reason: string) => {
    const req = moveRequest!
    return run(() =>
      req.mode === 'move'
        ? moveInBlock(id, req.studentIds, req.groupId!, when, reason)
        : unassignFromBlock(id, req.studentIds, when, reason),
    ).then((err) => {
      if (!err) setMoveRequest(null)
      return err
    })
  }

  const onDragEnd = (e: DragEndEvent) => {
    if (!block || !e.over) return
    const dragged = String(e.active.id)
    // Belgilangan o'quvchi sudralsa — u bilan birga belgilanganlarning hammasi.
    const ids = selected.includes(dragged) ? selected : [dragged]
    const target = String(e.over.id)
    if (target === UNASSIGNED) removeFromBlock(ids)
    else {
      const group = block.groups.find((g) => g.id === target)
      if (group) void sendTo(ids, group)
    }
  }

  const handleActivate = async () => {
    const res = await activateBlock(id).catch((e) => {
      setBanner({ tone: 'error', lines: [apiError(e, "Faollashtirib bo'lmadi")] })
      return null
    })
    if (!res) return
    setBanner(res.isActive ? { tone: 'ok', lines: ['Blok faollashtirildi'] } : { tone: 'warn', lines: res.errors })
    load()
  }

  if (!block || !roster) return <Loader label="Yuklanmoqda..." />

  const sources = Array.from(new Set(roster.unassigned.map((m) => m.sourceName))).sort()
  const unassigned = roster.unassigned.filter((m) => !sourceFilter || m.sourceName === sourceFilter)

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/blocks')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="flex items-center gap-2 text-xl font-semibold text-slate-800">
              {block.name}
              <span
                className={cn(
                  'rounded-md px-2 py-0.5 text-xs font-medium',
                  block.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
                )}
              >
                {block.isActive ? 'Faol' : 'Faol emas'}
              </span>
            </h1>
            <p className="text-sm text-slate-400">
              {block.subjectName} · {DAY_PATTERN_LABELS[block.dayPattern]} · {block.sources.map((s) => s.name).join(', ')}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <DatePicker value={date} onChange={(v) => v && setDate(v)} ariaLabel="Sana" title="Qaysi sanadagi holat" />
          <Button variant="secondary" onClick={() => downloadBlockRoster(id, date).catch(() => alert("Yuklab bo'lmadi"))}>
            <Download className="h-4 w-4" /> Excel
          </Button>
          <Button variant="secondary" onClick={() => navigate(`/admin/blocks/${id}`)}>
            <Pencil className="h-4 w-4" /> Tahrirlash
          </Button>
          {!block.isActive && (
            <Button onClick={handleActivate}>
              <CheckCircle2 className="h-4 w-4" /> Faollashtirish
            </Button>
          )}
        </div>
      </div>

      {banner && (
        <Card
          className={cn(
            'text-sm',
            banner.tone === 'error' && 'border-red-200 bg-red-50 text-red-700',
            banner.tone === 'warn' && 'border-amber-200 bg-amber-50 text-amber-800',
            banner.tone === 'ok' && 'border-emerald-200 bg-emerald-50 text-emerald-700',
          )}
        >
          <div className="flex items-start gap-2">
            {banner.tone === 'ok' ? <CheckCircle2 className="mt-0.5 h-4 w-4" /> : <AlertTriangle className="mt-0.5 h-4 w-4" />}
            <ul className="space-y-0.5">
              {banner.lines.map((l) => (
                <li key={l}>{l}</li>
              ))}
            </ul>
          </div>
        </Card>
      )}

      {selected.length > 0 && (
        <Card className="flex flex-wrap items-center gap-2 py-3 text-sm">
          <span className="font-medium text-slate-700">{selected.length} ta belgilandi:</span>
          {block.groups.map((g) => (
            <Button key={g.id} variant="secondary" disabled={busy} onClick={() => void sendTo(selected, g)}>
              → {g.name}
            </Button>
          ))}
          <Button variant="secondary" disabled={busy} onClick={() => removeFromBlock(selected)}>
            <UserMinus className="h-4 w-4" /> Blokdan chiqarish
          </Button>
          <Button variant="ghost" onClick={() => setSelected([])}>
            Bekor qilish
          </Button>
        </Card>
      )}

      <DndContext sensors={sensors} onDragEnd={onDragEnd}>
        <div className="grid gap-4 md:grid-cols-2">
          {block.groups.map((g) => {
            const col = roster.groups.find((r) => r.groupId === g.id)
            return (
              <Column
                key={g.id}
                id={g.id}
                title={g.name}
                subtitle={
                  <span className="flex items-center gap-1">
                    {g.teacherName ?? 'Ustoz biriktirilmagan'}
                    {g.teacherSince && <span className="text-slate-400">· {formatDmy(g.teacherSince)} dan</span>}
                    <button
                      type="button"
                      title="Ustozni almashtirish"
                      onClick={() => setTeacherFor(g)}
                      className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                    >
                      <UserCog className="h-3.5 w-3.5" />
                    </button>
                  </span>
                }
                count={col?.members.length ?? 0}
              >
                {col?.members.map((m) => (
                  <StudentCard
                    key={m.studentId}
                    member={m}
                    selected={selected.includes(m.studentId)}
                    locked={lockedTo(m)?.id === g.id}
                    onToggle={() => toggle(m.studentId)}
                  />
                ))}
              </Column>
            )
          })}
        </div>

        <Column
          id={UNASSIGNED}
          title="Taqsimlanmagan"
          subtitle={
            sources.length > 1 ? (
              <select
                className="rounded-md border border-slate-200 bg-white px-2 py-0.5 text-xs text-slate-600"
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
              >
                <option value="">Barcha sinflar</option>
                {sources.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            ) : (
              <span>Guruhga sudrang yoki belgilab yuboring</span>
            )
          }
          count={roster.unassigned.length}
          tone={roster.unassigned.length > 0 ? 'warn' : undefined}
          grid
        >
          {unassigned.map((m) => (
            <StudentCard
              key={m.studentId}
              member={m}
              selected={selected.includes(m.studentId)}
              onToggle={() => toggle(m.studentId)}
            />
          ))}
          {roster.unassigned.length === 0 && (
            <p className="col-span-full py-4 text-center text-sm text-slate-400">Hamma o'quvchi taqsimlangan</p>
          )}
        </Column>
      </DndContext>

      <BlockMoveModal request={moveRequest} onClose={() => setMoveRequest(null)} onSubmit={submitMove} />
      <BlockTeacherModal
        group={teacherFor}
        teachers={subjectTeachers}
        onClose={() => setTeacherFor(null)}
        onDone={(b) => {
          setTeacherFor(null)
          setBlock(b)
        }}
      />
    </div>
  )
}

interface ColumnProps {
  id: string
  title: string
  subtitle: ReactNode
  count: number
  tone?: 'warn'
  /** Kartalar bir nechta ustunda (keng "Taqsimlanmagan" uchun). */
  grid?: boolean
  children: ReactNode
}

function Column({ id, title, subtitle, count, tone, grid, children }: ColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id })
  return (
    <div
      ref={setNodeRef}
      className={cn(
        'rounded-2xl border bg-white p-4 transition-colors',
        isOver ? 'border-brand-400 bg-brand-50/40' : tone === 'warn' ? 'border-amber-200' : 'border-slate-200',
      )}
    >
      <div className="mb-3 flex items-start justify-between gap-2">
        <div>
          <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
          <div className="text-xs text-slate-500">{subtitle}</div>
        </div>
        <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">{count}</span>
      </div>
      <div className={cn('min-h-[60px] gap-2', grid ? 'grid sm:grid-cols-2 lg:grid-cols-4' : 'flex flex-col')}>
        {children}
      </div>
    </div>
  )
}

interface StudentCardProps {
  member: BlockMember
  selected: boolean
  /** Manba sinf bo'yicha blokda o'z guruhiga qulflangan (8-qoida). */
  locked?: boolean
  onToggle: () => void
}

function StudentCard({ member: m, selected, locked, onToggle }: StudentCardProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: m.studentId })
  const style = transform ? { transform: `translate(${transform.x}px, ${transform.y}px)` } : undefined
  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className={cn(
        'flex cursor-grab items-center gap-2 rounded-xl border bg-white px-3 py-2 shadow-sm active:cursor-grabbing',
        selected ? 'border-brand-400 bg-brand-50/50' : 'border-slate-200',
        isDragging && 'relative z-10 opacity-90 shadow-lg',
      )}
    >
      <input
        type="checkbox"
        className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
        checked={selected}
        onChange={onToggle}
        onPointerDown={(e) => e.stopPropagation()}
      />
      <div className="min-w-0 flex-1 text-sm">
        <Link
          to={`/admin/students/${m.studentId}`}
          className="font-medium text-slate-800 hover:text-brand-700 hover:underline"
          onPointerDown={(e) => e.stopPropagation()}
        >
          {m.fullName}
        </Link>
        <span className="text-slate-400"> · {m.sourceName}</span>
        {m.joinedOn && <div className="text-[11px] text-slate-400">{formatDmy(m.joinedOn)} dan</div>}
      </div>
      {locked && (
        <span title="Manba sinf bo'yicha — faqat shu guruhda">
          <Lock className="h-3.5 w-3.5 text-slate-300" />
        </span>
      )}
    </div>
  )
}
