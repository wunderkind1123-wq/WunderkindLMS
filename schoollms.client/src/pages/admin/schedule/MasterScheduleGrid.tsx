/**
 * Umumiy jadval tuzish — hamma sinf bitta jadvalda (mijoz, 2026-09-23: "jadval yaratish
 * bo'limini ham jadvalli qil — hamma sinf va soatlar table sifatida ko'rinib tursin, har
 * katakka bosganda kichik modal ochilib fan va o'qituvchini tahrirlash mumkin bo'lsin").
 *
 * MODEL O'ZGARMAGAN: jadval — har sinfning NOMLI shabloni (masalan "Asosiy"); bu ekran bir
 * xil nomdagi shablonlarni yonma-yon ko'rsatadi. Sinfda shu nomdagi shablon bo'lmasa, u
 * birinchi katak saqlanganda yaratiladi. Katak oynasi — sinf sahifasidagining o'zi
 * (`LessonSlotModal`): bo'linish, o'qituvchi bandligi tekshiruvi, "Tozalash".
 *
 * Qatorlar — sinflar, ustunlar — tanlangan kunning dars soatlari. Bir o'qituvchi bir soatda
 * ikki sinfda turgan katak qizil hoshiya bilan belgilanadi.
 *
 * QORALAMA → NASHR (mijoz, 2026-09-23: "yangi jadval yaratiladi, lekin sinflarga qo'shilmaydi;
 * bitta tugma — nashr qilish; tasdiqlangandan keyin jadval doimiy/faol bo'ladi"):
 *  - FAOL jadval (joriy haftada amal qilayotgani) ham shu yerda tahrirlanadi (2026-10-11, mijoz:
 *    "muharrir noqulay" — ilgari bitta darsni almashtirish uchun butun jadvalni nusxalab qayta nashr
 *    qilish kerak edi). O'zgarish joriy haftadan kuchga kiradi; shablon o'tgan haftalarda ham
 *    ishlatilgan bo'lsa server uning nusxasini oladi va o'tgan haftalar eski versiyada qoladi
 *    (`LiveScheduleEdit`). Darsni sudrab shu qatordagi boshqa soatga o'tkazish yoki ikki darsning
 *    o'rnini almashtirish mumkin;
 *  - "Yangi jadval" — qoralama (bo'sh yoki faolning nusxasi). Sinflarga ta'sir qilmaydi, chunki
 *    haftaga biriktirilmagan shablon hech qayerda ishlatilmaydi;
 *  - "Nashr qilish" — qoralama JORIY haftadan o'quv yili oxirigacha hamma sinfda qo'yiladi.
 *    O'tgan haftalar tegilmaydi (jurnal, davomat, maosh tarixi o'z jadvali bo'yicha qoladi).
 *
 * QATORLAR — DARS EGALARI (docs/modules/track-groups-as-classes.md): 1–8-sinflar, keyin
 * YO'NALISH guruhlari (9–11-sinflar o'rnida — ularning sinfi bu yerda yo'q), keyin (faqat
 * "Guruh darslari" yoqilganda) oddiy guruhlar. Tartib serverda (`/admin/schedule/owners`).
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, CalendarRange, Clock, FileSpreadsheet, Pencil, Plus, Printer, Trash2, Undo2, Wand2 } from 'lucide-react'
import type { LessonOwnerKind, ScheduleLesson, ScheduleTemplate, SchoolSettings, Subject, Teacher } from '@/types'
import { getLessonOwners } from '@/api/services/lessonOwners'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import {
  createTemplate,
  deleteTemplate,
  getCurriculumNorms,
  getOccupiedSlots,
  getTemplates,
  isArchivedTemplateName,
  renameTemplate,
  setLiveTemplateCells,
  type CellChange,
  type CurriculumNorms,
  type LiveCellResult,
} from '@/api/services/scheduleTemplates'
import { ScheduleChecks, type RoomClash, type TeacherClash } from './ScheduleChecks'
import { downloadScheduleExcel, getScheduleRooms, type ScheduleRooms } from '@/api/services/timetableBuilder'
import { getWeekAssignments, saveWeekAssignments } from '@/api/services/weekAssignments'
import { weekDays, schedulePeriods } from '@/config/constants'
import { getCurrentQuarterAndWeek, getEditableQuarterAndWeek, getQuarterWeeks } from '@/lib/weeks'
import {
  bellScheduleOf,
  periodTimeMap,
  periodTimeMapFor,
  timesOverlap,
  type PeriodTime,
} from '@/components/schedule/periodTimes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { subGroupBadge } from '@/lib/subGroups'
import { LessonSlotModal, type OccupiedSlots, type SlotTarget } from '../classes/LessonSlotModal'

const DEFAULT_NAME = 'Asosiy'

/** Jadval qatori — sinf yoki guruh (yo'nalish guruhi sinf kabi). */
interface OwnerRow {
  id: string
  name: string
  kind: LessonOwnerKind
  isTrack: boolean
}

const errText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function MasterScheduleGrid() {
  const [classes, setClasses] = useState<OwnerRow[]>([])
  /** Sinf id → uning hamma shablonlari. */
  const [templates, setTemplates] = useState<Record<string, ScheduleTemplate[]>>({})
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  /** Hamma shablonlardagi band soatlar (hech biri chiqarilmagan) — katak oynasida filtrlanadi. */
  const [occupied, setOccupied] = useState<OccupiedSlots>({})
  const [loading, setLoading] = useState(true)

  const [name, setName] = useState('')
  const [day, setDay] = useState(0)
  const [editing, setEditing] = useState<{ cls: OwnerRow; slot: SlotTarget } | null>(null)
  /** Sudralayotgan dars katagi va ustidan o'tilayotgan katak (faqat shu sinf qatorida). */
  const [drag, setDrag] = useState<{ classId: string; period: number } | null>(null)
  const [dropAt, setDropAt] = useState<number | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const [newOpen, setNewOpen] = useState(false)
  const [newName, setNewName] = useState('')
  /** Yangi qoralama faol jadvaldan nusxa bilan boshlansinmi. */
  const [copyActive, setCopyActive] = useState(true)
  const [creating, setCreating] = useState(false)
  const [assignOpen, setAssignOpen] = useState(false)
  const [assigning, setAssigning] = useState(false)
  /** Sinf id → joriy haftada amal qilayotgan shablon id'si ("faol" jadval). */
  const [activeNow, setActiveNow] = useState<Record<string, string | null>>({})

  /** Jadvalni butunlay (hamma sinfda) qayta nomlash / o'chirish (2026-11-20). */
  const [renameOpen, setRenameOpen] = useState(false)
  const [renameTo, setRenameTo] = useState('')
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [busy, setBusy] = useState(false)

  /** Ish reja bo'yicha haftalik me'yor — "Tekshiruv" paneli uchun. */
  const [normQuarter, setNormQuarter] = useState<number | null>(null)
  const [norms, setNorms] = useState<CurriculumNorms | null>(null)
  /** Tekshiruvdan bosilgan katak — bir oz vaqt belgilanib turadi. */
  const [focus, setFocus] = useState<{ ownerId: string; period: number } | null>(null)

  /** Xonalar va sinflarning o'z xonasi (2026-11-21). */
  const [roomInfo, setRoomInfo] = useState<ScheduleRooms>({ rooms: [], homeRooms: {} })
  /**
   * "Ortga qaytarish" (2026-11-21): har saqlashdan oldingi kataklar holati. Bekor qilish — o'sha
   * holatni oddiy saqlash bilan qaytarish (faol jadvalda ham tarix qoidasi o'z kuchida).
   */
  const [history, setHistory] = useState<{ cls: OwnerRow; cells: CellChange[]; label: string; tplName: string }[]>([])
  const [exporting, setExporting] = useState(false)

  useEffect(() => {
    Promise.all([getLessonOwners(), getSubjects(), getTeachers(), getSettings(), getOccupiedSlots('')])
      .then(async ([owners, subs, tchs, st, occ]) => {
        getScheduleRooms().then(setRoomInfo).catch(() => undefined)
        // Arxivsiz, tartiblangan — server qoidasi (sinflar, yo'nalishlar, oddiy guruhlar).
        const active: OwnerRow[] = owners.map((o) => ({
          id: o.id,
          name: o.name,
          kind: o.kind,
          isTrack: o.isTrack,
        }))
        // "O'chirilgan" (arxivlangan) jadval bu yerda ko'rinmaydi — u faqat o'tgan haftalar tarixi (2026-11-06).
        const tpls = await Promise.all(
          active.map((c) =>
            getTemplates(c.id)
              .then((ts) => ts.filter((t) => !t.isArchived))
              .catch(() => [] as ScheduleTemplate[]),
          ),
        )
        const map: Record<string, ScheduleTemplate[]> = {}
        active.forEach((c, i) => (map[c.id] = tpls[i]))
        setClasses(active)
        setTemplates(map)
        setSubjects(subs)
        setTeachers(tchs)
        setSettings(st)
        setOccupied(occ)
        // Eng ko'p sinfda uchraydigan nom — sukut bo'yicha ochiladi.
        const counts = new Map<string, number>()
        for (const list of tpls) for (const t of list) counts.set(t.name, (counts.get(t.name) ?? 0) + 1)
        const top = [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0]
        // ?name=... — avtomatik tuzilgan jadvalni darhol ochish uchun.
        const wanted = new URLSearchParams(window.location.search).get('name')
        setName(wanted && counts.has(wanted) ? wanted : (top ?? DEFAULT_NAME))
        // Joriy haftada qaysi shablon amal qilyapti — "Faol" belgisi shundan.
        // Yakshanba kuni — kelgusi hafta (server bilan bir xil, 2026-10-11).
        const { quarter: cq, week: cw } = getEditableQuarterAndWeek(st.quarters) ?? getCurrentQuarterAndWeek(st.quarters)
        const wa = await Promise.all(active.map((c) => getWeekAssignments(c.id, cq).catch(() => [])))
        const now: Record<string, string | null> = {}
        active.forEach((c, i) => (now[c.id] = wa[i].find((a) => a.week === cw)?.templateId ?? null))
        setActiveNow(now)
        setNormQuarter(cq)
      })
      .catch((e) => setError(errText(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (normQuarter == null) return
    getCurriculumNorms(normQuarter)
      .then(setNorms)
      .catch(() => setNorms(null))
  }, [normQuarter])

  useEffect(() => {
    if (!focus) return
    document.getElementById(`cell-${focus.ownerId}-${focus.period}`)?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' })
    const t = setTimeout(() => setFocus(null), 2500)
    return () => clearTimeout(t)
  }, [focus])

  /** Hamma sinflardagi shablon nomlari (+ hozir yaratilayotgani); eski versiyalar alohida. */
  const names = useMemo(() => {
    const set = new Set<string>()
    Object.values(templates).forEach((l) => l.forEach((t) => set.add(t.name)))
    if (name) set.add(name)
    return [...set].sort((a, b) => a.localeCompare(b, 'uz'))
  }, [templates, name])
  const currentNames = names.filter((n) => !isArchivedTemplateName(n))
  const archivedNames = names.filter(isArchivedTemplateName)

  /** Egadagi shu nomli shablonlar (server tartibi: amaldagisi birinchi). Odatda bitta. */
  const templatesNamed = (classId: string) => templates[classId]?.filter((t) => t.name === name) ?? []
  /**
   * Shu nomli shablon. Eski ma'lumotda bitta egada bir xil nomli ikkitasi bo'lishi mumkin (server endi
   * buni taqiqlaydi) — unda joriy haftadagisi, bo'lmasa server tartibidagi birinchisi.
   */
  const templateOf = (classId: string) => {
    const list = templatesNamed(classId)
    return list.find((t) => t.id === activeNow[classId]) ?? list[0] ?? null
  }
  /** Bir xil nomli shablonlari bor egalar — qaysi biri nazarda tutilgani noaniq, ommaviy amallar to'xtaydi. */
  const duplicatedOwners = classes.filter((c) => templatesNamed(c.id).length > 1)
  const duplicateError = (owners: OwnerRow[]) =>
    `${owners.map((c) => c.name).join(', ')}: "${name}" nomli ${owners.length > 1 ? 'jadvallar' : 'jadval'} bir nechta. ` +
    "Avval sinf/guruh jadvali sahifasida ortiqchasini qayta nomlang."
  const lessonsAt = (classId: string, period: number) =>
    (templateOf(classId)?.lessons ?? []).filter((l) => l.day === day && l.period === period)

  // Ko'rsatiladigan soatlar: sozlamadagi dars vaqtlari; bo'lmasa — 1..8, band bo'lgan eng katta soatgacha.
  const periodTimes = useMemo(() => periodTimeMap(settings), [settings])
  const periods = useMemo(() => {
    const used = Math.max(
      0,
      ...Object.values(templates).flatMap((l) => l.flatMap((t) => t.lessons.map((x) => x.period))),
    )
    // Alohida qo'ng'iroq jadvalida darslar ko'proq bo'lishi mumkin — ularning ustunlari ham ko'rinsin.
    const configured = Math.max(
      0,
      ...periodTimes.keys(),
      ...(settings?.bellSchedules ?? []).flatMap((b) => b.periods.map((x) => x.period)),
    )
    const n = Math.min(schedulePeriods.length, Math.max(configured || 8, used))
    return schedulePeriods.slice(0, n)
  }, [templates, periodTimes, settings])
  const timeLabel = (p: number) => {
    const t = periodTimes.get(p)
    return t ? `${t.start}–${t.end}` : null
  }
  /** Shu sinf/guruhning o'z dars vaqtlari — alohida qo'ng'iroq jadvali bo'lsa (2026-10-11). */
  const timesByOwner = useMemo(
    () => new Map(classes.map((o) => [o.id, periodTimeMapFor(settings, o.id)])),
    [classes, settings],
  )
  const periodTimesOf = useCallback(
    (ownerId: string) => timesByOwner.get(ownerId) ?? periodTimeMapFor(settings, ownerId),
    [timesByOwner, settings],
  )
  const ownerTimeLabel = (ownerId: string, p: number) => {
    const t = periodTimesOf(ownerId).get(p)
    return t ? `${t.start}–${t.end}` : null
  }

  const subjectOf = (id: string) => subjects.find((s) => s.id === id)
  const teacherName = (id: string) => teachers.find((t) => t.id === id)?.fullName ?? ''
  const shortName = (full: string) => {
    const [last, first] = full.split(' ')
    return first ? `${last} ${first[0]}.` : last ?? ''
  }

  /** Eganing shu jadvaldagi darslari (shablon yo'q — null) — "Tekshiruv" paneli uchun. */
  const lessonsOfOwner = useCallback(
    (ownerId: string) => templates[ownerId]?.find((t) => t.name === name)?.lessons ?? null,
    [templates, name],
  )

  /**
   * To'qnashuvlar — shu jadvalning HAMMA kunlari bo'yicha (2026-11-20: "Tekshiruv" paneli). Bitta
   * egadagi bo'lingan dars (bir soatda 1-/2-guruh) to'qnashuv emas. Ikkala darsning vaqti ma'lum
   * bo'lsa vaqt KESISHUVI, aks holda soat raqami solishtiriladi: sinflarning qo'ng'iroq jadvallari
   * har xil bo'lishi mumkin (2026-10-11).
   */
  const clashList = useMemo(() => {
    type Entry = { ownerId: string; day: number; period: number; time: PeriodTime | undefined }
    const byTeacher = new Map<string, Entry[]>()
    for (const c of classes) {
      const seen = new Set<string>()
      for (const l of templates[c.id]?.find((t) => t.name === name)?.lessons ?? []) {
        if (!l.teacherId) continue
        const k = `${l.teacherId}|${l.day}|${l.period}`
        if (seen.has(k)) continue
        seen.add(k)
        const list = byTeacher.get(l.teacherId) ?? []
        list.push({ ownerId: c.id, day: l.day, period: l.period, time: periodTimesOf(c.id).get(l.period) })
        byTeacher.set(l.teacherId, list)
      }
    }
    const out: TeacherClash[] = []
    for (const [teacherId, list] of byTeacher)
      for (let i = 0; i < list.length; i++)
        for (let j = i + 1; j < list.length; j++) {
          const a = list[i]
          const b = list[j]
          if (a.day !== b.day || a.ownerId === b.ownerId) continue
          if (a.time && b.time ? timesOverlap(a.time, b.time) : a.period === b.period)
            out.push({ teacherId, day: a.day, a: { ownerId: a.ownerId, period: a.period }, b: { ownerId: b.ownerId, period: b.period } })
        }
    return out
  }, [classes, templates, name, periodTimesOf])

  /** To'qnashgan kataklar ("sinf|kun|soat|o'qituvchi") va har kundagi to'qnashuvlar soni. */
  const clashes = useMemo(() => {
    const keys = new Set<string>()
    for (const c of clashList) {
      keys.add(`${c.a.ownerId}|${c.day}|${c.a.period}|${c.teacherId}`)
      keys.add(`${c.b.ownerId}|${c.day}|${c.b.period}|${c.teacherId}`)
    }
    return keys
  }, [clashList])
  const clashesByDay = useMemo(() => {
    const out: number[] = weekDays.map(() => 0)
    for (const c of clashList) out[c.day] = (out[c.day] ?? 0) + 1
    return out
  }, [clashList])

  /** Darsning amaldagi xonasi: o'zi tanlangan xona, bo'lmasa (butun sinf darsida) sinfning o'z xonasi. */
  const effectiveRoom = useCallback(
    (ownerId: string, l: ScheduleLesson) =>
      l.roomId ?? ((l.subGroup ?? 0) === 0 ? (roomInfo.homeRooms[ownerId] ?? null) : null),
    [roomInfo],
  )
  const roomName = useCallback((id: string) => roomInfo.rooms.find((r) => r.id === id)?.name ?? '', [roomInfo])

  /** Xona to'qnashuvlari — bir xona bir vaqtda ikki egada (shu jadval, hamma kunlar). */
  const roomClashList = useMemo(() => {
    type Entry = { ownerId: string; day: number; period: number; time: PeriodTime | undefined }
    const byRoom = new Map<string, Entry[]>()
    for (const c of classes)
      for (const l of templates[c.id]?.find((t) => t.name === name)?.lessons ?? []) {
        const room = effectiveRoom(c.id, l)
        if (!room) continue
        const list = byRoom.get(room) ?? []
        if (!list.some((e) => e.ownerId === c.id && e.day === l.day && e.period === l.period))
          list.push({ ownerId: c.id, day: l.day, period: l.period, time: periodTimesOf(c.id).get(l.period) })
        byRoom.set(room, list)
      }
    const out: RoomClash[] = []
    for (const [roomId, list] of byRoom)
      for (let i = 0; i < list.length; i++)
        for (let j = i + 1; j < list.length; j++) {
          const a = list[i]
          const b = list[j]
          if (a.day !== b.day || a.ownerId === b.ownerId) continue
          if (a.time && b.time ? timesOverlap(a.time, b.time) : a.period === b.period)
            out.push({ roomId, day: a.day, a: { ownerId: a.ownerId, period: a.period }, b: { ownerId: b.ownerId, period: b.period } })
        }
    return out
  }, [classes, templates, name, periodTimesOf, effectiveRoom])

  /** Katak oynasi uchun: xona shu kun va soatda boshqa qaysi egada band. */
  const roomBusyFor = (cls: OwnerRow, period: number) => (roomId: string): string | null => {
    const mine = periodTimesOf(cls.id).get(period)
    for (const o of classes) {
      if (o.id === cls.id) continue
      for (const l of templateOf(o.id)?.lessons ?? []) {
        if (l.day !== day || effectiveRoom(o.id, l) !== roomId) continue
        const theirs = periodTimesOf(o.id).get(l.period)
        if (mine && theirs ? timesOverlap(mine, theirs) : l.period === period) return o.name
      }
    }
    return null
  }

  /** O'qituvchi shu kuni boshqa sinfda shu vaqtda (yoki vaqti noma'lum bo'lsa — shu soatda) bandmi. */
  const teacherBusyAt = (cls: OwnerRow, period: number, teacherId: string): string | null => {
    const mine = periodTimesOf(cls.id).get(period)
    for (const o of classes) {
      if (o.id === cls.id) continue
      for (const l of templateOf(o.id)?.lessons ?? []) {
        if (l.day !== day || l.teacherId !== teacherId) continue
        const theirs = periodTimesOf(o.id).get(l.period)
        if (mine && theirs ? timesOverlap(mine, theirs) : l.period === period) return o.name
      }
    }
    return null
  }

  /**
   * Katak oynasi uchun band soatlar — FAQAT shu jadvalning boshqa sinflaridagi darslar.
   * Qoralama hozirgi faol jadval bilan bir vaqtda ishlamaydi: o'qituvchi faol jadvalda shu
   * soatda dars bersa ham, bu qoralama uchun u band EMAS (aks holda soxta ogohlantirish).
   */
  const occupiedFor = (cls: OwnerRow): OccupiedSlots => {
    const out: OccupiedSlots = {}
    for (const [tid, list] of Object.entries(occupied))
      out[tid] = list.filter((o) => o.templateName === name && o.className !== cls.name)
    return out
  }

  const replaceLocal = (classId: string, tpl: ScheduleTemplate) =>
    setTemplates((prev) => ({
      ...prev,
      [classId]: [...(prev[classId] ?? []).filter((t) => t.id !== tpl.id), tpl],
    }))

  const refreshOccupied = () => getOccupiedSlots('').then(setOccupied).catch(() => undefined)

  /**
   * Server javobini joyiga qo'yadi: o'zgargan shablon (yoki uning yangi nusxasi) va, nusxa olingan
   * bo'lsa, o'tgan haftalarda qolgan eski shablonning yangi nomi. Faol belgisi nusxaga o'tadi.
   */
  const applyLive = (classId: string, r: LiveCellResult) => {
    setTemplates((prev) => {
      const rest = (prev[classId] ?? [])
        .filter((t) => t.id !== r.template.id)
        .map((t) => (t.id === r.archivedId && r.archivedName ? { ...t, name: r.archivedName } : t))
      return { ...prev, [classId]: [...rest, r.template] }
    })
    if (r.archivedId)
      setActiveNow((prev) => (prev[classId] === r.archivedId ? { ...prev, [classId]: r.template.id } : prev))
  }

  /**
   * Kataklarni saqlash — faol jadvalda ham, qoralamada ham bitta yo'l (server farqlaydi).
   * `undo` — bu saqlashning o'zi "Ortga qaytarish" (tarixga yozilmaydi).
   */
  const saveCells = async (cls: OwnerRow, cells: CellChange[], done: string, undo = false) => {
    setError(null)
    setSaving(true)
    try {
      // Sinfda shu nomdagi shablon yo'q bo'lsa — birinchi katakda yaratiladi.
      const tpl = templateOf(cls.id) ?? (await createTemplate(cls.id, name))
      const before: CellChange[] = cells.map((c) => ({
        day: c.day,
        period: c.period,
        lessons: tpl.lessons.filter((l) => l.day === c.day && l.period === c.period).map((l) => ({ ...l })),
      }))
      const rowLive = activeNow[cls.id] === tpl.id
      const r = await setLiveTemplateCells(cls.id, tpl.id, cells)
      applyLive(cls.id, r)
      if (!undo) setHistory((h) => [...h.slice(-29), { cls, cells: before, label: done, tplName: name }])
      setToast(
        r.archivedId
          ? `${done} — o'tgan haftalar avvalgi jadval bo'yicha qoldi`
          : live && !rowLive
            ? `${done} — bu sinfda bu jadval hali faol emas ("Faollashtirish")`
            : done,
      )
      void refreshOccupied()
    } catch (e) {
      setError(errText(e, "Darsni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  /** Oxirgi o'zgarishni bekor qilish — kataklar avvalgi holatiga qaytadi. */
  const undoLast = async () => {
    const last = history[history.length - 1]
    if (!last || saving || last.tplName !== name) return
    setHistory((h) => h.slice(0, -1))
    await saveCells(last.cls, last.cells, `Bekor qilindi: ${last.label}`, true)
  }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null
      if (target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) return
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        void undoLastRef.current()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])
  const undoLastRef = useRef(undoLast)
  useEffect(() => {
    undoLastRef.current = undoLast
  })

  const handleSave = async (d: number, period: number, next: ScheduleLesson[]) => {
    if (!editing) return
    const cls = editing.cls
    setEditing(null)
    await saveCells(cls, [{ day: d, period, lessons: next }], `${cls.name} · ${weekDays[d]} · ${period}-soat saqlandi`)
  }

  const handleClear = async (d: number, period: number) => {
    if (!editing) return
    const cls = editing.cls
    setEditing(null)
    if (!templateOf(cls.id)) return
    await saveCells(cls, [{ day: d, period, lessons: [] }], `${cls.name} · ${weekDays[d]} · ${period}-soat tozalandi`)
  }

  /**
   * Sudrab tashlash: dars shu qatordagi boshqa soatga o'tadi; u yerda dars bo'lsa — o'rinlari
   * almashadi. Ikkala katak bitta so'rovda saqlanadi (hammasi yoki hech biri).
   */
  const handleDrop = async (cls: OwnerRow, from: number, to: number) => {
    setDrag(null)
    setDropAt(null)
    if (from === to) return
    // Katak oynasidagi kabi ogohlantirish: o'qituvchi yangi vaqtda boshqa sinfda bandmi.
    const busy = [
      ...lessonsAt(cls.id, from).map((l) => ({ l, period: to })),
      ...lessonsAt(cls.id, to).map((l) => ({ l, period: from })),
    ]
      .map(({ l, period }) => (l.teacherId ? { teacherId: l.teacherId, where: teacherBusyAt(cls, period, l.teacherId) } : null))
      .find((x) => x?.where)
    if (
      busy &&
      !confirm(
        `${teacherName(busy.teacherId)} shu vaqtda ${busy.where} da ham dars beradi. Baribir o'tkazilsinmi?`,
      )
    )
      return
    const at = (p: number, target: number) => lessonsAt(cls.id, p).map((l) => ({ ...l, day, period: target }))
    await saveCells(
      cls,
      [
        { day, period: to, lessons: at(from, to) },
        { day, period: from, lessons: at(to, from) },
      ],
      lessonsAt(cls.id, to).length > 0
        ? `${cls.name} · ${weekDays[day]}: ${from}- va ${to}-soat o'rin almashdi`
        : `${cls.name} · ${weekDays[day]}: ${from}-soatdagi dars ${to}-soatga o'tdi`,
    )
  }

  /**
   * Yangi qoralama. Nusxa tanlansa — har sinfning FAOL shablonidagi darslar yangi nomdagi
   * shablonga ko'chiriladi (katakma-katak). Sinflarga ta'sir qilmaydi: haftaga biriktirilmagan.
   */
  const createDraft = async () => {
    const draft = newName.trim()
    if (!draft) return
    setCreating(true)
    setError(null)
    try {
      if (copyActive) {
        await Promise.all(
          classes.map(async (c) => {
            const src = (templates[c.id] ?? []).find((t) => t.id === activeNow[c.id])
            if (!src || src.lessons.length === 0) return
            const tpl = await createTemplate(c.id, draft)
            const cells = new Map<string, ScheduleLesson[]>()
            for (const l of src.lessons) {
              const k = `${l.day}|${l.period}`
              cells.set(k, [...(cells.get(k) ?? []), { ...l }])
            }
            // Sinfning hamma kataklari bitta so'rovda (ilgari har katakka alohida so'rov ketardi).
            const r = await setLiveTemplateCells(
              c.id,
              tpl.id,
              [...cells.entries()].map(([k, lessons]) => {
                const [d, p] = k.split('|').map(Number)
                return { day: d, period: p, lessons }
              }),
            )
            replaceLocal(c.id, r.template)
          }),
        )
      }
      setName(draft)
      setNewOpen(false)
      setToast(`"${draft}" qoralamasi yaratildi — sinflarga hali ta'sir qilmaydi`)
    } catch (e) {
      setError(errText(e, "Yangi jadvalni yaratib bo'lmadi"))
    } finally {
      setCreating(false)
    }
  }

  /**
   * "Nashr qilish" (mijoz, 2026-09-23: "chorakka bo'lib o'tirishi shart emas — aktiv holatdagi
   * jadval sifatida qabul qilinsa bo'ldi"). Jadval JORIY haftadan o'quv yili oxirigacha
   * hamma sinfda qo'yiladi; o'tgan haftalar TEGILMAYDI — jurnal, davomat va maosh tarixi
   * o'sha haftalarning jadvali bo'yicha qoladi. Ichkarida bu oddiy hafta biriktirish.
   */
  const makeActive = async (only?: OwnerRow) => {
    if (!settings) return
    // Joriy hafta, yakshanba kuni — kelgusi hafta: o'tib bo'lgan kunlar qayta yozilmaydi.
    const current = getEditableQuarterAndWeek(settings.quarters)
    if (!current) {
      setError("O'quv yili choraklari tugagan — avval Sozlamalar → Choraklar'ni yangilang.")
      return
    }
    const { quarter: cq, week: cw } = current
    const plan = settings.quarters
      .filter((q) => q.quarter >= cq && q.startDate && q.endDate)
      .map((q) => ({
        quarter: q.quarter,
        weeks: getQuarterWeeks(q.startDate, q.endDate)
          .map((w) => w.week)
          .filter((w) => q.quarter > cq || w >= cw),
      }))
    const targets = only ? [only] : classes.filter((c) => templateOf(c.id))
    // Bir xil nomli ikki shablondan noto'g'risi joriy va kelgusi haftalarga qo'yilib qolmasin.
    const ambiguous = targets.filter((c) => templatesNamed(c.id).length > 1)
    if (ambiguous.length > 0) {
      setError(duplicateError(ambiguous))
      return
    }
    setAssigning(true)
    setError(null)
    try {
      for (const c of targets) {
        const tplId = templateOf(c.id)!.id
        for (const { quarter, weeks } of plan) {
          if (weeks.length === 0) continue
          const current = await getWeekAssignments(c.id, quarter)
          const byWeek = new Map(current.map((a) => [a.week, a.templateId]))
          weeks.forEach((w) => byWeek.set(w, tplId))
          await saveWeekAssignments(
            c.id,
            quarter,
            [...byWeek.entries()].map(([week, templateId]) => ({ week, templateId })),
          )
        }
      }
      setActiveNow((prev) => {
        const next = { ...prev }
        targets.forEach((c) => (next[c.id] = templateOf(c.id)!.id))
        return next
      })
      setAssignOpen(false)
      setToast(
        only
          ? `${only.name}: "${name}" endi faol jadval`
          : `"${name}" nashr qilindi — endi faol jadval (${targets.length} ta sinf)`,
      )
    } catch (e) {
      setError(errText(e, "Jadvalni nashr qilib bo'lmadi"))
    } finally {
      setAssigning(false)
    }
  }

  /** Jadvalni hamma sinfda qayta nomlash — har sinfdagi shu nomli shablon. */
  const renameAll = async () => {
    const to = renameTo.trim()
    if (!to || to === name) return
    if (duplicatedOwners.length > 0) {
      setError(duplicateError(duplicatedOwners))
      return
    }
    setBusy(true)
    setError(null)
    const owners = classes.filter((c) => templateOf(c.id))
    let done = 0
    try {
      for (const c of owners) {
        const tpl = templateOf(c.id)!
        await renameTemplate(c.id, tpl.id, to)
        done++
      }
    } catch (e) {
      setError(
        errText(e, "Nomni o'zgartirib bo'lmadi") +
          (done > 0 ? ` (${done} ta sinfda o'zgardi — qayta urinib ko'ring)` : ''),
      )
    } finally {
      const renamed = new Set(owners.slice(0, done).map((c) => templateOf(c.id)!.id))
      setTemplates((prev) => {
        const next: Record<string, ScheduleTemplate[]> = {}
        for (const [k, list] of Object.entries(prev)) next[k] = list.map((t) => (renamed.has(t.id) ? { ...t, name: to } : t))
        return next
      })
      if (done === owners.length) {
        setName(to)
        setRenameOpen(false)
        setToast(`Jadval nomi o'zgardi: "${to}"`)
      }
      setBusy(false)
    }
  }

  /**
   * Jadvalni hamma sinfda o'chirish. Faol jadval o'chirilmaydi (sinflar jadvalsiz qolardi). O'tgan
   * haftalarda ishlatilgan shablonni server arxivlaydi — tarix (jurnal, davomat, maosh) saqlanadi.
   */
  const deleteAll = async () => {
    if (duplicatedOwners.length > 0) {
      setError(duplicateError(duplicatedOwners))
      return
    }
    setBusy(true)
    setError(null)
    const owners = classes.filter((c) => templateOf(c.id))
    const removed = new Set<string>()
    try {
      for (const c of owners) {
        const tpl = templateOf(c.id)!
        await deleteTemplate(c.id, tpl.id)
        removed.add(tpl.id)
      }
    } catch (e) {
      setError(errText(e, "Jadvalni o'chirib bo'lmadi") + (removed.size > 0 ? ` (${removed.size} ta sinfda o'chdi)` : ''))
    } finally {
      const next: Record<string, ScheduleTemplate[]> = {}
      for (const [k, list] of Object.entries(templates)) next[k] = list.filter((t) => !removed.has(t.id))
      setTemplates(next)
      if (removed.size === owners.length) {
        // Eng ko'p sinfda uchraydigan qolgan jadvalga o'tamiz.
        const counts = new Map<string, number>()
        for (const list of Object.values(next))
          for (const t of list) if (!isArchivedTemplateName(t.name)) counts.set(t.name, (counts.get(t.name) ?? 0) + 1)
        setName([...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? DEFAULT_NAME)
        setDeleteOpen(false)
        setToast(`"${name}" jadvali o'chirildi`)
      }
      void refreshOccupied()
      setBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const withTemplate = classes.filter((c) => templateOf(c.id)).length
  const activeCount = classes.filter((c) => {
    const t = templateOf(c.id)
    return t && activeNow[c.id] === t.id
  }).length
  const isActive = withTemplate > 0 && activeCount === withTemplate
  /** Faol jadval (qisman bo'lsa ham): o'zgarish joriy haftadan darhol kuchga kiradi. */
  const live = activeCount > 0
  /** Eski versiya ("… gacha") faqat o'tgan haftalarda — tarix, o'zgartirilmaydi. */
  const readOnly = isArchivedTemplateName(name)
  const pickable = subjects.filter((s) => s.isActive !== false)
  const canUndo = history.length > 0 && history[history.length - 1].tplName === name

  return (
    <div className="space-y-4">
      {/* Boshqaruv qatori */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <select
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="h-10 max-w-[18rem] truncate rounded-xl border border-slate-200 bg-white px-3 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
            aria-label="Jadval"
          >
            {currentNames.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
            {archivedNames.length > 0 && (
              <optgroup label="Eski versiyalar (o'tgan haftalar)">
                {archivedNames.map((n) => (
                  <option key={n} value={n}>
                    {n}
                  </option>
                ))}
              </optgroup>
            )}
          </select>
          {!readOnly && withTemplate > 0 && (
            <>
              <button
                type="button"
                onClick={() => {
                  setRenameTo(name)
                  setRenameOpen(true)
                }}
                title="Jadval nomini o'zgartirish (hamma sinfda)"
                aria-label="Jadval nomini o'zgartirish"
                className="flex h-10 w-10 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-500 hover:bg-slate-50 hover:text-slate-700"
              >
                <Pencil className="h-4 w-4" />
              </button>
              <button
                type="button"
                onClick={() => setDeleteOpen(true)}
                title="Jadvalni o'chirish (hamma sinfda)"
                aria-label="Jadvalni o'chirish"
                className="flex h-10 w-10 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-500 hover:bg-red-50 hover:text-red-600"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </>
          )}
          <Button variant="secondary" onClick={() => { setNewName(''); setNewOpen(true) }}>
            <Plus className="h-4 w-4" /> Yangi jadval
          </Button>
          {withTemplate > 0 && (
            <span
              className={cn(
                'rounded-full px-2.5 py-1 text-xs font-medium',
                isActive
                  ? 'bg-emerald-50 text-emerald-700'
                  : activeCount > 0
                    ? 'bg-amber-50 text-amber-700'
                    : 'bg-slate-100 text-slate-500',
              )}
              title={live ? "Hozir amal qilayotgan jadval" : "Nashr qilinmagan — sinflarga ta'sir qilmaydi"}
            >
              {isActive ? 'Faol' : activeCount > 0 ? `Qisman faol · ${activeCount}/${withTemplate}` : 'Qoralama'}
            </span>
          )}
          <span className="text-xs text-slate-400">
            {withTemplate} / {classes.length} sinfda bor
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {!readOnly && (
            <Button
              variant="secondary"
              disabled={saving || !canUndo}
              onClick={() => void undoLast()}
              title={canUndo ? `Ortga qaytarish (Ctrl+Z): ${history[history.length - 1].label}` : "Bekor qilinadigan o'zgarish yo'q"}
            >
              <Undo2 className="h-4 w-4" /> Ortga
            </Button>
          )}
          {withTemplate > 0 && (
            <>
              <Button
                variant="secondary"
                disabled={exporting}
                title="Excel'da yuklab olish: har kun alohida varaq va o'qituvchilar varag'i"
                onClick={async () => {
                  setExporting(true)
                  try {
                    await downloadScheduleExcel(name)
                  } catch (e) {
                    setError(errText(e, "Excel faylni yuklab bo'lmadi"))
                  } finally {
                    setExporting(false)
                  }
                }}
              >
                <FileSpreadsheet className="h-4 w-4" /> Excel
              </Button>
              <Link
                to={`/admin/schedule/print?name=${encodeURIComponent(name)}`}
                target="_blank"
                className="inline-flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                title="Butun haftani chop etish"
              >
                <Printer className="h-4 w-4" /> Chop etish
              </Link>
            </>
          )}
          <Link
            to="/admin/schedule/builder"
            className="inline-flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
            title="Yuklama, o'qituvchilar bandligi va avtomatik jadval tuzish"
          >
            <Wand2 className="h-4 w-4" /> Avtomatik tuzish
          </Link>
          {!live && !readOnly && (
            <Button onClick={() => setAssignOpen(true)} disabled={withTemplate === 0}>
              <CalendarRange className="h-4 w-4" /> Nashr qilish
            </Button>
          )}
        </div>
      </div>

      {/* Hafta kunlari */}
      <div className="flex w-fit flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
        {weekDays.map((d, i) => (
          <button
            key={d}
            type="button"
            onClick={() => setDay(i)}
            className={cn(
              'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
              day === i ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {d}
            {clashesByDay[i] > 0 && (
              <span
                title={`${clashesByDay[i]} ta to'qnashuv — o'qituvchi bir vaqtda ikki joyda`}
                className="ml-1.5 rounded-full bg-red-100 px-1.5 text-[10px] font-semibold text-red-700"
              >
                {clashesByDay[i]}
              </span>
            )}
          </button>
        ))}
      </div>

      {!readOnly && withTemplate > 0 && (
        <ScheduleChecks
          owners={classes}
          lessonsOf={lessonsOfOwner}
          clashes={clashList}
          roomClashes={roomClashList}
          roomName={roomName}
          subjects={subjects}
          teachers={teachers}
          norms={norms}
          quarters={(settings?.quarters ?? []).filter((q) => q.startDate && q.endDate).map((q) => q.quarter)}
          quarter={normQuarter}
          onQuarter={setNormQuarter}
          onJump={(d, ownerId, period) => {
            setDay(d)
            setFocus({ ownerId, period })
          }}
        />
      )}

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {readOnly ? (
        <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
          <b>Eski versiya.</b> Bu jadval faqat o'tgan haftalarda amal qilgan — jurnal, davomat va maosh
          tarixi shunga tayanadi, shuning uchun u o'zgartirilmaydi.
        </div>
      ) : live ? (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          Bu — <b>faol jadval</b>. Katakni bosib darsni o'zgartiring yoki darsni sudrab boshqa soatga
          o'tkazing — o'zgarish <b>joriy haftadan</b> kuchga kiradi, o'tgan haftalar (jurnal, davomat,
          maosh) avvalgi jadval bo'yicha qoladi. Katta o'zgarishni oldindan tayyorlash uchun{' '}
          <b>"Yangi jadval"</b> (qoralama) yarating.
        </div>
      ) : (
        withTemplate > 0 || name ? (
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
            <b>Qoralama.</b> O'zgarishlar sinflarga ta'sir qilmaydi — "Nashr qilish" bosilmaguncha
            hech qayerda ko'rinmaydi.
          </div>
        ) : null
      )}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-max min-w-full border-separate border-spacing-0 text-sm">
            <thead>
              <tr>
                <th className="sticky left-0 z-20 min-w-[7rem] border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
                  Sinf
                </th>
                {periods.map((p) => (
                  <th key={p} className="min-w-[9.5rem] border-b border-l border-slate-100 bg-slate-50 px-2 py-2 text-center font-normal">
                    <div className="text-xs font-semibold text-slate-700">{p}-soat</div>
                    {timeLabel(p) && <div className="text-[10px] tabular-nums text-slate-400">{timeLabel(p)}</div>}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {classes.map((c) => (
                <tr key={c.id} className="group">
                  <td className="sticky left-0 z-10 border-b border-r border-slate-200 bg-white px-3 py-2 group-hover:bg-slate-50">
                    <Link
                      to={`/admin/schedule/manage/${c.id}`}
                      className="font-semibold text-slate-800 hover:text-brand-600"
                      title={c.kind === 'group' ? 'Guruh jadvali — shablonlar va haftalar' : 'Sinf jadvali — shablonlar va haftalar'}
                    >
                      {c.name}
                    </Link>
                    {c.kind === 'group' && (
                      <span className="block text-[10px] font-medium uppercase tracking-wide text-slate-400">
                        {c.isTrack ? "yo'nalish" : 'guruh'}
                      </span>
                    )}
                    {!readOnly && templatesNamed(c.id).length > 1 && (
                      <Link
                        to={`/admin/schedule/manage/${c.id}`}
                        title={`"${name}" nomli ${templatesNamed(c.id).length} ta jadval bor — qaysi biri nazarda tutilgani noaniq. Ortiqchasini qayta nomlang.`}
                        className="mt-0.5 block text-[10px] font-medium text-red-600 hover:underline"
                      >
                        {templatesNamed(c.id).length} ta bir xil nomli jadval
                      </Link>
                    )}
                    {live && !readOnly && templatesNamed(c.id).length <= 1 && activeNow[c.id] !== templateOf(c.id)?.id && (
                      <span className="mt-0.5 flex flex-wrap items-center gap-1 text-[10px] font-medium text-amber-700">
                        {templateOf(c.id) ? 'faol emas' : "jadval yo'q"}
                        {templateOf(c.id) && (
                          <button
                            type="button"
                            disabled={assigning}
                            onClick={() => {
                              if (confirm(`${c.name}: "${name}" jadvali joriy haftadan faol bo'lsinmi? O'tgan haftalar o'zgarmaydi.`))
                                void makeActive(c)
                            }}
                            title="Shu sinf uchun bu jadvalni joriy haftadan o'quv yili oxirigacha qo'yish"
                            className="rounded bg-amber-100 px-1 text-amber-800 hover:bg-amber-200 disabled:opacity-50"
                          >
                            Faollashtirish
                          </button>
                        )}
                      </span>
                    )}
                    {bellScheduleOf(settings, c.id) && (
                      <span
                        title="Alohida qo'ng'iroq jadvali — dars vaqtlari ustun sarlavhasidagidan farq qiladi"
                        className="mt-0.5 flex items-center gap-1 text-[10px] font-medium text-slate-500"
                      >
                        <Clock className="h-3 w-3" /> {bellScheduleOf(settings, c.id)?.name}
                      </span>
                    )}
                  </td>
                  {periods.map((p) => {
                    const lessons = lessonsAt(c.id, p)
                    const clash = lessons.some((l) => l.teacherId && clashes.has(`${c.id}|${day}|${p}|${l.teacherId}`))
                    const focused = focus?.ownerId === c.id && focus.period === p
                    const ownTime = ownerTimeLabel(c.id, p)
                    const dropTarget = drag?.classId === c.id && dropAt === p && drag.period !== p
                    return (
                      <td
                        key={p}
                        className="border-b border-l border-slate-100 p-1"
                        onDragOver={(e) => {
                          if (drag?.classId !== c.id) return
                          e.preventDefault()
                          e.dataTransfer.dropEffect = 'move'
                          if (dropAt !== p) setDropAt(p)
                        }}
                        onDrop={(e) => {
                          if (drag?.classId !== c.id) return
                          e.preventDefault()
                          void handleDrop(c, drag.period, p)
                        }}
                      >
                        {/* div role=button: Firefox <button> ni sudratmaydi. */}
                        <div
                          id={`cell-${c.id}-${p}`}
                          role="button"
                          tabIndex={readOnly ? -1 : 0}
                          aria-disabled={readOnly || saving}
                          onKeyDown={(e) => {
                            if (readOnly || saving || (e.key !== 'Enter' && e.key !== ' ')) return
                            e.preventDefault()
                            setEditing({ cls: c, slot: { day, period: p, lessons } })
                          }}
                          draggable={!readOnly && !saving && lessons.length > 0}
                          onDragStart={(e) => {
                            e.dataTransfer.effectAllowed = 'move'
                            e.dataTransfer.setData('text/plain', `${c.id}|${p}`)
                            setDrag({ classId: c.id, period: p })
                          }}
                          onDragEnd={() => {
                            setDrag(null)
                            setDropAt(null)
                          }}
                          onClick={() => {
                            if (!readOnly && !saving) setEditing({ cls: c, slot: { day, period: p, lessons } })
                          }}
                          title={
                            clash
                              ? "O'qituvchi shu vaqtda boshqa sinfda ham bor"
                              : `${c.name} · ${p}-soat${ownTime ? ` · ${ownTime}` : ''}${
                                  lessons.length > 0 && !readOnly ? ' — bosing yoki sudrab boshqa soatga o\u2018tkazing' : ''
                                }`
                          }
                          className={cn(
                            'flex h-14 w-full flex-col items-stretch justify-center gap-0.5 rounded-lg px-2 text-left transition-colors',
                            'outline-none focus-visible:ring-2 focus-visible:ring-brand-300',
                            readOnly
                              ? 'cursor-default'
                              : lessons.length === 0
                                ? 'cursor-pointer text-slate-300 hover:bg-brand-50/60'
                                : 'cursor-grab hover:brightness-95 active:cursor-grabbing',
                            readOnly && lessons.length === 0 && 'text-slate-200',
                            clash && 'ring-2 ring-red-400',
                            drag?.classId === c.id && drag.period === p && 'opacity-40',
                            dropTarget && 'bg-brand-50 ring-2 ring-brand-400',
                            focused && 'animate-pulse ring-2 ring-amber-400',
                          )}
                        >
                          {lessons.length === 0 ? (
                            <span className="text-center text-lg leading-none">{readOnly ? '' : '+'}</span>
                          ) : (
                            [...lessons]
                              .sort((a, b) => (a.subGroup ?? 0) - (b.subGroup ?? 0))
                              .map((l, i) => {
                                const s = subjectOf(l.subjectId)
                                return (
                                  <span
                                    key={i}
                                    className="block truncate rounded-md px-1.5 py-0.5"
                                    style={{ backgroundColor: s?.color ? `${s.color}1F` : '#f1f5f9' }}
                                  >
                                    <span className="block truncate text-xs font-semibold text-slate-800" style={s?.color ? { color: s.color } : undefined}>
                                      {(l.subGroup ?? 0) > 0 && <span className="mr-1 text-[10px] text-slate-500">{subGroupBadge(l.subGroup ?? 0, c.isTrack)}</span>}
                                      {s?.name ?? '—'}
                                    </span>
                                    {lessons.length === 1 && l.teacherId && (
                                      <span className="block truncate text-[11px] text-slate-500">
                                        {shortName(teacherName(l.teacherId))}
                                        {l.roomId && roomName(l.roomId) ? ` · ${roomName(l.roomId)}` : ''}
                                      </span>
                                    )}
                                  </span>
                                )
                              })
                          )}
                        </div>
                      </td>
                    )
                  })}
                </tr>
              ))}
              {classes.length === 0 && (
                <tr>
                  <td colSpan={periods.length + 1} className="px-4 py-10 text-center text-slate-400">
                    Sinflar yo'q
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <LessonSlotModal
        slot={editing?.slot ?? null}
        subjects={pickable}
        teachers={teachers}
        occupiedSlots={editing ? occupiedFor(editing.cls) : {}}
        timeLabel={editing ? ownerTimeLabel(editing.cls.id, editing.slot.period) : null}
        slotTime={editing ? periodTimesOf(editing.cls.id).get(editing.slot.period) : null}
        ownerKind={editing?.cls.kind ?? 'class'}
        isTrack={editing?.cls.isTrack ?? false}
        onClose={() => setEditing(null)}
        onSave={(d, p, lessons) => void handleSave(d, p, lessons)}
        onClear={(d, p) => void handleClear(d, p)}
        rooms={roomInfo.rooms}
        homeRoomName={editing && roomInfo.homeRooms[editing.cls.id] ? roomName(roomInfo.homeRooms[editing.cls.id]) : null}
        roomBusy={editing ? roomBusyFor(editing.cls, editing.slot.period) : undefined}
      />

      <Modal
        open={newOpen}
        onClose={() => setNewOpen(false)}
        title="Yangi jadval"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setNewOpen(false)}>
              Bekor qilish
            </Button>
            <Button
              disabled={!newName.trim() || names.includes(newName.trim()) || creating}
              onClick={() => void createDraft()}
            >
              {creating ? 'Yaratilmoqda...' : 'Yaratish'}
            </Button>
          </>
        }
      >
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-600">Jadval nomi</span>
          <input
            autoFocus
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder="Masalan: 2-chorak jadvali"
            className="h-10 w-full rounded-xl border border-slate-200 px-3 outline-none focus:border-brand-400"
          />
        </label>
        {names.includes(newName.trim()) && <p className="mt-1 text-xs text-amber-700">Bu nomdagi jadval bor</p>}
        <label className="mt-3 flex items-start gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={copyActive}
            onChange={(e) => setCopyActive(e.target.checked)}
            className="mt-0.5 h-4 w-4 accent-brand-600"
          />
          <span>
            Faol jadvaldan nusxa olish
            <span className="block text-xs text-slate-400">
              Kichik o'zgarish uchun — hammasini qaytadan to'ldirish shart emas.
            </span>
          </span>
        </label>
        <p className="mt-3 text-xs text-slate-400">
          Yangi jadval qoralama bo'lib turadi — "Nashr qilish" bosilmaguncha sinflarga ta'sir qilmaydi.
        </p>
      </Modal>

      <Modal
        open={assignOpen}
        onClose={() => setAssignOpen(false)}
        title="Jadvalni nashr qilish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setAssignOpen(false)}>
              Bekor qilish
            </Button>
            <Button onClick={() => void makeActive()} disabled={assigning}>
              {assigning ? 'Nashr qilinmoqda...' : 'Nashr qilish'}
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Nashr qilingach <b>"{name}"</b> doimiy (faol) jadvalga aylanadi va <b>shu haftadan boshlab</b>{' '}
          {withTemplate} ta sinfda amal qiladi: dars jadvali, jurnal, davomat va Mini App shu bo'yicha ishlaydi.
        </p>
        <p className="mt-2 text-xs text-slate-400">
          Hozirgi faol jadval o'tgan haftalar uchun saqlanadi — u haftalarning jurnali, davomati va maoshi
          o'zgarmaydi.
        </p>
      </Modal>

      <Modal
        open={renameOpen}
        onClose={() => setRenameOpen(false)}
        title="Jadval nomini o'zgartirish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRenameOpen(false)}>
              Bekor qilish
            </Button>
            <Button
              disabled={
                busy ||
                !renameTo.trim() ||
                renameTo.trim() === name ||
                names.includes(renameTo.trim()) ||
                isArchivedTemplateName(renameTo.trim())
              }
              onClick={() => void renameAll()}
            >
              {busy ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-600">Yangi nom</span>
          <input
            autoFocus
            value={renameTo}
            onChange={(e) => setRenameTo(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') void renameAll()
            }}
            className="h-10 w-full rounded-xl border border-slate-200 px-3 outline-none focus:border-brand-400"
          />
        </label>
        {renameTo.trim() !== name && names.includes(renameTo.trim()) && (
          <p className="mt-1 text-xs text-amber-700">Bu nomdagi jadval bor</p>
        )}
        <p className="mt-3 text-xs text-slate-400">
          Nom {withTemplate} ta sinfda o'zgaradi. Darslar, haftalar va faollik o'zgarmaydi.
        </p>
      </Modal>

      <Modal
        open={deleteOpen}
        onClose={() => setDeleteOpen(false)}
        title="Jadvalni o'chirish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleteOpen(false)}>
              {live ? 'Yopish' : 'Bekor qilish'}
            </Button>
            {!live && (
              <Button variant="danger" disabled={busy} onClick={() => void deleteAll()}>
                {busy ? "O'chirilmoqda..." : "O'chirish"}
              </Button>
            )}
          </>
        }
      >
        {live ? (
          <p className="text-sm text-slate-600">
            <b>"{name}"</b> hozir {activeCount} ta sinfda <b>faol</b> — uni o'chirsangiz bu sinflar joriy haftadan
            jadvalsiz qoladi. Avval yangi jadvalni nashr qiling, keyin buni o'chirish mumkin bo'ladi.
          </p>
        ) : (
          <>
            <p className="text-sm text-slate-600">
              <b>"{name}"</b> jadvali {withTemplate} ta sinfdan o'chiriladi.
            </p>
            <p className="mt-2 text-xs text-slate-400">
              Agar u o'tgan haftalarda ishlatilgan bo'lsa, o'sha haftalarning jurnali, davomati va maoshi saqlanadi —
              jadval faqat ro'yxatdan yo'qoladi va kelgusi haftalardan olinadi.
            </p>
          </>
        )}
      </Modal>

      <Toast message={toast} onClose={closeToast} />
    </div>
  )
}
