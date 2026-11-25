import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, Library, Plus, Trash2 } from 'lucide-react'
import {
  bankApi,
  monitoringError,
  type BankApi,
  type BankScope,
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
 *
 * `scope="teacher"` (`/teacher/savollar`, 2026-11-24): ustoz o'zi dars beradigan fan va sinflarning
 * banklarini ko'radi, yaratadi va Word'dan to'ldiradi. Testni va natijani o'quv bo'limi boshqaradi.
 */
export function MonitoringBanksPage({ scope = 'admin' }: { scope?: BankScope }) {
  const bapi = useMemo(() => bankApi(scope), [scope])
  const teacher = scope === 'teacher'
  const [banks, setBanks] = useState<MonitoringBank[] | null>(null)
  const [options, setOptions] = useState<MonitoringOptions | null>(null)
  const [editing, setEditing] = useState<MonitoringBank | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    Promise.all([bapi.getBanks(), bapi.getOptions()])
      .then(([b, o]) => {
        setBanks(b)
        setOptions(o)
      })
      .catch((e) => setError(monitoringError(e, "Banklarni olib bo'lmadi")))
  }, [bapi])

  useEffect(() => {
    load()
  }, [load])

  async function remove(b: MonitoringBank): Promise<void> {
    if (!confirm(`«${b.title}» banki va undagi ${b.questionCount} ta savol o'chirilsinmi? O'tkazilgan testlar o'zgarmaydi.`)) return
    try {
      await bapi.deleteBank(b.id)
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
          <h1 className="text-xl font-semibold text-slate-800">{teacher ? 'Test savollarim' : 'Monitoring savollari'}</h1>
          <p className="text-sm text-slate-400">
            {teacher
              ? "Dars beradigan fan va sinflaringiz bo'yicha savollar banki. Word'dagi testni yuklab, tekshirib, tahrirlaysiz — testni o'quv bo'limi tuzadi"
              : 'Ichki monitoring uchun alohida savollar banklari'}
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => void downloadFile(bapi.wordTemplatePath, 'test-savollari-namuna.docx')}>
            <Download className="h-4 w-4" /> Word namunasi
          </Button>
          <Button variant="secondary" onClick={() => void downloadFile(bapi.templatePath, 'monitoring-savollar-namuna.xlsx')}>
            <Download className="h-4 w-4" /> Excel namunasi
          </Button>
          <Button onClick={() => setEditing('new')} disabled={teacher && options.subjects.length === 0}>
            <Plus className="h-4 w-4" /> Yangi bank
          </Button>
        </div>
      </div>

      {banks.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <Library className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-500">
            {teacher && options.subjects.length === 0
              ? "Dars jadvalida sizga fan biriktirilmagan — o'quv bo'limiga murojaat qiling"
              : "Hali bank yo'q. «Yangi bank» tugmasini bosing, keyin Word fayldagi testni yuklang"}
          </p>
        </Card>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {banks.map((b) => (
            <Card key={b.id} className="p-4">
              <div className="flex items-start justify-between gap-2">
                <Link to={`${bapi.listPath}/${b.id}`} className="min-w-0">
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
          api={bapi}
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
  api,
  onClose,
  onSaved,
}: {
  bank: MonitoringBank | null
  options: MonitoringOptions
  api: BankApi
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
      const { id } = await api.saveBank(bank?.id ?? null, {
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
            {options.tracks.length > 0 && <option value="track">Yo'nalish</option>}
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
