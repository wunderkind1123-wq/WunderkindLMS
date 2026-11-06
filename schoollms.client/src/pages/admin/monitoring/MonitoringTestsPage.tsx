import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ClipboardList, Plus, Trash2 } from 'lucide-react'
import {
  createTest,
  deleteTest,
  getBank,
  getBanks,
  getMonitoringOptions,
  getMonitoringOwners,
  getTests,
  monitoringError,
  STATUS_LABEL,
  type MonitoringBank,
  type MonitoringDelivery,
  type MonitoringMethod,
  type MonitoringOptions,
  type MonitoringOwnerOption,
  type MonitoringQuestion,
  type MonitoringScoring,
  type MonitoringTestListItem,
} from '@/api/services/monitoring'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn, formatDate } from '@/lib/utils'
import { control } from './MonitoringBanksPage'

/**
 * Ichki monitoring testlari (mijoz, 2026-11-06 — curriculum-monitoring.md §2). Faqat o'quv bo'limi
 * ko'radi. Qog'ozda (javoblar kiritiladi) yoki onlayn (shaxsiy kod); oddiy (foiz) yoki milliy
 * sertifikat uslubi (0–75 ball, Rasch yoki qo'lda vazn).
 */
export function MonitoringTestsPage() {
  const [tests, setTests] = useState<MonitoringTestListItem[] | null>(null)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    getTests().then(setTests).catch((e) => setError(monitoringError(e, "Testlarni olib bo'lmadi")))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function remove(t: MonitoringTestListItem): Promise<void> {
    if (!confirm(`«${t.title}» testi va uning barcha natijalari o'chirilsinmi?`)) return
    try {
      await deleteTest(t.id)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!tests) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ichki monitoring</h1>
          <p className="text-sm text-slate-400">O'quv bo'limining nazorat testlari — natijani faqat o'quv bo'limi ko'radi</p>
        </div>
        <div className="flex gap-2">
          <Link to="/admin/monitoring/banks"><Button variant="secondary">Savollar banki</Button></Link>
          <Button onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" /> Yangi test
          </Button>
        </div>
      </div>

      {tests.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <ClipboardList className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Hali monitoring testi yo'q</p>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Test</th>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Sinflar</th>
                  <th className="px-4 py-3">Usul</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Topshirdi</th>
                  <th className="px-4 py-3 text-right">O'rtacha</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {tests.map((t) => (
                  <tr key={t.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <Link to={`/admin/monitoring/${t.id}`} className="font-medium text-brand-700 hover:underline">{t.title}</Link>
                      <p className="text-xs text-slate-400">{t.subjectName} · {t.groupName ?? `${t.grade}-sinf`} · {t.itemCount} savol</p>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{formatDate(t.testDate)}</td>
                    <td className="px-4 py-3 text-slate-500">{t.owners.join(', ')}</td>
                    <td className="px-4 py-3 text-xs text-slate-600">
                      {t.delivery === 'online' ? 'Onlayn' : "Qog'ozda"} · {t.scoring === 'national' ? `Milliy (${t.nationalMethod === 'rasch' ? 'Rasch' : 'vazn'})` : 'Oddiy'}
                    </td>
                    <td className="px-4 py-3">
                      <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium',
                        t.status === 'closed' ? 'bg-slate-100 text-slate-600' : t.status === 'published' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700')}>
                        {STATUS_LABEL[t.status]}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">{t.doneCount}/{t.participantCount}</td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {t.averagePercent == null ? '—' : `${t.averagePercent}%`}
                      {t.averagePoints != null && <span className="block text-xs text-slate-400">{t.averagePoints} ball</span>}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <button type="button" title="O'chirish" onClick={() => void remove(t)} className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {creating && <CreateTestModal onClose={() => setCreating(false)} />}
    </div>
  )
}

function toLocalInput(d: Date) {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

function CreateTestModal({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const [options, setOptions] = useState<MonitoringOptions | null>(null)
  const [owners, setOwners] = useState<MonitoringOwnerOption[]>([])
  const [banks, setBanks] = useState<MonitoringBank[]>([])
  const [title, setTitle] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [kind, setKind] = useState<'grade' | 'track'>('grade')
  const [grade, setGrade] = useState('')
  const [groupId, setGroupId] = useState('')
  const [delivery, setDelivery] = useState<MonitoringDelivery>('paper')
  const [scoring, setScoring] = useState<MonitoringScoring>('simple')
  const [method, setMethod] = useState<MonitoringMethod>('rasch')
  const [testDate, setTestDate] = useState(new Date().toLocaleDateString('sv-SE'))
  const [opensAt, setOpensAt] = useState(() => toLocalInput(new Date()))
  const [closesAt, setClosesAt] = useState(() => toLocalInput(new Date(Date.now() + 3 * 3600_000)))
  const [timeLimit, setTimeLimit] = useState('60')
  const [chosenOwners, setChosenOwners] = useState<string[]>([])
  const [source, setSource] = useState<'key' | 'bank'>('key')
  const [key, setKey] = useState('')
  const [optionCount, setOptionCount] = useState('4')
  const [bankId, setBankId] = useState('')
  const [bankQuestions, setBankQuestions] = useState<MonitoringQuestion[]>([])
  const [picked, setPicked] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getMonitoringOptions(), getMonitoringOwners(), getBanks()]).then(([o, w, b]) => {
      setOptions(o)
      setOwners(w)
      setBanks(b)
    })
  }, [])

  useEffect(() => {
    if (!bankId) return
    getBank(bankId).then((d) => {
      setBankQuestions(d.questions)
      setPicked(d.questions.map((q) => q.id))
    })
  }, [bankId])

  const matchingBanks = useMemo(
    () => banks.filter((b) => b.subjectId === subjectId && (kind === 'grade' ? String(b.grade) === grade : b.groupId === groupId)),
    [banks, subjectId, kind, grade, groupId],
  )
  // the classes of the chosen grade first, then the rest
  const ownerList = useMemo(() => {
    const first = owners.filter((o) => (kind === 'grade' ? o.grade != null && String(o.grade) === grade : o.id === groupId))
    return [...first, ...owners.filter((o) => !first.includes(o))]
  }, [owners, kind, grade, groupId])

  const useBank = delivery === 'online' || source === 'bank'

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const { id } = await createTest({
        title: title.trim(),
        subjectId,
        grade: kind === 'grade' && grade !== '' ? Number(grade) : null,
        groupId: kind === 'track' ? groupId || null : null,
        delivery,
        scoring,
        nationalMethod: scoring === 'national' ? method : null,
        testDate,
        opensAt: delivery === 'online' ? new Date(opensAt).toISOString() : null,
        closesAt: delivery === 'online' ? new Date(closesAt).toISOString() : null,
        timeLimitMin: delivery === 'online' ? Number(timeLimit) : null,
        owners: chosenOwners.map((k) => {
          const [ownerKind, ...rest] = k.split(':')
          return { kind: ownerKind, id: rest.join(':') }
        }),
        questionIds: useBank ? picked : undefined,
        key: useBank ? undefined : key,
        optionCount: useBank ? undefined : Number(optionCount),
      })
      navigate(`/admin/monitoring/${id}`)
    } catch (e) {
      setError(monitoringError(e, "Testni yaratib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const label = 'font-medium text-slate-600'

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title="Yangi monitoring testi"
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void save()} disabled={busy || !options}>{busy ? 'Yaratilmoqda…' : 'Yaratish'}</Button>
        </div>
      }
    >
      {!options ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4 text-sm">
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="block space-y-1 sm:col-span-2">
              <span className={label}>Nomi</span>
              <input className={control} value={title} placeholder="Masalan: 7-sinf Matematika — 1-chorak monitoringi" onChange={(e) => setTitle(e.target.value)} />
            </label>
            <label className="block space-y-1">
              <span className={label}>Fan</span>
              <select className={control} value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
                <option value="">Tanlang</option>
                {options.subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </label>
            <div className="space-y-1">
              <span className={label}>Ish reja (mavzular shundan)</span>
              <div className="grid grid-cols-2 gap-2">
                <select className={control} value={kind} onChange={(e) => setKind(e.target.value as 'grade' | 'track')} aria-label="Turi">
                  <option value="grade">Sinf darajasi</option>
                  <option value="track">Yo'nalish</option>
                </select>
                {kind === 'grade' ? (
                  <select className={control} value={grade} onChange={(e) => setGrade(e.target.value)} aria-label="Daraja">
                    <option value="">Daraja</option>
                    {options.grades.map((g) => <option key={g} value={g}>{g}-sinf</option>)}
                  </select>
                ) : (
                  <select className={control} value={groupId} onChange={(e) => setGroupId(e.target.value)} aria-label="Yo'nalish">
                    <option value="">Yo'nalish</option>
                    {options.tracks.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                )}
              </div>
            </div>
            <label className="block space-y-1">
              <span className={label}>O'tkazish</span>
              <select className={control} value={delivery} onChange={(e) => setDelivery(e.target.value as MonitoringDelivery)}>
                <option value="paper">Qog'ozda — javoblar kiritiladi</option>
                <option value="online">Onlayn — o'quvchi shaxsiy kod bilan yechadi</option>
              </select>
            </label>
            <label className="block space-y-1">
              <span className={label}>Sana</span>
              <input type="date" className={control} value={testDate} onChange={(e) => setTestDate(e.target.value)} />
            </label>
            <label className="block space-y-1">
              <span className={label}>Baholash</span>
              <select className={control} value={scoring} onChange={(e) => setScoring(e.target.value as MonitoringScoring)}>
                <option value="simple">Oddiy — to'g'ri javob va foiz</option>
                <option value="national">Milliy sertifikat uslubi — 0–75 ball, daraja</option>
              </select>
            </label>
            {scoring === 'national' && (
              <label className="block space-y-1">
                <span className={label}>Ball usuli</span>
                <select className={control} value={method} onChange={(e) => setMethod(e.target.value as MonitoringMethod)}>
                  <option value="rasch">Rasch — T = 50 + 10·Z (savol qiyinligi javoblardan)</option>
                  <option value="weights">Qo'lda vazn — har savol bali</option>
                </select>
              </label>
            )}
          </div>

          {delivery === 'online' && (
            <div className="grid gap-3 rounded-lg border border-slate-200 p-3 sm:grid-cols-3">
              <label className="block space-y-1">
                <span className={label}>Ochiladi</span>
                <input type="datetime-local" className={control} value={opensAt} onChange={(e) => setOpensAt(e.target.value)} />
              </label>
              <label className="block space-y-1">
                <span className={label}>Yopiladi</span>
                <input type="datetime-local" className={control} value={closesAt} onChange={(e) => setClosesAt(e.target.value)} />
              </label>
              <label className="block space-y-1">
                <span className={label}>Vaqt (daqiqa)</span>
                <input className={control} inputMode="numeric" value={timeLimit} onChange={(e) => setTimeLimit(e.target.value)} />
              </label>
            </div>
          )}

          <div className="space-y-1">
            <span className={label}>Qaysi sinf / guruhlar</span>
            <div className="flex max-h-36 flex-wrap gap-1.5 overflow-auto rounded-lg border border-slate-200 p-2">
              {ownerList.map((o) => {
                const k = `${o.kind}:${o.id}`
                const on = chosenOwners.includes(k)
                return (
                  <button key={k} type="button" onClick={() => setChosenOwners(on ? chosenOwners.filter((x) => x !== k) : [...chosenOwners, k])}
                    className={cn('rounded-lg px-2.5 py-1 text-xs font-medium', on ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200')}>
                    {o.name}
                  </button>
                )
              })}
            </div>
          </div>

          <div className="space-y-2 rounded-lg border border-slate-200 p-3">
            {delivery === 'paper' && (
              <div className="flex gap-4">
                <label className="flex items-center gap-2"><input type="radio" checked={source === 'key'} onChange={() => setSource('key')} /> Faqat javoblar kaliti</label>
                <label className="flex items-center gap-2"><input type="radio" checked={source === 'bank'} onChange={() => setSource('bank')} /> Savollar bankidan</label>
              </div>
            )}
            {!useBank ? (
              <div className="grid gap-3 sm:grid-cols-[1fr_8rem]">
                <label className="block space-y-1">
                  <span className={label}>Kalit — har savolning to'g'ri javobi (masalan ABDCA…)</span>
                  <input className={cn(control, 'font-mono tracking-widest')} value={key} onChange={(e) => setKey(e.target.value.toUpperCase().replace(/[^A-F]/g, ''))} />
                  <span className="text-xs text-slate-400">{key.length} ta savol. Mavzu va vaznlarni test sahifasida biriktirasiz.</span>
                </label>
                <label className="block space-y-1">
                  <span className={label}>Variantlar</span>
                  <select className={control} value={optionCount} onChange={(e) => setOptionCount(e.target.value)}>
                    {[2, 3, 4, 5, 6].map((n) => <option key={n} value={n}>{n} ta</option>)}
                  </select>
                </label>
              </div>
            ) : (
              <div className="space-y-2">
                <select className={control} value={bankId} onChange={(e) => setBankId(e.target.value)}>
                  <option value="">{matchingBanks.length ? 'Bankni tanlang' : 'Bu fan va sinf uchun bank yo‘q — avval «Savollar banki»da yarating'}</option>
                  {matchingBanks.map((b) => <option key={b.id} value={b.id}>{b.title} ({b.questionCount})</option>)}
                </select>
                {bankQuestions.length > 0 && (
                  <div className="max-h-52 overflow-auto rounded-lg border border-slate-100">
                    {bankQuestions.map((q) => (
                      <label key={q.id} className="flex items-start gap-2 border-b border-slate-50 px-2 py-1.5 last:border-0">
                        <input type="checkbox" checked={picked.includes(q.id)} onChange={(e) => setPicked(e.target.checked ? [...picked, q.id] : picked.filter((x) => x !== q.id))} />
                        <span className="line-clamp-2 text-slate-700">{q.order}. {q.text}</span>
                      </label>
                    ))}
                  </div>
                )}
                {bankQuestions.length > 0 && <p className="text-xs text-slate-400">{picked.length} ta savol tanlandi</p>}
              </div>
            )}
          </div>
          {error && <p className="whitespace-pre-line text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
