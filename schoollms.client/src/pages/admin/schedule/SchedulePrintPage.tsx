/**
 * Jadvalni chop etish (mijoz, 2026-11-21: "dars jadvali bilan bog'liq hamma narsani boshqarish").
 * Admin menyusisiz alohida sahifa: har kun — alohida varaq (A4, yotiq). Ikki ko'rinish: sinflar
 * bo'yicha (qator — sinf, ustun — soat) va o'qituvchilar bo'yicha (qator — ustoz, ustun — soat).
 * `?name=` — qaysi jadval (umumiy jadval muharriridagi nom).
 */
import { useEffect, useMemo, useState } from 'react'
import { Printer } from 'lucide-react'
import type { ScheduleTemplate, SchoolSettings, Subject, Teacher } from '@/types'
import { getLessonOwners, type LessonOwnerItem } from '@/api/services/lessonOwners'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import { getScheduleRooms, type ScheduleRooms } from '@/api/services/timetableBuilder'
import { weekDays, schedulePeriods } from '@/config/constants'
import { periodTimeMap } from '@/components/schedule/periodTimes'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const shortName = (full: string) => {
  const [last, first] = full.split(' ')
  return first ? `${last} ${first[0]}.` : (last ?? '')
}

export function SchedulePrintPage() {
  const name = new URLSearchParams(window.location.search).get('name') ?? ''
  const [owners, setOwners] = useState<LessonOwnerItem[]>([])
  const [templates, setTemplates] = useState<Record<string, ScheduleTemplate>>({})
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [rooms, setRooms] = useState<ScheduleRooms>({ rooms: [], homeRooms: {} })
  const [view, setView] = useState<'classes' | 'teachers'>('classes')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([getLessonOwners(), getSubjects(), getTeachers(), getSettings(), getScheduleRooms().catch(() => null)])
      .then(async ([os, subs, tchs, st, rm]) => {
        const tpls = await Promise.all(os.map((o) => getTemplates(o.id).catch(() => [] as ScheduleTemplate[])))
        const map: Record<string, ScheduleTemplate> = {}
        os.forEach((o, i) => {
          const t = tpls[i].find((x) => x.name === name && !x.isArchived)
          if (t) map[o.id] = t
        })
        setOwners(os.filter((o) => map[o.id]))
        setTemplates(map)
        setSubjects(subs)
        setTeachers(tchs)
        setSettings(st)
        if (rm) setRooms(rm)
      })
      .finally(() => setLoading(false))
  }, [name])

  const times = useMemo(() => periodTimeMap(settings), [settings])
  const periods = useMemo(() => {
    const used = Math.max(0, ...Object.values(templates).flatMap((t) => t.lessons.map((l) => l.period)), ...times.keys())
    return schedulePeriods.slice(0, Math.max(used, 1))
  }, [templates, times])
  const days = useMemo(
    () => (Object.values(templates).some((t) => t.lessons.some((l) => l.day === 5)) ? weekDays : weekDays.slice(0, 5)),
    [templates],
  )
  const subjectName = (id: string) => subjects.find((s) => s.id === id)?.name ?? '—'
  const teacherName = (id: string) => teachers.find((t) => t.id === id)?.fullName ?? ''
  const roomName = (id?: string | null) => (id ? (rooms.rooms.find((r) => r.id === id)?.name ?? '') : '')

  const teacherRows = useMemo(() => {
    const ids = new Set<string>()
    for (const t of Object.values(templates)) for (const l of t.lessons) if (l.teacherId) ids.add(l.teacherId)
    return [...ids].sort((a, b) => teacherName(a).localeCompare(teacherName(b), 'uz'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templates, teachers])

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="min-h-screen bg-white p-6 text-slate-900 print:p-0">
      <style>{`@media print { @page { size: A4 landscape; margin: 10mm; } .print-day { break-after: page; } .print-day:last-child { break-after: auto; } }`}</style>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3 print:hidden">
        <div>
          <h1 className="text-xl font-semibold">Dars jadvali — {name || '—'}</h1>
          <p className="text-sm text-slate-500">Har kun alohida varaqda chiqadi (A4, yotiq).</p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
            {(
              [
                ['classes', 'Sinflar bo‘yicha'],
                ['teachers', 'O‘qituvchilar bo‘yicha'],
              ] as const
            ).map(([k, label]) => (
              <button
                key={k}
                type="button"
                onClick={() => setView(k)}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-sm font-medium',
                  view === k ? 'bg-white shadow-sm' : 'text-slate-500',
                )}
              >
                {label}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={() => window.print()}
            className="inline-flex items-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
          >
            <Printer className="h-4 w-4" /> Chop etish
          </button>
        </div>
      </div>

      {owners.length === 0 ? (
        <p className="text-slate-500">"{name}" nomli jadval topilmadi.</p>
      ) : (
        days.map((d, di) => (
          <section key={d} className="print-day mb-8">
            <h2 className="mb-2 text-base font-semibold">
              {d} <span className="font-normal text-slate-500">· {name}</span>
            </h2>
            <table className="w-full border-collapse text-[11px] leading-tight">
              <thead>
                <tr>
                  <th className="border border-slate-400 bg-slate-100 px-1.5 py-1 text-left">
                    {view === 'classes' ? 'Sinf' : 'O‘qituvchi'}
                  </th>
                  {periods.map((p) => {
                    const t = times.get(p)
                    return (
                      <th key={p} className="border border-slate-400 bg-slate-100 px-1 py-1 text-center">
                        {p}-soat
                        {t && <div className="font-normal text-slate-500">{t.start}–{t.end}</div>}
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {view === 'classes'
                  ? owners.map((o) => (
                      <tr key={o.id} className="break-inside-avoid">
                        <td className="border border-slate-400 px-1.5 py-1 font-semibold">{o.name}</td>
                        {periods.map((p) => {
                          const ls = templates[o.id].lessons
                            .filter((l) => l.day === di && l.period === p)
                            .sort((a, b) => (a.subGroup ?? 0) - (b.subGroup ?? 0))
                          return (
                            <td key={p} className="border border-slate-400 px-1 py-0.5 align-top">
                              {ls.map((l, i) => (
                                <div key={i}>
                                  <span className="font-medium">
                                    {(l.subGroup ?? 0) > 0 ? `${l.subGroup}-gr: ` : ''}
                                    {subjectName(l.subjectId)}
                                  </span>
                                  <div className="text-slate-600">
                                    {l.teacherId ? shortName(teacherName(l.teacherId)) : ''}
                                    {roomName(l.roomId) ? ` · ${roomName(l.roomId)}` : ''}
                                  </div>
                                </div>
                              ))}
                            </td>
                          )
                        })}
                      </tr>
                    ))
                  : teacherRows.map((tid) => (
                      <tr key={tid} className="break-inside-avoid">
                        <td className="border border-slate-400 px-1.5 py-1 font-semibold">{shortName(teacherName(tid))}</td>
                        {periods.map((p) => {
                          const here = owners.flatMap((o) =>
                            templates[o.id].lessons
                              .filter((l) => l.day === di && l.period === p && l.teacherId === tid)
                              .map((l) => ({ o, l })),
                          )
                          return (
                            <td key={p} className="border border-slate-400 px-1 py-0.5 align-top">
                              {here.map(({ o, l }, i) => (
                                <div key={i}>
                                  <span className="font-medium">{o.name}</span>
                                  <div className="text-slate-600">
                                    {subjectName(l.subjectId)}
                                    {roomName(l.roomId) ? ` · ${roomName(l.roomId)}` : ''}
                                  </div>
                                </div>
                              ))}
                            </td>
                          )
                        })}
                      </tr>
                    ))}
              </tbody>
            </table>
          </section>
        ))
      )}
    </div>
  )
}
