/**
 * Yuklama va avtomatik jadval tuzish (mijoz, 2026-11-21: "dars jadvali bilan bog'liq hamma narsani
 * boshqarish" — 7-band; "keyin yana qoidalar kiritamiz").
 *
 *  1. Yuklama — har sinf/guruh × fan: haftasiga necha dars, kim o'tadi, qaysi xonada (maxsus xona).
 *     Birinchi marta joriy jadvaldan to'ldiriladi, keyin tahrirlanadi.
 *  2. O'qituvchilar bandligi — ustoz ishlay olmaydigan kun yoki soatlar.
 *  3. Avtomatik tuzish — yuklamadan yangi QORALAMA jadval: sinflarga "Nashr qilish"siz ta'sir qilmaydi,
 *     umumiy jadval muharririda ko'rib, qo'lda to'g'rilab, keyin nashr qilinadi.
 *
 * Qoidalar ro'yxati (Avtomatik tuzish bo'limidagi belgilar) kengayadi: yangi qoida serverda
 * `GenerateScheduleRequest` ga maydon va `TimetableSolver.Score` ga jarima bo'lib qo'shiladi.
 */
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AlertTriangle, ArrowLeft, CheckCircle2, Plus, RefreshCw, Trash2, Wand2 } from 'lucide-react'
import type { SchoolSettings, Subject, Teacher } from '@/types'
import { getLessonOwners, type LessonOwnerItem } from '@/api/services/lessonOwners'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import {
  fillLoadsFromSchedule,
  generateSchedule,
  getScheduleRooms,
  getStudyDays,
  saveStudyDays,
  getTeachingLoads,
  getUnavailability,
  saveOwnerLoads,
  saveUnavailability,
  type GenerateResult,
  type OwnerStudyDays,
  type ScheduleRooms,
  type TeacherUnavailability,
  type TeachingLoad,
  type UnavailabilitySlot,
} from '@/api/services/timetableBuilder'
import { weekDays, schedulePeriods } from '@/config/constants'
import { periodTimeMap } from '@/components/schedule/periodTimes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'

type Tab = 'loads' | 'busy' | 'generate'

const errText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const today = () => {
  const d = new Date()
  return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}`
}

export function TimetableBuilderPage() {
  const navigate = useNavigate()
  const [tab, setTab] = useState<Tab>('loads')
  const [owners, setOwners] = useState<LessonOwnerItem[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [rooms, setRooms] = useState<ScheduleRooms>({ rooms: [], homeRooms: {} })
  const [loads, setLoads] = useState<TeachingLoad[]>([])
  const [busy, setBusy] = useState<TeacherUnavailability[]>([])
  const [studyDays, setStudyDays] = useState<OwnerStudyDays[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const reloadLoads = () => getTeachingLoads().then(setLoads)

  useEffect(() => {
    Promise.all([
      getLessonOwners(),
      getSubjects(),
      getTeachers(),
      getSettings(),
      getScheduleRooms(),
      getTeachingLoads(),
      getUnavailability(),
      getStudyDays(),
    ])
      .then(([os, subs, tchs, st, rm, ld, un, sd]) => {
        setStudyDays(sd)
        setOwners(os)
        setSubjects(subs.filter((s) => s.isActive !== false))
        setTeachers(tchs)
        setSettings(st)
        setRooms(rm)
        setLoads(ld)
        setBusy(un)
      })
      .catch((e) => setError(errText(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const periods = useMemo(() => {
    const configured = Math.max(0, ...periodTimeMap(settings).keys())
    return schedulePeriods.slice(0, configured || 8)
  }, [settings])

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link to="/admin/schedule/manage" className="mb-1 inline-flex items-center gap-1 text-sm text-slate-500 hover:text-brand-600">
            <ArrowLeft className="h-4 w-4" /> Dars jadvali yaratish
          </Link>
          <h1 className="text-xl font-semibold text-slate-800">Yuklama va avtomatik tuzish</h1>
          <p className="text-sm text-slate-400">
            Har sinfda qaysi fandan haftasiga necha dars va kim o'tishini kiriting — tizim shundan yangi jadval tuzadi.
            Natija qoralama bo'ladi: ko'rib, to'g'rilab, keyin nashr qilasiz.
          </p>
        </div>
      </div>

      <div className="flex w-fit flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
        {(
          [
            ['loads', '1. Yuklama'],
            ['busy', "2. O'qituvchilar bandligi"],
            ['generate', '3. Avtomatik tuzish'],
          ] as const
        ).map(([k, label]) => (
          <button
            key={k}
            type="button"
            onClick={() => setTab(k)}
            className={cn(
              'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
              tab === k ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {label}
          </button>
        ))}
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {tab === 'loads' && (
        <LoadsTab
          owners={owners}
          subjects={subjects}
          teachers={teachers}
          rooms={rooms}
          loads={loads}
          capacityOf={(o) => periods.length * (studyDays.find((d) => d.ownerId === o.id)?.days ?? 6)}
          studyDays={studyDays}
          onDays={async (o, days) => {
            try {
              await saveStudyDays(o.id, days)
              setStudyDays((prev) => [...prev.filter((d) => d.ownerId !== o.id), { ownerId: o.id, days, isDefault: false }])
              setToast(`${o.name}: haftasiga ${days} kun`)
            } catch (e) {
              setError(errText(e, "Saqlab bo'lmadi"))
            }
          }}
          onSaved={async (msg) => {
            await reloadLoads()
            setToast(msg)
          }}
          onError={setError}
        />
      )}
      {tab === 'busy' && (
        <BusyTab
          teachers={teachers}
          loads={loads}
          busy={busy}
          periods={periods}
          onSaved={(teacherId, slots) => {
            setBusy((prev) => [...prev.filter((b) => b.teacherId !== teacherId), { teacherId, slots }])
            setToast("O'qituvchi bandligi saqlandi")
          }}
          onError={setError}
        />
      )}
      {tab === 'generate' && (
        <GenerateTab
          loads={loads}
          owners={owners}
          onOpen={(name) => navigate(`/admin/schedule/manage?name=${encodeURIComponent(name)}`)}
          onError={setError}
        />
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}

/* ============================================================== 1. Yuklama */

interface EditRow {
  subjectId: string
  subGroup: number
  teacherId: string
  hoursPerWeek: number
  roomId: string
}

function LoadsTab({
  owners,
  subjects,
  teachers,
  rooms,
  loads,
  capacityOf,
  studyDays,
  onDays,
  onSaved,
  onError,
}: {
  owners: LessonOwnerItem[]
  subjects: Subject[]
  teachers: Teacher[]
  rooms: ScheduleRooms
  loads: TeachingLoad[]
  capacityOf: (o: LessonOwnerItem) => number
  studyDays: OwnerStudyDays[]
  onDays: (o: LessonOwnerItem, days: number) => void
  onSaved: (msg: string) => Promise<void>
  onError: (msg: string | null) => void
}) {
  const [editing, setEditing] = useState<LessonOwnerItem | null>(null)
  const [rows, setRows] = useState<EditRow[]>([])
  const [saving, setSaving] = useState(false)
  const [fillOpen, setFillOpen] = useState(false)
  const [overwrite, setOverwrite] = useState(false)
  const [filling, setFilling] = useState(false)

  const subjectName = (id: string) => subjects.find((s) => s.id === id)?.name ?? '—'
  const roomName = (id: string | null) => (id ? (rooms.rooms.find((r) => r.id === id)?.name ?? '') : '')
  const hasHalves = (o: LessonOwnerItem) => o.kind === 'class' || o.isTrack

  const open = (o: LessonOwnerItem) => {
    setEditing(o)
    setRows(
      loads
        .filter((l) => l.ownerId === o.id)
        .map((l) => ({
          subjectId: l.subjectId,
          subGroup: l.subGroup,
          teacherId: l.teacherId,
          hoursPerWeek: l.hoursPerWeek,
          roomId: l.roomId ?? '',
        })),
    )
  }

  const save = async () => {
    if (!editing) return
    setSaving(true)
    onError(null)
    try {
      await saveOwnerLoads(
        editing.id,
        rows.map((r) => ({
          ownerKind: editing.kind,
          ownerId: editing.id,
          subjectId: r.subjectId,
          subGroup: r.subGroup,
          teacherId: r.teacherId,
          hoursPerWeek: r.hoursPerWeek,
          roomId: r.roomId || null,
        })),
      )
      const name = editing.name
      setEditing(null)
      await onSaved(`${name}: yuklama saqlandi`)
    } catch (e) {
      onError(errText(e, "Yuklamani saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const fill = async () => {
    setFilling(true)
    onError(null)
    try {
      const r = await fillLoadsFromSchedule(null, overwrite)
      setFillOpen(false)
      await onSaved(
        `Joriy jadvaldan to'ldirildi: ${r.owners} ta sinf/guruh, ${r.rows} ta fan` +
          (r.skipped > 0 ? ` (${r.skipped} tasida yuklama bor edi — o'zgarmadi)` : ''),
      )
    } catch (e) {
      onError(errText(e, "To'ldirib bo'lmadi"))
    } finally {
      setFilling(false)
    }
  }

  const totalHours = rows.reduce((s, r) => s + (Number(r.hoursPerWeek) || 0), 0)
  const rowErrors = rows.map((r, i) => {
    if (!r.subjectId || !r.teacherId) return "Fan va o'qituvchini tanlang"
    if (rows.some((x, j) => j !== i && x.subjectId === r.subjectId && x.subGroup === r.subGroup)) return 'Takrorlangan'
    if (rows.some((x) => x.subjectId === r.subjectId && (x.subGroup === 0) !== (r.subGroup === 0)))
      return 'Bir fan ham butun sinfga, ham yarmiga bo‘lingan'
    if (!(r.hoursPerWeek >= 1 && r.hoursPerWeek <= 20)) return '1–20 soat'
    return null
  })

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-slate-500">
          Jami: {new Set(loads.map((l) => l.ownerId)).size} ta sinf/guruhda yuklama bor, haftasiga{' '}
          {loads.reduce((s, l) => s + l.hoursPerWeek, 0)} soat.
        </p>
        <Button variant="secondary" onClick={() => setFillOpen(true)}>
          <RefreshCw className="h-4 w-4" /> Joriy jadvaldan to'ldirish
        </Button>
      </div>

      <Card className="overflow-hidden p-0">
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-500">
            <tr>
              <th className="px-4 py-2">Sinf / guruh</th>
              <th className="px-4 py-2">Fanlar</th>
              <th className="px-4 py-2">Kunlar</th>
              <th className="px-4 py-2 text-right">Haftasiga</th>
              <th className="px-4 py-2" />
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {owners.map((o) => {
              const mine = loads.filter((l) => l.ownerId === o.id)
              const hours = mine.reduce((s, l) => s + (l.subGroup === 2 ? 0 : l.hoursPerWeek), 0)
              const cap = capacityOf(o)
              return (
                <tr key={o.id} className="hover:bg-slate-50">
                  <td className="px-4 py-2 font-medium text-slate-800">
                    {o.name}
                    {o.kind === 'group' && (
                      <span className="ml-1.5 text-[10px] uppercase text-slate-400">{o.isTrack ? "yo'nalish" : 'guruh'}</span>
                    )}
                  </td>
                  <td className="px-4 py-2 text-slate-500">
                    {mine.length === 0 ? (
                      <span className="text-amber-700">yuklama kiritilmagan</span>
                    ) : (
                      <span className="line-clamp-2">
                        {[...new Set(mine.map((l) => l.subjectId))]
                          .map((s) => `${subjectName(s)} ${Math.max(...mine.filter((l) => l.subjectId === s).map((l) => l.hoursPerWeek))}`)
                          .join(', ')}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-2">
                    <select
                      value={studyDays.find((d) => d.ownerId === o.id)?.days ?? 6}
                      onChange={(e) => onDays(o, Number(e.target.value))}
                      className="h-8 rounded-lg border border-slate-200 bg-white px-2 text-sm outline-none focus:border-brand-400"
                      title="Haftalik o'quv kunlari"
                    >
                      <option value={5}>5 kun (Du–Ju)</option>
                      <option value={6}>6 kun (Du–Sha)</option>
                    </select>
                  </td>
                  <td
                    className={cn('px-4 py-2 text-right tabular-nums', hours > cap ? 'text-red-600' : 'text-slate-700')}
                    title={`Sig'adi: ${cap} soat`}
                  >
                    {hours} soat
                  </td>
                  <td className="px-4 py-2 text-right">
                    <Button variant="ghost" className="px-3 py-1" onClick={() => open(o)}>
                      Tahrirlash
                    </Button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </Card>

      <Modal
        open={!!editing}
        onClose={() => setEditing(null)}
        size="xl"
        title={editing ? `${editing.name} — yuklama` : ''}
        footer={
          <>
            <span className="mr-auto text-sm text-slate-500">Haftasiga jami: {totalHours} soat</span>
            <Button variant="secondary" onClick={() => setEditing(null)}>
              Bekor qilish
            </Button>
            <Button disabled={saving || rowErrors.some(Boolean)} onClick={() => void save()}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        {editing && (
          <div className="space-y-3">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="text-left text-xs text-slate-500">
                  <tr>
                    <th className="py-1 pr-2">Fan</th>
                    {hasHalves(editing) && <th className="py-1 pr-2">Kimga</th>}
                    <th className="py-1 pr-2">O'qituvchi</th>
                    <th className="py-1 pr-2">Soat/hafta</th>
                    <th className="py-1 pr-2">Xona</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r, i) => {
                    const forSubject = teachers.filter((t) => t.subjectIds.includes(r.subjectId))
                    const options = forSubject.length > 0 ? forSubject : teachers
                    const set = (patch: Partial<EditRow>) =>
                      setRows((prev) => prev.map((x, j) => (j === i ? { ...x, ...patch } : x)))
                    const cls = 'h-9 w-full rounded-lg border border-slate-200 bg-white px-2 text-sm outline-none focus:border-brand-400'
                    return (
                      <tr key={i} className="align-top">
                        <td className="py-1 pr-2">
                          <select className={cls} value={r.subjectId} onChange={(e) => set({ subjectId: e.target.value })}>
                            <option value="">Tanlang</option>
                            {subjects.map((s) => (
                              <option key={s.id} value={s.id}>
                                {s.name}
                              </option>
                            ))}
                          </select>
                          {rowErrors[i] && <p className="mt-0.5 text-[11px] text-red-600">{rowErrors[i]}</p>}
                        </td>
                        {hasHalves(editing) && (
                          <td className="py-1 pr-2">
                            <select className={cls} value={r.subGroup} onChange={(e) => set({ subGroup: Number(e.target.value) })}>
                              <option value={0}>{editing.isTrack ? "Butun yo'nalish" : 'Butun sinf'}</option>
                              <option value={1}>{editing.isTrack ? 'Katta guruh' : '1-guruh'}</option>
                              <option value={2}>{editing.isTrack ? 'Kichik guruh' : '2-guruh'}</option>
                            </select>
                          </td>
                        )}
                        <td className="py-1 pr-2">
                          <select className={cls} value={r.teacherId} onChange={(e) => set({ teacherId: e.target.value })}>
                            <option value="">Tanlang</option>
                            {options.map((t) => (
                              <option key={t.id} value={t.id}>
                                {t.fullName}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td className="w-24 py-1 pr-2">
                          <input
                            type="number"
                            min={1}
                            max={20}
                            className={cls}
                            value={r.hoursPerWeek}
                            onChange={(e) => set({ hoursPerWeek: Number(e.target.value) })}
                          />
                        </td>
                        <td className="py-1 pr-2">
                          <select className={cls} value={r.roomId} onChange={(e) => set({ roomId: e.target.value })}>
                            <option value="">
                              {rooms.homeRooms[editing.id] ? `Sinf xonasi (${roomName(rooms.homeRooms[editing.id])})` : 'Sinf xonasi'}
                            </option>
                            {rooms.rooms.map((rm) => (
                              <option key={rm.id} value={rm.id}>
                                {rm.name}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td className="py-1">
                          <button
                            type="button"
                            onClick={() => setRows((prev) => prev.filter((_, j) => j !== i))}
                            className="flex h-9 w-9 items-center justify-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-600"
                            aria-label="Qatorni o'chirish"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <Button
              variant="secondary"
              onClick={() => setRows((prev) => [...prev, { subjectId: '', subGroup: 0, teacherId: '', hoursPerWeek: 2, roomId: '' }])}
            >
              <Plus className="h-4 w-4" /> Fan qo'shish
            </Button>
            <p className="text-xs text-slate-400">
              Bo'lingan darsda (1-/2-guruh yoki katta/kichik) har yarmi alohida qator — ular bir soatga, har biri o'z ustozi bilan
              qo'yiladi. Xona tanlanmasa — sinfning o'z xonasi.
            </p>
          </div>
        )}
      </Modal>

      <Modal
        open={fillOpen}
        onClose={() => setFillOpen(false)}
        size="sm"
        title="Joriy jadvaldan to'ldirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setFillOpen(false)}>
              Bekor qilish
            </Button>
            <Button disabled={filling} onClick={() => void fill()}>
              {filling ? "To'ldirilmoqda..." : "To'ldirish"}
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Har sinfning hozir amal qilayotgan jadvalidagi darslar sanaladi: fan, haftasiga necha soat, eng ko'p o'tadigan ustoz va
          xona.
        </p>
        <label className="mt-3 flex items-start gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} className="mt-0.5 h-4 w-4 accent-brand-600" />
          <span>
            Mavjud yuklamani ham qayta yozish
            <span className="block text-xs text-slate-400">Belgilanmasa — faqat yuklamasi yo'q sinflar to'ldiriladi.</span>
          </span>
        </label>
      </Modal>
    </div>
  )
}

/* ============================================================== 2. Bandlik */

function BusyTab({
  teachers,
  loads,
  busy,
  periods,
  onSaved,
  onError,
}: {
  teachers: Teacher[]
  loads: TeachingLoad[]
  busy: TeacherUnavailability[]
  periods: number[]
  onSaved: (teacherId: string, slots: UnavailabilitySlot[]) => void
  onError: (msg: string | null) => void
}) {
  const [query, setQuery] = useState('')
  const [teacherId, setTeacherId] = useState<string>('')
  const [slots, setSlots] = useState<UnavailabilitySlot[]>([])
  const [saving, setSaving] = useState(false)

  const hoursOf = (id: string) => loads.filter((l) => l.teacherId === id).reduce((s, l) => s + l.hoursPerWeek, 0)
  const list = teachers
    .filter((t) => t.fullName.toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a, b) => hoursOf(b.id) - hoursOf(a.id) || a.fullName.localeCompare(b.fullName, 'uz'))

  const pick = (id: string) => {
    setTeacherId(id)
    setSlots(busy.find((b) => b.teacherId === id)?.slots ?? [])
  }
  const has = (d: number, p: number) => slots.some((s) => s.day === d && s.period === p)
  const toggle = (d: number, p: number) =>
    setSlots((prev) => {
      if (p === 0)
        return has(d, 0) ? prev.filter((s) => !(s.day === d && s.period === 0)) : [...prev.filter((s) => s.day !== d), { day: d, period: 0 }]
      if (prev.some((s) => s.day === d && s.period === 0)) return prev
      return has(d, p) ? prev.filter((s) => !(s.day === d && s.period === p)) : [...prev, { day: d, period: p }]
    })

  const save = async () => {
    setSaving(true)
    onError(null)
    try {
      await saveUnavailability(teacherId, slots)
      onSaved(teacherId, slots)
    } catch (e) {
      onError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[18rem_1fr]">
      <Card className="p-0">
        <div className="border-b border-slate-100 p-3">
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="O'qituvchini qidirish"
            className="h-9 w-full rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-brand-400"
          />
        </div>
        <ul className="max-h-[32rem] divide-y divide-slate-100 overflow-y-auto">
          {list.map((t) => {
            const n = busy.find((b) => b.teacherId === t.id)?.slots.length ?? 0
            return (
              <li key={t.id}>
                <button
                  type="button"
                  onClick={() => pick(t.id)}
                  className={cn('flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-slate-50', teacherId === t.id && 'bg-brand-50')}
                >
                  <span className="truncate text-slate-700">{t.fullName}</span>
                  <span className="ml-2 shrink-0 text-xs text-slate-400">
                    {hoursOf(t.id) > 0 && `${hoursOf(t.id)} s`}
                    {n > 0 && <span className="ml-1 rounded bg-amber-100 px-1 text-amber-800">band {n}</span>}
                  </span>
                </button>
              </li>
            )
          })}
        </ul>
      </Card>

      <Card>
        {!teacherId ? (
          <p className="text-sm text-slate-500">Chapdan o'qituvchini tanlang.</p>
        ) : (
          <div className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <p className="font-medium text-slate-800">{teachers.find((t) => t.id === teacherId)?.fullName}</p>
                <p className="text-xs text-slate-500">
                  Yuklamasi: haftasiga {hoursOf(teacherId)} soat. Qizil katak — bu vaqtda dars qo'yilmaydi.
                </p>
              </div>
              <Button disabled={saving} onClick={() => void save()}>
                {saving ? 'Saqlanmoqda...' : 'Saqlash'}
              </Button>
            </div>
            <div className="overflow-x-auto">
              <table className="text-sm">
                <thead>
                  <tr>
                    <th className="px-2 py-1 text-left text-xs font-medium text-slate-500">Kun</th>
                    <th className="px-2 py-1 text-xs font-medium text-slate-500">Butun kun</th>
                    {periods.map((p) => (
                      <th key={p} className="px-1 py-1 text-xs font-medium text-slate-500">
                        {p}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {weekDays.map((d, di) => (
                    <tr key={d}>
                      <td className="px-2 py-1 text-slate-700">{d}</td>
                      <td className="px-2 py-1 text-center">
                        <input type="checkbox" checked={has(di, 0)} onChange={() => toggle(di, 0)} className="h-4 w-4 accent-red-600" />
                      </td>
                      {periods.map((p) => {
                        const off = has(di, 0) || has(di, p)
                        return (
                          <td key={p} className="px-0.5 py-0.5">
                            <button
                              type="button"
                              onClick={() => toggle(di, p)}
                              disabled={has(di, 0)}
                              className={cn(
                                'h-8 w-9 rounded-md border text-xs transition-colors',
                                off ? 'border-red-300 bg-red-100 text-red-700' : 'border-slate-200 bg-white text-slate-300 hover:bg-slate-50',
                              )}
                              aria-label={`${d}, ${p}-soat`}
                            >
                              {off ? '✕' : ''}
                            </button>
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}

/* ============================================================== 3. Avtomatik tuzish */

function GenerateTab({
  loads,
  owners,
  onOpen,
  onError,
}: {
  loads: TeachingLoad[]
  owners: LessonOwnerItem[]
  onOpen: (name: string) => void
  onError: (msg: string | null) => void
}) {
  const [name, setName] = useState(`Avtomatik jadval ${today()}`)
  const [noGaps, setNoGaps] = useState(true)
  const [respectUnavailability, setRespectUnavailability] = useState(true)
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<GenerateResult | null>(null)

  const withLoads = owners.filter((o) => loads.some((l) => l.ownerId === o.id))
  const without = owners.filter((o) => !loads.some((l) => l.ownerId === o.id))

  const run = async () => {
    setRunning(true)
    onError(null)
    setResult(null)
    try {
      setResult(await generateSchedule({ name: name.trim(), noGaps, respectUnavailability }))
    } catch (e) {
      onError(errText(e, "Jadvalni tuzib bo'lmadi"))
    } finally {
      setRunning(false)
    }
  }

  const rule = (checked: boolean, set: (v: boolean) => void, title: string, hint: string) => (
    <label className="flex items-start gap-2 text-sm text-slate-700">
      <input type="checkbox" checked={checked} onChange={(e) => set(e.target.checked)} className="mt-0.5 h-4 w-4 accent-brand-600" />
      <span>
        {title}
        <span className="block text-xs text-slate-400">{hint}</span>
      </span>
    </label>
  )

  return (
    <div className="grid gap-4 lg:grid-cols-[24rem_1fr]">
      <Card className="space-y-4">
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-600">Yangi jadval nomi</span>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="h-10 w-full rounded-xl border border-slate-200 px-3 outline-none focus:border-brand-400"
          />
        </label>
        <div className="space-y-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Qoidalar</p>
          <p className="text-xs text-slate-500">
            Har doim: o'qituvchi va xona bir vaqtda bitta joyda, sinfda bir soatda bitta dars, blok soatlari va boshqa
            sinflarning faol jadvali band.
          </p>
          {rule(respectUnavailability, setRespectUnavailability, "O'qituvchi bandligini hisobga olish", "2-bo'limda belgilangan kun va soatlarga dars qo'yilmaydi")}
          {rule(noGaps, setNoGaps, 'Oynasiz jadval', "Sinf darslari orasida bo'sh soat qolmasin, o'qituvchilarga ham kam bo'lsin")}
          <p className="text-xs text-slate-500">
            O'quv kunlari har sinfda o'zicha — "1. Yuklama" jadvalidagi "Kunlar" ustuni (sukut: 1–3-sinflar 5 kun, qolganlari 6 kun).
          </p>
          <p className="text-xs text-slate-400">Bir fan kuniga ko'pi bilan ⌈soat/kunlar⌉ marta. Yangi qoidalar keyin qo'shiladi.</p>
        </div>
        <div className="rounded-xl bg-slate-50 px-3 py-2 text-xs text-slate-600">
          {withLoads.length} ta sinf/guruh uchun tuziladi
          {without.length > 0 && (
            <span className="block text-amber-700">
              Yuklamasi yo'q ({without.length}): {without.slice(0, 6).map((o) => o.name).join(', ')}
              {without.length > 6 ? '…' : ''} — ular tuzilmaydi.
            </span>
          )}
        </div>
        <Button className="w-full" disabled={running || !name.trim() || withLoads.length === 0} onClick={() => void run()}>
          <Wand2 className="h-4 w-4" /> {running ? 'Tuzilmoqda... (bir necha soniya)' : 'Jadval tuzish'}
        </Button>
      </Card>

      <Card>
        {!result ? (
          <div className="space-y-2 text-sm text-slate-500">
            <p>Natija shu yerda chiqadi.</p>
            <p>
              Jadval <b>qoralama</b> sifatida yaratiladi — hozirgi jadval va sinflar o'zgarmaydi. Uni "Dars jadvali yaratish"
              sahifasida ko'rib, kerak bo'lsa qo'lda to'g'rilab, keyin "Nashr qilish" bosasiz.
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                {result.unplaced.length === 0 ? (
                  <CheckCircle2 className="h-5 w-5 text-emerald-600" />
                ) : (
                  <AlertTriangle className="h-5 w-5 text-amber-600" />
                )}
                <p className="font-medium text-slate-800">
                  "{result.name}" tuzildi: {result.placed} / {result.total} dars joylashdi
                </p>
              </div>
              <Button onClick={() => onOpen(result.name)}>Jadvalni ochish</Button>
            </div>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              {[
                ['Sinflar', result.owners],
                ['Sinf oynalari', result.ownerGaps],
                ["O'qituvchi oynalari", result.teacherGaps],
                ['Fan takrori (kunda)', result.subjectRepeats],
              ].map(([label, v]) => (
                <div key={label as string} className="rounded-xl bg-slate-50 px-3 py-2">
                  <p className="text-xs text-slate-500">{label}</p>
                  <p className="text-lg font-semibold tabular-nums text-slate-800">{v}</p>
                </div>
              ))}
            </div>
            {result.warnings.length > 0 && (
              <ul className="list-disc space-y-0.5 rounded-xl border border-amber-200 bg-amber-50 py-2 pl-7 pr-3 text-sm text-amber-800">
                {result.warnings.map((w, i) => (
                  <li key={i}>{w}</li>
                ))}
              </ul>
            )}
            {result.unplaced.length > 0 && (
              <div>
                <p className="mb-1 text-sm font-medium text-slate-700">Joylashmagan darslar — qo'lda qo'ying:</p>
                <ul className="divide-y divide-slate-100 rounded-xl border border-slate-100 text-sm">
                  {result.unplaced.map((u, i) => (
                    <li key={i} className="px-3 py-1.5 text-slate-700">
                      {u.ownerName} — {u.subjectName} ({u.teacherNames}): {u.missing} ta
                    </li>
                  ))}
                </ul>
                <p className="mt-1 text-xs text-slate-400">
                  Odatda sabab: o'qituvchi juda band yoki bandlik vaqtlari ko'p, sinfda joy yetmaydi yoki maxsus xona band.
                </p>
              </div>
            )}
          </div>
        )}
      </Card>
    </div>
  )
}
