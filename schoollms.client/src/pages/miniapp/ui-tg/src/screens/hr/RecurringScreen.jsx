/**
 * TAKRORIY — xodimning O'ZIGA qo'ygan takroriy topshiriqlari (mijoz, 2026-11-06:
 * "o'zimga o'zim ham takroriy topshiriq qo'sha olay"; `/api/hr/me/templates`).
 *
 * Faqat xodimning o'zi yaratgan qoidalar ko'rinadi — rahbar unga qo'ygan takroriy
 * topshiriq rahbarniki, bu yerda chiqmaydi. Har kuni belgilangan kunlarda topshiriq
 * avtomatik yaratiladi va "Topshiriqlar" ro'yxatida paydo bo'ladi.
 */
import { useCallback, useState } from 'react'
import { Plus, Repeat } from 'lucide-react'
import { Card, EmptyState, ErrorState, Hero, Loader, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { hrApi, hrErrorMessage } from '../../lib/hrApi'
import { hapticNotify, hapticSelect } from '../../lib/telegram'
import { BottomSheet, Btn, FieldLabel, Hint, INPUT_CLASS, Notice } from './shared'

const WEEKDAYS = [
  [1, 'Du'],
  [2, 'Se'],
  [3, 'Ch'],
  [4, 'Pa'],
  [5, 'Ju'],
  [6, 'Sh'],
  [7, 'Ya'],
]
const WEEKDAY_FULL = ['', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba', 'yakshanba']
const RECURRENCES = [
  ['daily', 'Har kuni'],
  ['weekly', 'Har hafta'],
  ['monthly', 'Har oy'],
]

function ruleLabel(t) {
  const days = t.weekdays ?? []
  if (t.recurrence === 'monthly') return `Oyning ${(t.daysOfMonth ?? []).join(', ')}-sanasi`
  if (t.recurrence === 'weekly') return `Har hafta: ${WEEKDAY_FULL[days[0]] ?? ''}`
  if (days.length === 0 || days.length >= 7) return 'Har kuni'
  return `Har kuni: ${days.map((d) => WEEKDAYS[d - 1]?.[1]).join(', ')}`
}

export function RecurringScreen({ heroTop }) {
  const q = useAsync(async () => {
    try {
      return { ok: true, data: await hrApi.templates() }
    } catch (e) {
      return { ok: false, error: hrErrorMessage(e) }
    }
  }, [])
  const [sheet, setSheet] = useState(null) // null | 'new' | template
  const [notice, setNotice] = useState(null)
  const items = q.data?.ok && Array.isArray(q.data.data) ? q.data.data : []
  const error = q.data && !q.data.ok ? q.data.error : q.error

  const toggle = async (tpl) => {
    hapticSelect()
    try {
      await hrApi.toggleTemplate(tpl.id)
      q.reload()
    } catch (e) {
      setNotice(hrErrorMessage(e))
      hapticNotify('error')
    }
  }

  const remove = async (tpl) => {
    // Telegram ichida brauzer `confirm` oynasi bloklanmaydi, lekin ikki bosqichli tugma xavfsizroq.
    try {
      await hrApi.deleteTemplate(tpl.id)
      hapticNotify('success')
      setSheet(null)
      q.reload()
    } catch (e) {
      setNotice(hrErrorMessage(e))
      hapticNotify('error')
    }
  }

  const close = useCallback(() => setSheet(null), [])

  return (
    <>
      <Screen>
        <Hero title="Takroriy topshiriqlar" subtitle="O‘zingiz uchun — belgilangan kunlarda avtomatik qo‘shiladi">
          {heroTop}
          <div className={heroTop ? 'mt-3' : ''}>
            <Btn variant="hero" size="sm" onClick={() => setSheet('new')}>
              <span className="inline-flex items-center gap-1.5">
                <Plus className="h-[17px] w-[17px]" />
                Qo‘shish
              </span>
            </Btn>
          </div>
        </Hero>

        {notice && (
          <Notice tone="danger" onDismiss={() => setNotice(null)}>
            {notice}
          </Notice>
        )}

        {q.loading && q.data === null ? (
          <Loader label="Yuklanmoqda…" />
        ) : error ? (
          <ErrorState message={error} onRetry={q.reload} />
        ) : items.length === 0 ? (
          <EmptyState icon={<Repeat className="h-10 w-10" />} title="Hali takroriy topshiriq yo‘q. Masalan: har dushanba haftalik reja." />
        ) : (
          <Card>
            {items.map((tpl) => (
              <div key={tpl.id} className="border-t border-slate-100 px-4 py-3 first:border-t-0">
                <div className="flex items-start gap-3">
                  <button type="button" onClick={() => setSheet(tpl)} className="min-w-0 flex-1 text-left">
                    <p className={'break-words text-[15px] font-semibold leading-snug ' + (tpl.isActive ? '' : 'text-slate-400')}>
                      {tpl.title}
                    </p>
                    <p className="mt-0.5 text-[13px] text-slate-500">
                      {ruleLabel(tpl)} · {tpl.dueTime ? `soat ${tpl.dueTime.slice(0, 5)} gacha` : 'kun oxirigacha'}
                    </p>
                  </button>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={tpl.isActive}
                    aria-label={tpl.isActive ? 'To‘xtatish' : 'Yoqish'}
                    onClick={() => void toggle(tpl)}
                    className={'relative h-7 w-12 shrink-0 rounded-full transition ' + (tpl.isActive ? 'bg-brand' : 'bg-slate-200')}
                  >
                    <span
                      className={
                        'absolute top-0.5 h-6 w-6 rounded-full bg-white shadow transition-all ' + (tpl.isActive ? 'left-[22px]' : 'left-0.5')
                      }
                    />
                  </button>
                </div>
              </div>
            ))}
          </Card>
        )}
        <div className="h-4" />
      </Screen>

      {sheet !== null && (
        <RecurringSheet
          key={sheet === 'new' ? 'new' : sheet.id}
          template={sheet === 'new' ? null : sheet}
          onClose={close}
          onDelete={remove}
          onSaved={() => {
            hapticNotify('success')
            setSheet(null)
            q.reload()
          }}
        />
      )}
    </>
  )
}

function RecurringSheet({ template, onClose, onSaved, onDelete }) {
  const [title, setTitle] = useState(template?.title ?? '')
  const [description, setDescription] = useState(template?.description ?? '')
  const [recurrence, setRecurrence] = useState(template?.recurrence ?? 'daily')
  const [weekdays, setWeekdays] = useState(
    template?.recurrence === 'daily' && template.weekdays?.length ? template.weekdays : [1, 2, 3, 4, 5, 6],
  )
  const [weekday, setWeekday] = useState(template?.recurrence === 'weekly' ? template.weekdays?.[0] ?? 1 : 1)
  const [monthDay, setMonthDay] = useState(template?.recurrence === 'monthly' ? String(template.daysOfMonth?.[0] ?? '') : '')
  const [dueTime, setDueTime] = useState(template?.dueTime ? template.dueTime.slice(0, 5) : '18:00')
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [error, setError] = useState(null)

  const submit = async () => {
    if (saving) return
    const cleanTitle = title.trim()
    if (cleanTitle.length < 3) return setError('Sarlavha kamida 3 ta belgi bo‘lsin.')
    if (recurrence === 'daily' && weekdays.length === 0) return setError('Kamida bitta kunni tanlang.')
    const day = Number(monthDay)
    if (recurrence === 'monthly' && !(day >= 1 && day <= 31)) return setError('Oy kuni 1–31 oralig‘ida bo‘lsin.')
    const body = {
      title: cleanTitle,
      description: description.trim() || null,
      recurrence,
      weekdays: recurrence === 'daily' ? [...weekdays].sort((a, b) => a - b) : recurrence === 'weekly' ? [weekday] : [],
      daysOfMonth: recurrence === 'monthly' ? [day] : [],
      dueTime: dueTime || null,
      kind: 'check',
    }
    setSaving(true)
    setError(null)
    try {
      if (template) await hrApi.updateTemplate(template.id, body)
      else await hrApi.createTemplate(body)
      onSaved()
    } catch (e) {
      setError(hrErrorMessage(e))
      hapticNotify('error')
      setSaving(false)
    }
  }

  const chip = (active) =>
    'rounded-xl px-3 py-2 text-[14px] font-semibold transition ' + (active ? 'bg-brand text-brand-ink' : 'bg-slate-100 text-slate-600')

  return (
    <BottomSheet
      title={template ? 'Takroriy topshiriq' : 'Yangi takroriy topshiriq'}
      busy={saving}
      onClose={onClose}
      footer={
        <div className="flex gap-2">
          {template && (
            <Btn variant="danger" onClick={() => (confirmDelete ? void onDelete(template) : setConfirmDelete(true))}>
              {confirmDelete ? 'Rostdan o‘chirish' : 'O‘chirish'}
            </Btn>
          )}
          <Btn variant="primary" grow loading={saving} onClick={() => void submit()}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Btn>
        </div>
      }
    >
      {error && (
        <Notice tone="danger" onDismiss={() => setError(null)} className="mt-1">
          {error}
        </Notice>
      )}
      <FieldLabel htmlFor="hr-rec-title">Sarlavha</FieldLabel>
      <input
        id="hr-rec-title"
        maxLength={200}
        placeholder="Masalan: Haftalik reja yozish"
        value={title}
        disabled={saving}
        onChange={(e) => setTitle(e.target.value)}
        className={INPUT_CLASS}
      />
      <FieldLabel htmlFor="hr-rec-desc">Tavsif</FieldLabel>
      <textarea
        id="hr-rec-desc"
        rows={2}
        maxLength={2000}
        value={description}
        disabled={saving}
        onChange={(e) => setDescription(e.target.value)}
        className={INPUT_CLASS + ' resize-none'}
      />

      <FieldLabel>Takrorlanish</FieldLabel>
      <div className="flex flex-wrap gap-2">
        {RECURRENCES.map(([value, label]) => (
          <button key={value} type="button" className={chip(recurrence === value)} onClick={() => setRecurrence(value)}>
            {label}
          </button>
        ))}
      </div>

      {recurrence === 'daily' && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {WEEKDAYS.map(([d, label]) => (
            <button
              key={d}
              type="button"
              aria-pressed={weekdays.includes(d)}
              className={chip(weekdays.includes(d))}
              onClick={() => setWeekdays((list) => (list.includes(d) ? list.filter((x) => x !== d) : [...list, d]))}
            >
              {label}
            </button>
          ))}
        </div>
      )}
      {recurrence === 'weekly' && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {WEEKDAYS.map(([d, label]) => (
            <button key={d} type="button" aria-pressed={weekday === d} className={chip(weekday === d)} onClick={() => setWeekday(d)}>
              {label}
            </button>
          ))}
        </div>
      )}
      {recurrence === 'monthly' && (
        <>
          <FieldLabel htmlFor="hr-rec-day">Oyning sanasi</FieldLabel>
          <input
            id="hr-rec-day"
            inputMode="numeric"
            placeholder="15"
            value={monthDay}
            onChange={(e) => setMonthDay(e.target.value.replace(/\D/g, '').slice(0, 2))}
            className={INPUT_CLASS}
          />
          <Hint>29–31 bo‘lmagan oylarda oyning oxirgi kuni olinadi.</Hint>
        </>
      )}

      <FieldLabel htmlFor="hr-rec-time">Muddat (soat)</FieldLabel>
      <input
        id="hr-rec-time"
        type="time"
        step={60}
        value={dueTime}
        onChange={(e) => setDueTime(e.target.value)}
        className={INPUT_CLASS + ' min-h-[44px]'}
      />
      <Hint>Bo‘sh qoldirilsa — kun oxiri (23:59).</Hint>
    </BottomSheet>
  )
}
