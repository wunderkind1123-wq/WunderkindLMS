import { useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, Check, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createArchiveReason,
  createArchiveReasonGroup,
  deleteArchiveReason,
  deleteArchiveReasonGroup,
  getArchiveReasonGroups,
  getArchiveReasons,
  reorderArchiveReasonGroups,
  updateArchiveReason,
  updateArchiveReasonGroup,
  type ArchiveReason,
  type ArchiveReasonGroup,
} from '@/api/services/archiveReasons'
import type { StageColor } from '@/types'
import { stageColors, stageColorKeys } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

/**
 * Arxivlash sabablari katalogi (§2.2) va ularning GURUHLARI (2026-11-13) —
 * "Sozlamalar → Arxivlash sabablari".
 *
 * GURUH = ARXIV DOSKASINING USTUNI. "O'zi ketdi / Maktab chiqardi / Tabiiy sabab" — maktab ularni
 * o'zi qo'shadi, nomlaydi, bo'yaydi, tartiblaydi. Har bir sabab bitta guruhga kiradi; arxivlashda
 * xodim faqat sababni tanlaydi. Ichida sabab bor guruh o'chirilmaydi (avval sabablarni ko'chirish).
 *
 * ISHLATILGAN SABAB O'CHIRILMAYDI. Aks holda arxivdagi o'quvchi "nega ketgani"ni
 * yo'qotardi. Ro'yxatdan chiqarish — "Faol" belgisini olib tashlash: eski yozuvlar
 * joyida qoladi, yangi arxivlashda esa tanlanmaydi.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const byPosition = <T extends { position: number; name: string }>(list: T[]) =>
  [...list].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))

export function ArchiveReasonsSettings() {
  const [rows, setRows] = useState<ArchiveReason[]>([])
  const [groups, setGroups] = useState<ArchiveReasonGroup[]>([])
  const [loading, setLoading] = useState(true)

  // Sabab oynasi
  const [editing, setEditing] = useState<ArchiveReason | null>(null)
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [position, setPosition] = useState(0)
  const [isActive, setIsActive] = useState(true)
  const [groupId, setGroupId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Guruh oynasi
  const [gEditing, setGEditing] = useState<ArchiveReasonGroup | null>(null)
  const [gOpen, setGOpen] = useState(false)
  const [gName, setGName] = useState('')
  const [gColor, setGColor] = useState<StageColor>('slate')
  const [gActive, setGActive] = useState(true)
  const [gError, setGError] = useState<string | null>(null)

  const reloadGroups = () => getArchiveReasonGroups(true).then((list) => setGroups(byPosition(list)))

  useEffect(() => {
    Promise.all([getArchiveReasons(true), getArchiveReasonGroups(true)])
      .then(([r, g]) => {
        setRows(byPosition(r))
        setGroups(byPosition(g))
      })
      .finally(() => setLoading(false))
  }, [])

  const groupName = (id: string | null) => groups.find((g) => g.id === id)

  // ----- sabablar -----
  const openCreate = () => {
    setEditing(null)
    setName('')
    // "Boshqa" 99-o'rinda turadi — yangi sabab undan oldin chiqsin.
    setPosition(Math.max(0, ...rows.filter((r) => r.position < 99).map((r) => r.position)) + 1)
    setIsActive(true)
    setGroupId('')
    setError(null)
    setOpen(true)
  }

  const openEdit = (r: ArchiveReason) => {
    setEditing(r)
    setName(r.name)
    setPosition(r.position)
    setIsActive(r.isActive)
    setGroupId(r.groupId ?? '')
    setError(null)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setBusy(true)
    setError(null)
    try {
      const input = { name: name.trim(), position, isActive, groupId: groupId || null }
      const saved = editing ? await updateArchiveReason(editing.id, input) : await createArchiveReason(input)
      setRows((prev) => byPosition([...prev.filter((x) => x.id !== saved.id), saved]))
      setOpen(false)
      void reloadGroups() // guruhdagi sabablar soni o'zgardi
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (r: ArchiveReason) => {
    if (!confirm(`"${r.name}" sababini o'chirasizmi?`)) return
    try {
      await deleteArchiveReason(r.id)
      setRows((prev) => prev.filter((x) => x.id !== r.id))
      void reloadGroups()
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  // ----- guruhlar -----
  const openGroupCreate = () => {
    setGEditing(null)
    setGName('')
    setGColor('slate')
    setGActive(true)
    setGError(null)
    setGOpen(true)
  }

  const openGroupEdit = (g: ArchiveReasonGroup) => {
    setGEditing(g)
    setGName(g.name)
    setGColor(g.color)
    setGActive(g.isActive)
    setGError(null)
    setGOpen(true)
  }

  const submitGroup = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!gName.trim()) return
    setBusy(true)
    setGError(null)
    try {
      const input = { name: gName.trim(), color: gColor, isActive: gActive }
      const saved = gEditing
        ? await updateArchiveReasonGroup(gEditing.id, input)
        : await createArchiveReasonGroup(input)
      setGroups((prev) => byPosition([...prev.filter((x) => x.id !== saved.id), saved]))
      setGOpen(false)
    } catch (err) {
      setGError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const removeGroup = async (g: ArchiveReasonGroup) => {
    if (!confirm(`"${g.name}" guruhini o'chirasizmi? Arxiv doskasidan bu ustun yo'qoladi.`)) return
    try {
      await deleteArchiveReasonGroup(g.id)
      setGroups((prev) => prev.filter((x) => x.id !== g.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  const move = async (index: number, delta: -1 | 1) => {
    const next = [...groups]
    const j = index + delta
    if (j < 0 || j >= next.length) return
    ;[next[index], next[j]] = [next[j], next[index]]
    const ordered = next.map((g, i) => ({ ...g, position: i }))
    setGroups(ordered)
    try {
      await reorderArchiveReasonGroups(ordered.map((g) => g.id))
    } catch (err) {
      alert(errorText(err, "Tartibni saqlab bo'lmadi"))
      void reloadGroups()
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const ungrouped = rows.filter((r) => r.isActive && !r.groupId).length

  return (
    <div className="space-y-4">
      {/* ---------------- Guruhlar ---------------- */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
          <div>
            <p className="font-semibold text-slate-800">Guruhlar</p>
            <p className="text-sm text-slate-400">
              Arxiv doskasining ustunlari: kim tashabbusi bilan ketgani — "o'zi ketdi", "maktab chiqardi" va h.k.
            </p>
          </div>
          <Button onClick={openGroupCreate}>
            <Plus className="h-4 w-4" /> Yangi guruh
          </Button>
        </div>

        <ul className="divide-y divide-slate-100">
          {groups.map((g, i) => {
            const c = stageColors[g.color] ?? stageColors.slate
            return (
              <li key={g.id} className={cn('flex items-center gap-3 px-4 py-3', !g.isActive && 'opacity-60')}>
                <div className="flex flex-col">
                  <button
                    type="button"
                    title="Yuqoriga"
                    aria-label="Yuqoriga"
                    disabled={i === 0}
                    onClick={() => move(i, -1)}
                    className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-30"
                  >
                    <ArrowUp className="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    title="Pastga"
                    aria-label="Pastga"
                    disabled={i === groups.length - 1}
                    onClick={() => move(i, 1)}
                    className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-30"
                  >
                    <ArrowDown className="h-3.5 w-3.5" />
                  </button>
                </div>
                <span className={cn('h-2.5 w-2.5 shrink-0 rounded-full', c.dot)} />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-slate-800">{g.name}</p>
                  <p className="text-xs text-slate-400">
                    {g.reasonCount} ta sabab · arxivda {g.studentCount} ta o'quvchi
                    {!g.isActive && ' · faol emas'}
                  </p>
                </div>
                <div className="flex items-center gap-0.5">
                  <button
                    type="button"
                    title="Tahrirlash"
                    onClick={() => openGroupEdit(g)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  {g.reasonCount === 0 && (
                    <button
                      type="button"
                      title="O'chirish"
                      onClick={() => removeGroup(g)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </div>
              </li>
            )
          })}
          {groups.length === 0 && (
            <li className="px-4 py-8 text-center text-sm text-slate-400">Hali guruh qo'shilmagan</li>
          )}
        </ul>
      </Card>

      {/* ---------------- Sabablar ---------------- */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
          <div>
            <p className="font-semibold text-slate-800">Arxivlash sabablari</p>
            <p className="text-sm text-slate-400">
              O'quvchini arxivlashda tanlanadi (majburiy). Har bir sababni guruhga qo'ying.
            </p>
            {ungrouped > 0 && (
              <p className="mt-1 text-xs font-medium text-amber-600">
                {ungrouped} ta faol sabab guruhsiz — ular bilan ketganlar doskada "Sababi aniqlanmagan" ustunida turadi.
              </p>
            )}
          </div>
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi sabab
          </Button>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="w-16 px-4 py-3">Tartib</th>
                <th className="px-4 py-3">Sabab</th>
                <th className="px-4 py-3">Guruh</th>
                <th className="px-4 py-3">Ishlatilgan</th>
                <th className="px-4 py-3">Holat</th>
                <th className="px-4 py-3 text-right">Amallar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((r) => {
                const g = groupName(r.groupId)
                return (
                  <tr key={r.id} className={cn('hover:bg-slate-50/60', !r.isActive && 'text-slate-400')}>
                    <td className="px-4 py-3 text-slate-400">{r.position}</td>
                    <td className={cn('px-4 py-3 font-medium', r.isActive ? 'text-slate-800' : 'text-slate-400')}>
                      <span className="block max-w-[14rem] truncate" title={r.name}>
                        {r.name}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3">
                      {g ? (
                        <span className="inline-flex items-center gap-1.5 text-slate-600">
                          <span className={cn('h-2 w-2 rounded-full', (stageColors[g.color] ?? stageColors.slate).dot)} />
                          {g.name}
                        </span>
                      ) : (
                        <span className="text-xs text-amber-600">Guruhsiz</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{r.usedBy} ta o'quvchi</td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          r.isActive ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-slate-400',
                        )}
                      >
                        {r.isActive ? 'Faol' : 'Faol emas'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => openEdit(r)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        {r.usedBy === 0 && (
                          <button
                            type="button"
                            title="O'chirish"
                            onClick={() => remove(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-slate-400">
                    Hali sabab qo'shilmagan
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {/* ---------------- Sabab oynasi ---------------- */}
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Sababni tahrirlash' : 'Yangi sabab'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="archive-reason-form" disabled={busy || !name.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="archive-reason-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Sabab nomi"
            required
            placeholder="masalan: Boshqa maktabga o'tdi"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <Select label="Guruh" value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Guruhsiz</option>
            {groups
              .filter((g) => g.isActive || g.id === editing?.groupId)
              .map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name}
                </option>
              ))}
          </Select>
          {editing && editing.groupId !== (groupId || null) && editing.usedBy > 0 && (
            <p className="-mt-2 text-xs text-slate-500">
              Shu sabab bilan ketgan {editing.usedBy} ta o'quvchi ham yangi guruhga o'tadi.
            </p>
          )}
          <Input
            label="Tartib raqami (kichigi tepada)"
            type="number"
            min={0}
            value={position}
            onChange={(e) => setPosition(Math.max(0, Number(e.target.value) || 0))}
          />
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faol — arxivlash oynasida tanlanadi
          </label>
          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      </Modal>

      {/* ---------------- Guruh oynasi ---------------- */}
      <Modal
        open={gOpen}
        onClose={() => setGOpen(false)}
        title={gEditing ? 'Guruhni tahrirlash' : 'Yangi guruh'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setGOpen(false)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="archive-group-form" disabled={busy || !gName.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="archive-group-form" onSubmit={submitGroup} className="space-y-4">
          <Input
            label="Guruh nomi"
            required
            maxLength={60}
            placeholder="masalan: Maktab chiqardi"
            value={gName}
            onChange={(e) => setGName(e.target.value)}
          />
          <div>
            <span className="mb-2 block text-sm font-medium text-slate-600">Rang</span>
            <div className="flex flex-wrap gap-2">
              {stageColorKeys.map((key) => (
                <button
                  key={key}
                  type="button"
                  aria-label={key}
                  onClick={() => setGColor(key)}
                  className={cn(
                    'flex h-8 w-8 items-center justify-center rounded-full',
                    stageColors[key].swatch,
                    gColor === key && 'ring-2 ring-slate-800 ring-offset-2',
                  )}
                >
                  {gColor === key && <Check className="h-4 w-4 text-white" />}
                </button>
              ))}
            </div>
          </div>
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={gActive}
              onChange={(e) => setGActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faol — doskada ustun bo'lib turadi
          </label>
          {gError && <p className="text-sm text-red-600">{gError}</p>}
        </form>
      </Modal>
    </div>
  )
}
