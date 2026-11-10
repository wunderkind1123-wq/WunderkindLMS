import { useState } from 'react'
import { ChevronRight, ClipboardList, Plus } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { TapScale } from '../components/AppCard'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { blockExamErrorMessage } from '../lib/blockExams'

// Oraliq testlar — yo'nalishlarning haftalik testi (mijoz, 2026-11-10). Oyda uchta oraliq test,
// to'rtinchisi oylik blok test. O'qituvchi savollar sonini o'zi belgilaydi, natija foizda hisoblanadi.
// Ro'yxatni server cheklaydi: dars jadvalida shu o'qituvchiga biriktirilgan yo'nalish·fan.
export default function InterimTestsScreen({ onNavigate, onBack }) {
  const list = useFetch(() => api.interimTests(), [])
  const options = useFetch(() => api.interimOptions(), [])
  const [creating, setCreating] = useState(false)
  const items = list.data
  const canCreate = (options.data || []).length > 0

  return (
    <div className="relative h-full flex flex-col bg-bg">
      <ScreenHeader
        title="Oraliq testlar"
        subtitle="Yo'nalishlarning haftalik testi"
        onBack={onBack}
        trailing={
          canCreate && (
            <button
              onClick={() => setCreating(true)}
              className="w-10 h-10 rounded-full bg-primary text-white flex items-center justify-center"
              aria-label="Yangi test"
            >
              <Plus size={20} />
            </button>
          )
        }
      />
      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        {list.loading && !items ? (
          <Loading label="Testlar yuklanmoqda…" />
        ) : list.error ? (
          <ErrorState error={{ message: blockExamErrorMessage(list.error) }} onRetry={list.reload} />
        ) : !items || items.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><ClipboardList size={30} /></EmptyIllustration>}
            title="Hali oraliq test yo'q"
            subtitle={
              canCreate
                ? "«+» tugmasi bilan test qo'shing: yo'nalish, fan, sana va savollar soni."
                : "Dars jadvalida sizga yo'nalish darsi biriktirilganda bu yerda test qo'sha olasiz."
            }
          />
        ) : (
          items.map((t) => (
            <div key={t.id} className="mb-2.5">
              <TapScale onClick={() => onNavigate?.('interimTestEntry', { id: t.id })}>
                <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3">
                  <div className="w-12 h-12 shrink-0 rounded-xl bg-primary-soft text-primary flex flex-col items-center justify-center">
                    <span className="text-[16px] font-extrabold leading-none">{t.date.slice(8, 10)}</span>
                    <span className="text-[10px] font-bold uppercase">{MONTHS[Number(t.date.slice(5, 7)) - 1]}</span>
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-[15px] font-bold text-text truncate">
                      {t.groupName} · {t.subjectName}
                    </p>
                    <p className="text-[12px] text-muted truncate">
                      {t.number}-oraliq test · {t.questionCount} savol{t.topic ? ` · ${t.topic}` : ''}
                    </p>
                  </div>
                  <div className="shrink-0 text-right">
                    <p className="text-[14px] font-extrabold text-text">{t.averagePercent == null ? '—' : `${fmt(t.averagePercent)}%`}</p>
                    <p className="text-[11px] font-semibold text-muted">
                      {t.doneCount}/{t.pupilCount}
                    </p>
                  </div>
                  <ChevronRight size={20} className="text-faint shrink-0" />
                </div>
              </TapScale>
            </div>
          ))
        )}
      </div>

      <AppSheet open={creating} onClose={() => setCreating(false)} title="Yangi oraliq test">
        {creating && (
          <TestForm
            options={options.data || []}
            onCancel={() => setCreating(false)}
            onSaved={(t) => {
              setCreating(false)
              list.reload()
              onNavigate?.('interimTestEntry', { id: t.id })
            }}
          />
        )}
      </AppSheet>
    </div>
  )
}

const MONTHS = ['yan', 'fev', 'mar', 'apr', 'may', 'iyn', 'iyl', 'avg', 'sen', 'okt', 'noy', 'dek']

export function fmt(v) {
  return String(v).replace('.', ',')
}

export function today() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const field =
  'w-full px-4 h-12 rounded-xl bg-surface2 border border-border outline-none text-[15px] text-text placeholder:text-faint focus:border-primary disabled:opacity-60'

/** Yangi test yoki mavjudini tahrirlash (`test` berilsa — yo'nalish va fan o'zgarmaydi). */
export function TestForm({ options, test, onCancel, onSaved }) {
  const [groupId, setGroupId] = useState(test?.groupId || options[0]?.groupId || '')
  const subjectsOf = (id) => options.find((o) => o.groupId === id)?.subjects || []
  const [subjectId, setSubjectId] = useState(test?.subjectId || (subjectsOf(groupId).length === 1 ? subjectsOf(groupId)[0].id : ''))
  const [date, setDate] = useState(test?.date || today())
  const [count, setCount] = useState(test ? String(test.questionCount) : '')
  const [topic, setTopic] = useState(test?.topic || '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const subjects = subjectsOf(groupId)

  const save = async () => {
    setBusy(true)
    setError(null)
    try {
      const body = { groupId, subjectId, date, questionCount: Number(count), topic: topic.trim() || null }
      const saved = test ? await api.updateInterimTest(test.id, body) : await api.createInterimTest(body)
      onSaved(saved)
    } catch (err) {
      setError(blockExamErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="px-5 pb-6 space-y-3">
      {!test && (
        <>
          <Label text="Yo'nalish">
            <select
              value={groupId}
              onChange={(e) => {
                setGroupId(e.target.value)
                const next = subjectsOf(e.target.value)
                setSubjectId(next.length === 1 ? next[0].id : '')
              }}
              className={field}
            >
              {options.map((o) => (
                <option key={o.groupId} value={o.groupId}>
                  {o.groupName}
                </option>
              ))}
            </select>
          </Label>
          <Label text="Fan">
            <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)} className={field}>
              <option value="">— Tanlang —</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </Label>
        </>
      )}
      <div className="grid grid-cols-2 gap-3">
        <Label text="Sana">
          <input type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} className={field} />
        </Label>
        <Label text="Savollar soni">
          <input
            value={count}
            inputMode="numeric"
            placeholder="Masalan: 20"
            onChange={(e) => setCount(e.target.value.replace(/\D/g, '').slice(0, 3))}
            className={field}
          />
        </Label>
      </div>
      <Label text="Mavzu (ixtiyoriy)">
        <input value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="Masalan: Present Perfect" className={field} />
      </Label>
      {error && <p className="text-[13px] font-semibold text-danger">{error}</p>}
      <div className="pt-2 flex gap-2.5">
        <AppButton label="Bekor qilish" style="ghost" expand onClick={onCancel} />
        <div className="flex-[2]">
          <AppButton
            label={busy ? 'Saqlanmoqda…' : test ? 'Saqlash' : 'Yaratish'}
            expand
            disabled={busy || !subjectId || !count || !date}
            onClick={save}
          />
        </div>
      </div>
    </div>
  )
}

function Label({ text, children }) {
  return (
    <label className="block">
      <span className="block pb-1.5 text-[12px] font-bold text-muted tracking-wide">{text}</span>
      {children}
    </label>
  )
}
