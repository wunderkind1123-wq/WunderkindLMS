import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight, Settings2, Trophy } from 'lucide-react'
import type { Subject } from '@/types'
import {
  BLOCK_ROLE_LABEL,
  certificateError,
  getCertificateRating,
  getTrackBlockSubjects,
  saveTrackBlockSubjects,
  type CertificateRatingRow,
  type TrackBlockSubjects,
} from '@/api/services/certificates'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const fmt = (n: number | null | undefined) => (n == null ? '—' : Number.isInteger(n) ? String(n) : n.toFixed(1))

/**
 * "Reyting" tab'i (mijoz, 2026-11-06: "qaysi o'quvchi hozirda nechta sertifikatga egaligi umumiy
 * necha ball to'plaganligini ko'rish" — curriculum-monitoring.md §3). Ball — blok testdagi
 * ekvivalent: sertifikat turidagi foiz × fanning o'quvchi yo'nalishidagi o'rni (1-fan 93, 2-fan
 * 63, majburiy 11). Hisob SERVERDA; jami ballga har fandan eng yaxshi sertifikat kiradi.
 */
export function CertificateRatingTab({ classNames, subjects }: { classNames: string[]; subjects: Subject[] }) {
  const [className, setClassName] = useState('')
  const [trackId, setTrackId] = useState('')
  const [includeExpired, setIncludeExpired] = useState(false)
  const [rows, setRows] = useState<CertificateRatingRow[]>([])
  const [tracks, setTracks] = useState<TrackBlockSubjects[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState<string | null>(null)
  const [settings, setSettings] = useState(false)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    getTrackBlockSubjects().then(setTracks).catch(() => setTracks([]))
  }, [version])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni belgilaymiz (maqsadli)
    setLoading(true)
    getCertificateRating({ className: className || undefined, trackId: trackId || undefined, includeExpired })
      .then((d) => {
        if (active) setRows(d)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [className, trackId, includeExpired, version])

  const unset = tracks.filter((t) => !t.subject1Id || !t.subject2Id)
  const totalCerts = rows.reduce((s, r) => s + r.certificateCount, 0)

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
        <select className={control} value={className} onChange={(e) => setClassName(e.target.value)} aria-label="Sinf">
          <option value="">Barcha sinflar</option>
          {classNames.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <select className={control} value={trackId} onChange={(e) => setTrackId(e.target.value)} aria-label="Yo'nalish">
          <option value="">Barcha yo'nalishlar</option>
          {tracks.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={includeExpired} onChange={(e) => setIncludeExpired(e.target.checked)} />
          Muddati o'tganlarni ham qo'shish
        </label>
        <div className="ml-auto">
          <Button variant="secondary" onClick={() => setSettings(true)}>
            <Settings2 className="h-4 w-4" /> Yo'nalish fanlari
          </Button>
        </div>
      </div>

      {unset.length > 0 && (
        <p className="border-b border-amber-100 bg-amber-50 px-4 py-2.5 text-sm text-amber-800">
          {unset.map((t) => t.name).join(', ')} — 1-fan va 2-fan belgilanmagan. Bu yo'nalish o'quvchilarining
          sertifikatlari majburiy fan (11 ball) sifatida hisoblanadi. «Yo'nalish fanlari» tugmasidan belgilang.
        </p>
      )}

      {loading ? (
        <div className="p-6">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : rows.length === 0 ? (
        <div className="flex flex-col items-center gap-2 p-10 text-center">
          <Trophy className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-400">Bu shartlar bo'yicha sertifikatli o'quvchi yo'q.</p>
        </div>
      ) : (
        <>
          <p className="px-4 pt-3 text-xs text-slate-400">
            {rows.length} o'quvchi · {totalCerts} ta sertifikat. Ball — blok testdagi ekvivalent (1-fan 93, 2-fan 63,
            majburiy 11); jami ballga har fandan eng yaxshi sertifikat kiradi.
          </p>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Yo'nalish</th>
                  <th className="px-4 py-3 text-right">Sertifikatlar</th>
                  <th className="px-4 py-3 text-right">Jami ball</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <RatingRow key={r.studentId} index={i + 1} row={r} open={open === r.studentId} onToggle={() => setOpen(open === r.studentId ? null : r.studentId)} />
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {settings && (
        <TrackSubjectsModal
          tracks={tracks}
          subjects={subjects}
          onClose={() => setSettings(false)}
          onSaved={() => {
            setSettings(false)
            setVersion((v) => v + 1)
          }}
        />
      )}
    </Card>
  )
}

function RatingRow({ index, row, open, onToggle }: { index: number; row: CertificateRatingRow; open: boolean; onToggle: () => void }) {
  return (
    <>
      <tr className="cursor-pointer hover:bg-slate-50/60" onClick={onToggle}>
        <td className="px-4 py-3 text-slate-400 tabular-nums">{index}</td>
        <td className="px-4 py-3 font-medium text-slate-800">
          <span className="inline-flex items-center gap-1.5">
            {open ? <ChevronDown className="h-4 w-4 text-slate-400" /> : <ChevronRight className="h-4 w-4 text-slate-400" />}
            {row.studentName}
          </span>
        </td>
        <td className="px-4 py-3 text-slate-500">{row.className || '—'}</td>
        <td className="px-4 py-3 text-slate-500">{row.trackName ?? <span className="text-slate-300">yo'q</span>}</td>
        <td className="px-4 py-3 text-right tabular-nums text-slate-700">{row.certificateCount}</td>
        <td className="px-4 py-3 text-right font-semibold tabular-nums text-slate-800">
          {row.totalPoints == null ? <span className="font-normal text-slate-300" title="Yo'nalishi yo'q — 1-/2-fan aniqlanmaydi">—</span> : fmt(row.totalPoints)}
        </td>
      </tr>
      {open && (
        <tr className="bg-slate-50/50">
          <td />
          <td colSpan={5} className="px-4 pb-3 pt-1">
            <ul className="space-y-1">
              {row.items.map((it) => (
                <li key={it.id} className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-sm">
                  <span className={cn('font-medium', it.isExpired ? 'text-slate-400 line-through' : 'text-slate-700')}>{it.label}</span>
                  <span className="text-slate-500">{it.subjectName ?? '—'}</span>
                  {it.role && <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">{BLOCK_ROLE_LABEL[it.role]}</span>}
                  <span className={cn('tabular-nums', it.counted ? 'font-semibold text-brand-700' : 'text-slate-400')}>
                    {it.points == null ? 'ball hisoblanmaydi' : `${fmt(it.points)} ball`}
                    {it.points != null && !it.counted && ' (shu fanda yaxshiroq sertifikat bor)'}
                  </span>
                </li>
              ))}
            </ul>
          </td>
        </tr>
      )}
    </>
  )
}

function TrackSubjectsModal({
  tracks,
  subjects,
  onClose,
  onSaved,
}: {
  tracks: TrackBlockSubjects[]
  subjects: Subject[]
  onClose: () => void
  onSaved: () => void
}) {
  const [values, setValues] = useState(() =>
    Object.fromEntries(tracks.map((t) => [t.id, { s1: t.subject1Id ?? '', s2: t.subject2Id ?? '' }])),
  )
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      for (const t of tracks) {
        const v = values[t.id]
        if (v.s1 && v.s1 === v.s2) throw new Error(`${t.name}: 1-fan va 2-fan bir xil bo'lmasin`)
        if (v.s1 !== (t.subject1Id ?? '') || v.s2 !== (t.subject2Id ?? ''))
          await saveTrackBlockSubjects(t.id, v.s1 || null, v.s2 || null)
      }
      onSaved()
    } catch (err) {
      setError(err instanceof Error && !('response' in err) ? err.message : certificateError(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Yo'nalish fanlari (blok test)"
      size="lg"
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void save()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <p className="mb-3 text-sm text-slate-500">
        Har yo'nalishning blok testdagi 1-fani (93 ball) va 2-fani (63 ball). Boshqa fan bo'yicha sertifikat majburiy
        fan (11 ball) sifatida hisoblanadi.
      </p>
      {tracks.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Faol yo'nalish guruhi yo'q.</p>
      ) : (
        <div className="space-y-3">
          {tracks.map((t) => (
            <div key={t.id} className="grid gap-2 rounded-xl border border-slate-200 p-3 sm:grid-cols-[1fr_1fr_1fr] sm:items-center">
              <div>
                <p className="font-medium text-slate-800">{t.name}</p>
                <p className="text-xs text-slate-400">{t.memberCount} o'quvchi</p>
              </div>
              {(['s1', 's2'] as const).map((key) => (
                <select
                  key={key}
                  className={control}
                  aria-label={`${t.name} — ${key === 's1' ? '1-fan' : '2-fan'}`}
                  value={values[t.id][key]}
                  onChange={(e) => setValues((all) => ({ ...all, [t.id]: { ...all[t.id], [key]: e.target.value } }))}
                >
                  <option value="">{key === 's1' ? '1-fan (93)' : '2-fan (63)'} — tanlanmagan</option>
                  {subjects.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              ))}
            </div>
          ))}
        </div>
      )}
      {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
    </Modal>
  )
}
