import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Lock, UserCog } from 'lucide-react'
import type { SchoolClass, Subject, Teacher } from '@/types'
import type { StudyGroupListItem } from '@/api/services/groups'
import type { Room } from '@/api/services/rooms'
import type {
  Block,
  BlockDayPattern,
  BlockGroup,
  BlockSourceKind,
  BlockSourceRef,
  BlockType,
  SaveBlockGroupPayload,
} from '@/api/services/blocks'
import {
  BLOCK_TYPE_LABELS,
  DAY_PATTERN_LABELS,
  apiError,
  createBlock,
  getBlock,
  updateBlock,
} from '@/api/services/blocks'
import { getClasses } from '@/api/services/classes'
import { getGroups } from '@/api/services/groups'
import { getRooms } from '@/api/services/rooms'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { formatDmy } from './blockDates'
import { BlockTeacherModal } from './BlockTeacherModal'

interface GroupDraft extends SaveBlockGroupPayload {
  /** Tahrirlashda — mavjud guruh (ustoz shu orqali almashtiriladi). */
  existing?: BlockGroup
}

const emptyGroups = (type: BlockType): GroupDraft[] =>
  type === 'level'
    ? [{ name: 'Katta' }, { name: 'Kichik' }]
    : [{ name: '' }, { name: '' }]

/**
 * Blok formasi — TZ 6.1: fan → manba sinflar → blok turi → 2 ta guruh nomi va ustozi.
 *
 * Fan va blok turi TAHRIRLASHDA QULFLANADI (server ham rad etadi): ularni almashtirish —
 * boshqa blok ochish. Ustoz tahrirlashda shu yerda emas, "Almashtirish" oynasida SANA bilan
 * o'zgaradi: o'sha kungacha qo'yilgan baholar eski ustozda qoladi.
 */
export function BlockFormPage() {
  const { id } = useParams<{ id: string }>()
  const isEdit = Boolean(id)
  const navigate = useNavigate()

  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [tracks, setTracks] = useState<StudyGroupListItem[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
  const [block, setBlock] = useState<Block | null>(null)
  const [loading, setLoading] = useState(true)

  const [name, setName] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [blockType, setBlockType] = useState<BlockType>('level')
  const [dayPattern, setDayPattern] = useState<BlockDayPattern>('odd')
  const [sources, setSources] = useState<BlockSourceRef[]>([])
  const [groups, setGroups] = useState<GroupDraft[]>(emptyGroups('level'))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [teacherFor, setTeacherFor] = useState<BlockGroup | null>(null)

  const fill = (b: Block) => {
    setBlock(b)
    setName(b.name)
    setSubjectId(b.subjectId)
    setBlockType(b.blockType)
    setDayPattern(b.dayPattern)
    setSources(b.sources.map((s) => ({ kind: s.kind, id: s.id })))
    setGroups(
      b.groups.map((g) => ({
        name: g.name,
        teacherId: g.teacherId,
        baseOwnerKind: g.baseOwnerKind,
        baseOwnerId: g.baseOwnerId,
        roomId: g.roomId,
        existing: g,
      })),
    )
  }

  useEffect(() => {
    Promise.all([
      getSubjects(undefined, true),
      getClasses(),
      getGroups(),
      getTeachers(),
      getRooms(),
      id ? getBlock(id) : Promise.resolve(null),
    ])
      .then(([s, c, g, t, r, b]) => {
        setSubjects(s)
        setClasses([...c].sort((a, x) => a.grade - x.grade || a.name.localeCompare(x.name)))
        setTracks(g.filter((x) => x.isTrack && !x.isArchived))
        setTeachers(t)
        setRooms(r)
        if (b) fill(b)
      })
      .finally(() => setLoading(false))
  }, [id])

  /** Blok faniga biriktirilgan ustozlar; fan tanlanmagan bo'lsa — hammasi. */
  const subjectTeachers = useMemo(
    () => (subjectId ? teachers.filter((t) => t.subjectIds?.includes(subjectId)) : teachers),
    [teachers, subjectId],
  )

  const sourceName = (s: BlockSourceRef) =>
    s.kind === 'class'
      ? classes.find((c) => c.id === s.id)?.name
      : tracks.find((t) => t.id === s.id)?.name

  const isPicked = (kind: BlockSourceKind, sid: string) => sources.some((s) => s.kind === kind && s.id === sid)

  const toggleSource = (kind: BlockSourceKind, sid: string) =>
    setSources((prev) =>
      isPicked(kind, sid) ? prev.filter((s) => !(s.kind === kind && s.id === sid)) : [...prev, { kind, id: sid }],
    )

  const setGroup = (i: number, patch: Partial<GroupDraft>) =>
    setGroups((prev) => prev.map((g, k) => (k === i ? { ...g, ...patch } : g)))

  const changeType = (t: BlockType) => {
    setBlockType(t)
    setGroups(emptyGroups(t))
  }

  const handleSave = async () => {
    setSaving(true)
    setError(null)
    const payload = {
      name: name.trim(),
      subjectId,
      blockType,
      dayPattern,
      sources,
      groups: groups.map((g) => ({
        name: g.name.trim(),
        teacherId: g.teacherId || null,
        baseOwnerKind: blockType === 'base_class' ? (g.baseOwnerKind ?? null) : null,
        baseOwnerId: blockType === 'base_class' ? (g.baseOwnerId ?? null) : null,
        roomId: g.roomId || null,
      })),
    }
    try {
      const saved = id ? await updateBlock(id, payload) : await createBlock(payload)
      navigate(isEdit ? '/admin/blocks' : `/admin/blocks/${saved.id}/taqsimlash`)
    } catch (e) {
      setError(apiError(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/blocks')}>
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-xl font-semibold text-slate-800">{isEdit ? block?.name : 'Yangi blok'}</h1>
          <p className="text-sm text-slate-400">
            Fan → manba sinflar → blok turi → 2 ta guruh va ustozlar
          </p>
        </div>
      </div>

      <Card className="space-y-4">
        <div className="grid gap-4 md:grid-cols-2">
          <Input label="Blok nomi" required placeholder="Masalan: Fil+Ijt Ingliz" value={name} onChange={(e) => setName(e.target.value)} />
          <div>
            <Select
              label="Fan"
              required
              disabled={isEdit}
              value={subjectId}
              onChange={(e) => setSubjectId(e.target.value)}
            >
              <option value="">Tanlang...</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
            {isEdit && <Locked />}
          </div>
          <div>
            <Select
              label="Blok turi"
              required
              disabled={isEdit}
              value={blockType}
              onChange={(e) => changeType(e.target.value as BlockType)}
            >
              {(Object.keys(BLOCK_TYPE_LABELS) as BlockType[]).map((t) => (
                <option key={t} value={t}>
                  {BLOCK_TYPE_LABELS[t]}
                </option>
              ))}
            </Select>
            {isEdit && <Locked />}
          </div>
          <Select
            label="Kun naqshi"
            required
            value={dayPattern}
            onChange={(e) => setDayPattern(e.target.value as BlockDayPattern)}
          >
            {(Object.keys(DAY_PATTERN_LABELS) as BlockDayPattern[]).map((p) => (
              <option key={p} value={p}>
                {DAY_PATTERN_LABELS[p]}
              </option>
            ))}
          </Select>
        </div>
        <p className="text-xs text-slate-400">
          Kun naqshi jadvalda tekshiriladi: naqshga zid kunga blok qo'yilmaydi. Istisno kerak bo'lsa —
          "Cheklovsiz" ni tanlang.
        </p>
      </Card>

      <Card className="space-y-3">
        <h2 className="text-sm font-semibold text-slate-700">Manba sinflar va yo'nalishlar</h2>
        <SourcePicker
          title="Yo'nalishlar"
          items={tracks.map((t) => ({ id: t.id, name: t.name }))}
          isPicked={(sid) => isPicked('group', sid)}
          onToggle={(sid) => toggleSource('group', sid)}
        />
        <SourcePicker
          title="Sinflar"
          items={classes.filter((c) => !c.isArchived).map((c) => ({ id: c.id, name: c.name }))}
          isPicked={(sid) => isPicked('class', sid)}
          onToggle={(sid) => toggleSource('class', sid)}
        />
        <p className="text-xs text-slate-400">
          Tanlangan: {sources.length > 0 ? sources.map(sourceName).join(', ') : 'hali yo\'q'}
        </p>
      </Card>

      <Card className="space-y-4">
        <h2 className="text-sm font-semibold text-slate-700">Guruhlar (aniq 2 ta)</h2>
        {groups.map((g, i) => (
          <div key={i} className="grid gap-3 rounded-xl border border-slate-100 p-3 md:grid-cols-4">
            <Input
              label={`${i + 1}-guruh nomi`}
              required
              placeholder={blockType === 'level' ? (i === 0 ? 'Katta' : 'Kichik') : `Iqt-${i + 1} guruhi`}
              value={g.name}
              onChange={(e) => setGroup(i, { name: e.target.value })}
            />
            {g.existing ? (
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-600">Ustoz</span>
                <div className="flex items-center gap-2">
                  <span className="text-sm text-slate-700">
                    {g.existing.teacherName ?? '—'}
                    {g.existing.teacherSince && (
                      <span className="text-slate-400"> · {formatDmy(g.existing.teacherSince)} dan</span>
                    )}
                  </span>
                  <Button variant="secondary" className="px-2 py-1" onClick={() => setTeacherFor(g.existing!)}>
                    <UserCog className="h-4 w-4" /> Almashtirish
                  </Button>
                </div>
              </div>
            ) : (
              <Select
                label="Ustoz"
                required
                value={g.teacherId ?? ''}
                onChange={(e) => setGroup(i, { teacherId: e.target.value })}
              >
                <option value="">Tanlang...</option>
                {subjectTeachers.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </Select>
            )}
            {blockType === 'base_class' && (
              <Select
                label="Guruh sinfi"
                required
                value={g.baseOwnerId ? `${g.baseOwnerKind}:${g.baseOwnerId}` : ''}
                onChange={(e) => {
                  const [kind, sid] = e.target.value.split(':')
                  setGroup(i, { baseOwnerKind: (kind || null) as BlockSourceKind | null, baseOwnerId: sid || null })
                }}
              >
                <option value="">Tanlang...</option>
                {sources.map((s) => (
                  <option key={`${s.kind}:${s.id}`} value={`${s.kind}:${s.id}`}>
                    {sourceName(s)}
                  </option>
                ))}
              </Select>
            )}
            <Select label="Xona (ixtiyoriy)" value={g.roomId ?? ''} onChange={(e) => setGroup(i, { roomId: e.target.value || null })}>
              <option value="">—</option>
              {rooms.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </Select>
          </div>
        ))}
        {blockType === 'base_class' && (
          <p className="text-xs text-slate-400">
            Guruh sinfining o'quvchilari avtomatik shu guruhga tushadi va boshqa guruhga o'tkazilmaydi.
            Qolgan sinf o'quvchilarini taqsimlash sahifasida o'zingiz bo'lasiz.
          </p>
        )}
        {subjectId && subjectTeachers.length === 0 && (
          <p className="text-xs text-amber-600">
            Bu fanga biriktirilgan ustoz yo'q — Boshqaruv → Xodimlar bo'limida ustozga fanni belgilang.
          </p>
        )}
      </Card>

      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={() => navigate('/admin/blocks')} disabled={saving}>
          Bekor qilish
        </Button>
        <Button onClick={handleSave} disabled={saving}>
          {saving ? 'Saqlanmoqda...' : isEdit ? 'Saqlash' : 'Yaratish va taqsimlashga o\'tish'}
        </Button>
      </div>

      <BlockTeacherModal
        group={teacherFor}
        teachers={subjectTeachers}
        onClose={() => setTeacherFor(null)}
        onDone={(b) => {
          setTeacherFor(null)
          fill(b)
        }}
      />
    </div>
  )
}

function Locked() {
  return (
    <p className="mt-1 flex items-center gap-1 text-xs text-slate-400">
      <Lock className="h-3 w-3" /> O'zgartirib bo'lmaydi — yangi blok oching
    </p>
  )
}

interface SourcePickerProps {
  title: string
  items: { id: string; name: string }[]
  isPicked: (id: string) => boolean
  onToggle: (id: string) => void
}

function SourcePicker({ title, items, isPicked, onToggle }: SourcePickerProps) {
  if (items.length === 0) return null
  return (
    <div>
      <p className="mb-1.5 text-xs font-medium uppercase tracking-wide text-slate-400">{title}</p>
      <div className="flex flex-wrap gap-1.5">
        {items.map((it) => (
          <button
            key={it.id}
            type="button"
            onClick={() => onToggle(it.id)}
            className={cn(
              'rounded-lg border px-3 py-1.5 text-sm transition-colors',
              isPicked(it.id)
                ? 'border-brand-600 bg-brand-600 text-white'
                : 'border-slate-200 text-slate-600 hover:bg-slate-50',
            )}
          >
            {it.name}
          </button>
        ))}
      </div>
    </div>
  )
}
