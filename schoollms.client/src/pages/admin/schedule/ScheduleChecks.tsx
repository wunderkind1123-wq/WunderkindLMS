/**
 * Umumiy jadvalning "Tekshiruv" paneli (mijoz, 2026-11-20: "dars jadvali bilan bog'liq hamma narsani
 * boshqarish"). Tanlangan jadvalning HAMMA kunlari bo'yicha muammolar bir joyda:
 *
 *  - o'qituvchi bir vaqtda ikki joyda (qo'ng'iroq jadvallari har xil bo'lsa — vaqt kesishuvi);
 *  - o'qituvchisi qo'yilmagan dars;
 *  - xona bir vaqtda ikki joyda (2026-11-21);
 *  - "oyna" — kunning birinchi va oxirgi darsi orasidagi bo'sh soat;
 *  - ish reja soatiga mos emas — fan jadvalda haftasiga necha dars va reja bo'yicha qancha kerak
 *    (`/admin/schedule/curriculum-norms`, tanlangan chorak).
 *
 * Qatorni bosish — o'sha kun ochiladi va katak belgilanadi. Hisob faqat brauzerda, ma'lumot —
 * muharrir allaqachon yuklagan shablonlar.
 */
import { useMemo, useState } from 'react'
import { AlertTriangle, CheckCircle2, ChevronDown } from 'lucide-react'
import type { ScheduleLesson, Subject, Teacher } from '@/types'
import type { CurriculumNorms } from '@/api/services/scheduleTemplates'
import { weekDays } from '@/config/constants'
import { cn } from '@/lib/utils'

export interface CheckOwner {
  id: string
  name: string
}

export interface RoomClash {
  roomId: string
  day: number
  a: { ownerId: string; period: number }
  b: { ownerId: string; period: number }
}

export interface TeacherClash {
  teacherId: string
  day: number
  a: { ownerId: string; period: number }
  b: { ownerId: string; period: number }
}

type Issue =
  | { kind: 'clash'; day: number; ownerId: string; period: number; text: string }
  | { kind: 'room'; day: number; ownerId: string; period: number; text: string }
  | { kind: 'teacher'; day: number; ownerId: string; period: number; text: string }
  | { kind: 'gap'; day: number; ownerId: string; period: number; text: string }
  | { kind: 'norm'; ownerId: string; text: string; detail: string; tone: 'less' | 'more' }

const SECTIONS: { kind: Issue['kind']; title: string; hint: string }[] = [
  { kind: 'clash', title: "O'qituvchi bir vaqtda ikki joyda", hint: 'Bittasini boshqa soatga o‘tkazing yoki o‘qituvchini almashtiring' },
  { kind: 'room', title: 'Xona bir vaqtda ikki joyda', hint: 'Darslardan biriga boshqa xona tanlang yoki soatini almashtiring' },
  { kind: 'teacher', title: "O'qituvchi qo'yilmagan", hint: 'Jurnal va maosh uchun o‘qituvchi kerak' },
  { kind: 'gap', title: 'Oyna (darslar orasida bo‘sh soat)', hint: 'O‘quvchilar bo‘sh soatda kutib qoladi' },
  { kind: 'norm', title: "Ish reja soatiga mos emas", hint: 'Haftalik dars soni reja bo‘yicha kerakli songa teng bo‘lishi kerak' },
]

const fmt = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1))

export function ScheduleChecks({
  owners,
  lessonsOf,
  clashes,
  roomClashes,
  roomName,
  subjects,
  teachers,
  norms,
  quarters,
  quarter,
  onQuarter,
  onJump,
}: {
  owners: CheckOwner[]
  /** Eganing shu jadvaldagi darslari; shablon yo'q bo'lsa — null. */
  lessonsOf: (ownerId: string) => ScheduleLesson[] | null
  clashes: TeacherClash[]
  roomClashes: RoomClash[]
  roomName: (id: string) => string
  subjects: Subject[]
  teachers: Teacher[]
  norms: CurriculumNorms | null
  quarters: number[]
  quarter: number | null
  onQuarter: (q: number) => void
  onJump: (day: number, ownerId: string, period: number) => void
}) {
  const [open, setOpen] = useState(false)
  const [shown, setShown] = useState<Record<string, number>>({})

  const issues = useMemo(() => {
    const ownerName = (id: string) => owners.find((o) => o.id === id)?.name ?? '—'
    const subjectName = (id: string) => subjects.find((s) => s.id === id)?.name ?? '—'
    const teacherName = (id: string) => teachers.find((t) => t.id === id)?.fullName ?? '—'
    const out: Issue[] = []
    for (const c of roomClashes)
      out.push({
        kind: 'room',
        day: c.day,
        ownerId: c.a.ownerId,
        period: c.a.period,
        text: `${roomName(c.roomId) || 'Xona'} — ${weekDays[c.day]}: ${ownerName(c.a.ownerId)} (${c.a.period}-soat) va ${ownerName(c.b.ownerId)} (${c.b.period}-soat)`,
      })
    for (const c of clashes)
      out.push({
        kind: 'clash',
        day: c.day,
        ownerId: c.a.ownerId,
        period: c.a.period,
        text: `${teacherName(c.teacherId)} — ${weekDays[c.day]}: ${ownerName(c.a.ownerId)} (${c.a.period}-soat) va ${ownerName(c.b.ownerId)} (${c.b.period}-soat)`,
      })

    for (const o of owners) {
      const lessons = lessonsOf(o.id)
      if (!lessons) continue
      for (const l of lessons)
        if (!l.teacherId)
          out.push({
            kind: 'teacher',
            day: l.day,
            ownerId: o.id,
            period: l.period,
            text: `${o.name} — ${weekDays[l.day]}, ${l.period}-soat: ${subjectName(l.subjectId)}`,
          })
      for (let d = 0; d < weekDays.length; d++) {
        const used = [...new Set(lessons.filter((l) => l.day === d).map((l) => l.period))].sort((a, b) => a - b)
        if (used.length < 2) continue
        for (let p = used[0] + 1; p < used[used.length - 1]; p++)
          if (!used.includes(p))
            out.push({ kind: 'gap', day: d, ownerId: o.id, period: p, text: `${o.name} — ${weekDays[d]}, ${p}-soat bo‘sh` })
      }
    }

    if (norms)
      for (const n of norms.norms) {
        const lessons = lessonsOf(n.ownerId)
        if (!lessons || !owners.some((o) => o.id === n.ownerId)) continue
        // Bo'lingan darsda bir vaqtdagi ikki katak — bitta dars (Fit hisoboti bilan bir xil).
        const placed = new Set(lessons.filter((l) => l.subjectId === n.subjectId).map((l) => `${l.day}|${l.period}`)).size
        // Bayramlar sababli chiqadigan kasr farq e'tiborsiz: faqat 1 va undan katta farq (mijoz, 2026-11-21).
        if (Math.abs(placed - n.perWeek) < 1) continue
        out.push({
          kind: 'norm',
          ownerId: n.ownerId,
          tone: placed < n.perWeek ? 'less' : 'more',
          text: `${ownerName(n.ownerId)} — ${subjectName(n.subjectId)}: jadvalda ${placed}, rejada ~${fmt(n.perWeek)} dars/hafta`,
          detail: `«${n.planTitle}»: ${norms.quarter}-chorakda ${n.planHours} soat, ${fmt(norms.weeks)} o‘quv haftasi`,
        })
      }
    return out
  }, [owners, lessonsOf, clashes, roomClashes, roomName, norms, subjects, teachers])

  const count = issues.length
  const firstOf = (o: Issue) => {
    if (o.kind !== 'norm') return onJump(o.day, o.ownerId, o.period)
    const first = lessonsOf(o.ownerId)?.[0]
    onJump(first?.day ?? 0, o.ownerId, first?.period ?? 1)
  }

  return (
    <div className="rounded-2xl border border-slate-200/80 bg-white">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left"
      >
        <span className="flex items-center gap-2 text-sm font-medium text-slate-700">
          {count === 0 ? (
            <CheckCircle2 className="h-4 w-4 text-emerald-600" />
          ) : (
            <AlertTriangle className="h-4 w-4 text-amber-600" />
          )}
          Tekshiruv
          <span
            className={cn(
              'rounded-full px-2 py-0.5 text-xs font-medium',
              count === 0 ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700',
            )}
          >
            {count === 0 ? 'muammo yo‘q' : `${count} ta muammo`}
          </span>
        </span>
        <ChevronDown className={cn('h-4 w-4 text-slate-400 transition-transform', open && 'rotate-180')} />
      </button>

      {open && (
        <div className="space-y-4 border-t border-slate-100 px-4 py-3">
          {SECTIONS.map((s) => {
            const list = issues.filter((i) => i.kind === s.kind)
            const limit = shown[s.kind] ?? 8
            return (
              <div key={s.kind}>
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="text-sm font-medium text-slate-700">
                    {s.title}{' '}
                    <span className={cn('text-xs', list.length ? 'text-amber-700' : 'text-emerald-600')}>
                      {list.length ? `· ${list.length}` : '· yo‘q'}
                    </span>
                  </p>
                  {s.kind === 'norm' && quarters.length > 0 && (
                    <label className="flex items-center gap-1.5 text-xs text-slate-500">
                      Chorak
                      <select
                        value={quarter ?? ''}
                        onChange={(e) => onQuarter(Number(e.target.value))}
                        className="h-7 rounded-lg border border-slate-200 bg-white px-1.5 text-xs outline-none focus:border-brand-400"
                      >
                        {quarters.map((q) => (
                          <option key={q} value={q}>
                            {q}-chorak
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                </div>
                {list.length > 0 && <p className="text-xs text-slate-400">{s.hint}</p>}
                {s.kind === 'norm' && norms && norms.norms.length === 0 && (
                  <p className="text-xs text-slate-400">
                    Bu chorak uchun ish reja yuklanmagan — soatlar tekshirilmadi (O'quv bo'limi → Ish reja).
                  </p>
                )}
                {list.length > 0 && (
                  <ul className="mt-1.5 divide-y divide-slate-100 rounded-xl border border-slate-100">
                    {list.slice(0, limit).map((i, k) => (
                      <li key={k}>
                        <button
                          type="button"
                          onClick={() => firstOf(i)}
                          className="block w-full px-3 py-1.5 text-left text-sm text-slate-700 hover:bg-slate-50"
                        >
                          {i.kind === 'norm' ? (
                            <>
                              <span className={i.tone === 'less' ? 'text-red-700' : 'text-amber-800'}>{i.text}</span>
                              <span className="block text-[11px] text-slate-400">{i.detail}</span>
                            </>
                          ) : (
                            i.text
                          )}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                {list.length > limit && (
                  <button
                    type="button"
                    onClick={() => setShown((p) => ({ ...p, [s.kind]: list.length }))}
                    className="mt-1 text-xs font-medium text-brand-600 hover:underline"
                  >
                    Yana {list.length - limit} ta
                  </button>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
