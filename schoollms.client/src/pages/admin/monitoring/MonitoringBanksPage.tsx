import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, Library, Plus, Trash2 } from 'lucide-react'
import {
  deleteBank,
  getBanks,
  getMonitoringOptions,
  monitoringError,
  saveBank,
  type MonitoringBank,
  type MonitoringOptions,
} from '@/api/services/monitoring'
import { downloadFile } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

export const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Monitoring savollar banklari — blok test banklaridan ALOHIDA (mijoz, 2026-11-06). Bank fan va
 * sinf darajasi (yoki yo'nalish) bo'yicha: savollarni shu darajaning ish rejasidagi mavzularga bog'lash
 * mumkin — natijada mavzu bo'yicha tahlil chiqadi.
 */
export function MonitoringBanksPage() {
  const [banks, setBanks] = useState<MonitoringBank[] | null>(null)
  const [options, setOptions] = useState<MonitoringOptions | null>(null)
  const [editing, setEditing] = useState<MonitoringBank | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    Promise.all([getBanks(), getMonitoringOptions()])
      .then(([b, o]) => {
        setBanks(b)
        setOptions(o)
      })
      .catch((e) => setError(monitoringError(e, "Banklarni olib bo'lmadi")))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function remove(b: MonitoringBank): Promise<void> {
    if (!confirm(`«${b.title}» banki va undagi ${b.questionCount} ta savol o'chirilsinmi? O'tkazilgan testlar o'zgarmaydi.`)) return
    try {
      await deleteBank(b.id)
      load()
    } catch (e) {
      alert(monitoringError(e))
    }
  }

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!banks || !options) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Monitoring savollari</h1>
          <p className="text-sm text-slate-400">Ichki monitoring uchun alohida savollar banklari</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => void downloadFile('/admin/monitoring/question-template', 'monitoring-savollar-namuna.xlsx')}>
            <Download className="h-4 w-4" /> Savollar namunasi
          </Button>
          <Button onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> Yangi bank
          </Button>
        </div>
      </div>

      {banks.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <Library className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">Hali bank yo'q</p>
        </Card>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {banks.map((b) => (
            <Card key={b.id} className="p-4">
              <div className="flex items-start justify-between gap-2">
                <Link to={`/admin/monitoring/banks/${b.id}`} className="min-w-0">
                  <p className="font-medium text-brand-700 hover:underline">{b.title}</p>
                  <p className="text-xs text-slate-400">
                    {b.subjectName} · {b.groupName ?? `${b.grade}-sinf`}
                  </p>
                </Link>
                <button type="button" title="O'chirish" onClick={() => void remove(b)} className="rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600">
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
              <p className="mt-3 text-sm text-slate-600">{b.questionCount} ta savol</p>
              {!b.planId && <p className="mt-1 text-xs text-amber-600">Bu fan va sinf uchun ish reja yo'q — mavzuga bog'lab bo'lmaydi</p>}
            </Card>
          ))}
        </div>
      )}

      {editing && (
        <BankModal
          bank={editing === 'new' ? null : editing}
          options={options}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}

export function BankModal({
  bank,
  options,
  onClose,
  onSaved,
}: {
  bank: MonitoringBank | null
  options: MonitoringOptions
  onClose: () => void
  onSaved: (id: string) => void
}) {
  const [title, setTitle] = useState(bank?.title ?? '')
  const [subjectId, setSubjectId] = useState(bank?.subjectId ?? '')
  const [kind, setKind] = useState<'grade' | 'track'>(bank?.groupId ? 'track' : 'grade')
  const [grade, setGrade] = useState(bank?.grade != null ? String(bank.grade) : '')
  const [groupId, setGroupId] = useState(bank?.groupId ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const { id } = await saveBank(bank?.id ?? null, {
        title: title.trim(),
        subjectId,
        grade: kind === 'grade' && grade !== '' ? Number(grade) : null,
        groupId: kind === 'track' ? groupId || null : null,
      })
      onSaved(id)
    } catch (e) {
      setError(monitoringError(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={bank ? 'Bankni tahrirlash' : 'Yangi savollar banki'}
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor qilish</Button>
          <Button onClick={() => void save()} disabled={busy}>{busy ? 'Saqlanmoqda…' : 'Saqlash'}</Button>
        </div>
      }
    >
      <div className="space-y-3 text-sm">
        <label className="block space-y-1">
          <span className="font-medium text-slate-600">Nomi</span>
          <input className={control} value={title} placeholder="Masalan: 7-sinf Matematika — 1-chorak" onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="block space-y-1">
          <span className="font-medium text-slate-600">Fan</span>
          <select className={control} value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">Tanlang</option>
            {options.subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
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
        {bank && <p className="text-xs text-amber-600">Fan yoki sinfni o'zgartirsangiz, savollarning mavzu bog'lanishi olib tashlanadi.</p>}
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
