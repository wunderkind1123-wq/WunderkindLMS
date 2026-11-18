/**
 * ISH REJA — o'qituvchi o'z sinf/guruhida o'tgan mavzusiga "o'tildi" belgisini qo'yadi
 * (mijoz, 2026-11-06; docs/modules/curriculum-monitoring.md §1). Ro'yxatni server cheklaydi
 * (dars jadvalida shu o'qituvchining shu fandan darsi bor sinf/guruhlar).
 *
 * 2026-11-18 (§1.2): ustoz o'z rejasini O'ZI yuklaydi (namuna Excel bot chatiga keladi), har mavzuda
 * chorak, hafta, mavzu va sana shart. O'z rejasida mavzuni o'zgartiradi, qo'shadi, o'chiradi va
 * "Dars bo'lmadi" — shu mavzu va keyingilari bitta darsga suriladi. Umumiy reja — faqat ko'rish.
 */
import { useState } from 'react'
import { BookOpenCheck, CalendarX, Check, ChevronLeft, ChevronRight, FileDown, MoreHorizontal, Pencil, Plus, Trash2, Upload } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, inTelegram } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const request = (promise) =>
  promise.catch((e) => {
    throw new Error(e?.message || "Ish rejani olib bo'lmadi")
  })

const FIELD =
  'w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white'
const PRIMARY = 'w-full rounded-2xl bg-brand py-3 text-[16px] font-bold text-brand-ink disabled:opacity-50'
const SECONDARY = 'w-full rounded-2xl bg-slate-100 py-3 text-[15px] font-semibold text-slate-700 disabled:opacity-50'

/** "Bugun" dagi karta. */
export function CurriculumCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <BookOpenCheck className="h-5 w-5" />
          </span>
        }
        title="Ish reja"
        subtitle="O'tilgan mavzularni belgilash"
        right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
      />
    </Card>
  )
}

const ddmm = (iso) => iso.split('-').reverse().join('.')

/** Rejadan orqada/oldinda — o'quv bo'limi hisobotidagi bilan bir xil hisob (server beradi). */
function paceText(item) {
  if (item.delta == null) return null
  if (item.delta < 0) return `${-item.delta} mavzu orqada`
  if (item.delta > 0) return `${item.delta} mavzu oldinda`
  return "Reja bo'yicha"
}

function forecastText(item) {
  if (!item.forecastFinish) return null
  return `Tugash: ${ddmm(item.forecastFinish)}${item.lateWarning ? ' — yil oxiriga ulgurmaydi' : ''}`
}

function BackPill({ label, onClick }) {
  return (
    <button type="button" onClick={onClick} aria-label="Orqaga"
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand">
      <ChevronLeft className="h-4 w-4" />
      {label}
    </button>
  )
}

function Alert({ tone = 'error', children }) {
  const cls = tone === 'error' ? 'bg-red-50 text-red-600' : tone === 'warn' ? 'bg-amber-50 text-amber-800' : 'bg-emerald-50 text-emerald-700'
  return <div className={`mx-4 mt-3 rounded-xl px-3 py-2 text-[13px] ${cls}`}>{children}</div>
}

export function CurriculumScreen({ onBack }) {
  const [open, setOpen] = useState(null)
  const [uploading, setUploading] = useState(false)
  const [note, setNote] = useState(null)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const list = useAsync(() => request(teacherApi.curriculum()), [])
  const keys = useAsync(() => teacherApi.curriculumKeys().catch(() => []), [])

  if (uploading) {
    return (
      <UploadScreen
        keys={keys.data ?? []}
        onBack={() => setUploading(false)}
        onSaved={() => {
          setUploading(false)
          setNote('Ish rejangiz saqlandi.')
          list.reload()
          keys.reload()
        }}
      />
    )
  }

  if (open) {
    return (
      <PlanScreen
        key={`${open.planId}|${open.ownerId}`}
        item={open}
        onBack={() => {
          setOpen(null)
          list.reload()
        }}
      />
    )
  }

  const template = async () => {
    setBusy(true)
    setError(null)
    setNote(null)
    try {
      await teacherApi.curriculumTemplateToTelegram()
      hapticNotify('success')
      setNote("Namuna Excel bot chatingizga yuborildi — to'ldirib, shu yerdan yuklang.")
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Namunani yuborib bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  const noKeys = keys.data && keys.data.length === 0

  return (
    <Screen>
      <Hero title="Ish reja" subtitle="Darsda o'tgan mavzuingizni belgilang" right={<BackPill label="Bugun" onClick={onBack} />} />
      {error && <Alert>{error}</Alert>}
      {note && <Alert tone="ok">{note}</Alert>}
      <Card title="O'z ish rejam">
        <Row
          onClick={busy ? undefined : () => void template()}
          lead={<span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-600"><FileDown className="h-5 w-5" /></span>}
          title="Namunani olish"
          subtitle={inTelegram ? 'Excel namuna bot chatingizga keladi' : "Telegram ichida oching — namuna bot chatiga yuboriladi"}
        />
        <Row
          onClick={!keys.data || noKeys ? undefined : () => { haptic('light'); setUploading(true) }}
          lead={<span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink"><Upload className="h-5 w-5" /></span>}
          title="Ish rejamni yuklash"
          subtitle={noKeys ? "Dars jadvalida sizga dars biriktirilmagan" : 'Excel (.xlsx) yoki Word (.docx): chorak, hafta, mavzu, sana'}
          right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
        />
      </Card>
      <AsyncBlock query={list} loadingLabel="Ish rejalar yuklanmoqda…">
        {(items) =>
          items.length === 0 ? (
            <Card>
              <EmptyState icon={<BookOpenCheck className="h-9 w-9" />} title="Ish reja yo'q"
                note="Namunani oling, to'ldiring va «Ish rejamni yuklash» orqali yuklang." />
            </Card>
          ) : (
            <Card title="Sinflar va fanlar">
              {items.map((item) => (
                <Row
                  key={`${item.planId}|${item.ownerId}`}
                  onClick={() => {
                    haptic('light')
                    setOpen(item)
                  }}
                  title={`${item.ownerName} · ${item.subjectName}`}
                  subtitle={
                    <>
                      {paceText(item) && (
                        <span className={'block font-medium ' + (item.delta < 0 ? 'text-red-600' : item.delta > 0 ? 'text-emerald-600' : 'text-slate-500')}>
                          {paceText(item)}
                          {item.lateWarning && <span className="font-normal"> · {forecastText(item)}</span>}
                        </span>
                      )}
                      {item.nextTopicTitle ? `Keyingi: ${item.nextTopicTitle}` : 'Hamma mavzu o‘tilgan'}
                    </>
                  }
                  right={
                    <span className="flex items-center gap-1.5">
                      <Badge tone={item.passedCount >= item.topicCount ? 'success' : item.delta < 0 ? 'danger' : 'neutral'}>
                        {item.passedCount}/{item.topicCount}
                      </Badge>
                      <ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />
                    </span>
                  }
                />
              ))}
            </Card>
          )
        }
      </AsyncBlock>
      <div className="h-4" />
    </Screen>
  )
}

/** O'z rejasini yuklash: avval tekshiruv (xatolar qator raqami bilan), keyin saqlash. */
function UploadScreen({ keys, onBack, onSaved }) {
  const [keyIdx, setKeyIdx] = useState(keys.length === 1 ? '0' : '')
  const [file, setFile] = useState(null)
  const [result, setResult] = useState(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const key = keyIdx === '' ? null : keys[Number(keyIdx)]
  const ok = result && result.errors.length === 0

  const run = async (dryRun) => {
    if (!key || !file) return
    setBusy(true)
    setError(null)
    try {
      const r = await teacherApi.importCurriculum({ file, subjectId: key.subjectId, grade: key.grade, groupId: key.groupId }, dryRun)
      setResult(r)
      if (r.errors.length > 0) hapticNotify('error')
      else if (!dryRun) {
        hapticNotify('success')
        onSaved()
      }
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Faylni yuklab bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero title="Ish rejamni yuklash" subtitle="Namuna bo'yicha to'ldirilgan fayl" right={<BackPill label="Orqaga" onClick={onBack} />} />
      <Card>
        <div className="space-y-3 px-4 py-3">
          <p className="text-[13px] text-slate-500">
            Har bir mavzuda <b>Chorak</b>, <b>Hafta</b>, <b>Mavzu</b> va <b>Sana</b> bo'lishi shart, hafta sanaga mos kelsin.
          </p>
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Fan va sinf</span>
            <select value={keyIdx} onChange={(e) => { setKeyIdx(e.target.value); setResult(null) }} className={FIELD + ' h-12'}>
              <option value="">— Tanlang —</option>
              {keys.map((k, i) => (
                <option key={k.label + i} value={String(i)}>{k.label}{k.planId ? ' — reja bor' : ''}</option>
              ))}
            </select>
          </label>
          {key?.planId && (
            <p className="rounded-xl bg-amber-50 px-3 py-2 text-[13px] text-amber-800">
              Bu fan va sinf uchun rejangiz bor — yangi fayl uni almashtiradi. «O'tildi» belgilari mavzu raqami (№) bo'yicha saqlanadi.
            </p>
          )}
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Fayl (.xlsx yoki .docx)</span>
            <input type="file" accept=".xlsx,.docx" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setResult(null) }} className="block w-full text-[14px]" />
          </label>
          <div className="flex gap-2">
            <button type="button" className={SECONDARY} disabled={!key || !file || busy} onClick={() => void run(true)}>Tekshirish</button>
            <button type="button" className={PRIMARY} disabled={!key || !file || busy || (result && !ok)} onClick={() => void run(false)}>
              {busy ? 'Kuting…' : 'Saqlash'}
            </button>
          </div>
        </div>
      </Card>
      {error && <Alert>{error}</Alert>}
      {result && result.errors.length > 0 && (
        <Alert>
          <p className="font-semibold">Faylda kamchiliklar bor — tuzatib qayta yuklang:</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-4">
            {result.errors.slice(0, 40).map((e) => <li key={e}>{e}</li>)}
          </ul>
        </Alert>
      )}
      {result && result.warnings?.length > 0 && (
        <Alert tone="warn">
          <p className="font-semibold">Diqqat (saqlashga to'sqinlik qilmaydi):</p>
          <ul className="mt-1 list-disc space-y-0.5 pl-4">
            {result.warnings.slice(0, 20).map((w) => <li key={w}>{w}</li>)}
          </ul>
        </Alert>
      )}
      {ok && (
        <Card title={`✓ ${result.topicCount} ta mavzu, ${result.totalHours} soat`}>
          {result.preview.slice(0, 60).map((t) => (
            <div key={t.order} className="border-t border-slate-100 px-4 py-2 text-[13px] first:border-t-0">
              <span className="font-medium">{t.order}. {t.title}</span>
              <span className="block text-slate-400">
                {t.quarter}-chorak, {t.week}-hafta{t.plannedOn ? ` · ${ddmm(t.plannedOn)}` : ''}{t.hours > 1 ? ` · ${t.hours} soat` : ''}
              </span>
            </div>
          ))}
        </Card>
      )}
      <div className="h-4" />
    </Screen>
  )
}

function PlanScreen({ item, onBack }) {
  const q = useAsync(() => request(teacherApi.curriculumPlan(item.planId)), [])
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)
  const [menu, setMenu] = useState(null) // topic id whose actions are open
  const [editing, setEditing] = useState(null) // { topic, after }
  const today = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Tashkent' })

  const act = async (key, fn, okHaptic = true) => {
    setBusy(key)
    setError(null)
    try {
      await fn()
      if (okHaptic) hapticNotify('success')
      setMenu(null)
      q.reload()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || 'Xatolik yuz berdi')
    } finally {
      setBusy(null)
    }
  }

  const toggle = (topic, passed) =>
    act(topic.id, () => teacherApi.markTopic({ topicId: topic.id, ownerKind: item.ownerKind, ownerId: item.ownerId, passed: !passed }))

  const shift = (topic) => {
    if (!window.confirm(`«${topic.title}» darsi bo'lmadimi? Shu mavzu va keyingilari bitta darsga suriladi.`)) return
    void act(`shift|${topic.id}`, () => teacherApi.shiftTopics(topic.id))
  }

  const remove = (topic) => {
    if (!window.confirm(`${topic.order}-mavzu «${topic.title}» o'chirilsinmi?`)) return
    void act(`del|${topic.id}`, () => teacherApi.deleteTopic(topic.id))
  }

  if (editing) {
    return (
      <TopicForm
        planId={item.planId}
        topic={editing.topic}
        after={editing.after}
        onBack={() => setEditing(null)}
        onSaved={() => {
          setEditing(null)
          q.reload()
        }}
      />
    )
  }

  return (
    <Screen>
      <AsyncBlock query={q} loadingLabel="Mavzular yuklanmoqda…">
        {(plan) => {
          const topics = plan.topics
          const own = !!plan.teacherId
          const owner = plan.owners.find((o) => o.id === item.ownerId)
          const pace = owner ? paceText(owner) : paceText(item)
          const passed = topics.filter((t) => t.marks.some((m) => m.ownerId === item.ownerId)).length
          return (
            <>
              <Hero title={`${item.ownerName} · ${item.subjectName}`} subtitle={plan.title} right={<BackPill label="Orqaga" onClick={onBack} />}>
                <HeroProgress value={passed} max={Math.max(topics.length, 1)} lead={`${passed}/${topics.length}`}
                  note={['mavzu o\'tildi', pace, forecastText(owner ?? item)].filter(Boolean).join(' · ')} />
              </Hero>
              {error && <Alert>{error}</Alert>}
              {!own && (
                <Alert tone="warn">Bu — o'quv bo'limining umumiy rejasi. O'zgartirish uchun «Ish rejamni yuklash» orqali o'z rejangizni yuklang.</Alert>
              )}
              {own && (
                <p className="mx-4 mt-3 text-[12px] text-slate-400">
                  Doirani bosing — «o'tildi». «⋯» — o'zgartirish, dars bo'lmadi (surish), yangi mavzu yoki o'chirish.
                </p>
              )}
              <Card title="Mavzular">
                {topics.map((t) => {
                  const mark = t.marks.find((m) => m.ownerId === item.ownerId)
                  const overdue = !mark && t.plannedOn != null && t.plannedOn < today
                  return (
                    <div key={t.id} className="border-t border-slate-100 first:border-t-0">
                      <div className="flex items-start">
                        <button
                          type="button"
                          disabled={busy === t.id}
                          onClick={() => void toggle(t, !!mark)}
                          className="flex min-w-0 flex-1 items-start gap-3 px-4 py-3 text-left disabled:opacity-50"
                        >
                          <span className={'mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full border-2 ' +
                            (mark ? 'border-emerald-500 bg-emerald-500 text-white' : 'border-slate-300')}>
                            {mark && <Check className="h-4 w-4" />}
                          </span>
                          <span className="min-w-0 flex-1">
                            <span className={'block text-[15px] leading-snug ' + (mark ? 'text-slate-500' : 'font-medium')}>
                              {t.order}. {t.title}
                            </span>
                            <span className="text-[12px] text-slate-400">
                              {t.plannedOn
                                ? <span className={overdue ? 'font-medium text-red-600' : ''}>reja {t.plannedExact ? '' : '~'}{ddmm(t.plannedOn)}</span>
                                : <>{t.quarter ? `${t.quarter}-chorak` : ''}{t.week ? `, ${t.week}-hafta` : ''}</>}
                              {t.hours > 1 ? ` · ${t.hours} soat` : ''}
                              {mark ? ` · o'tildi ${ddmm(mark.passedOn)}` : ''}
                            </span>
                          </span>
                        </button>
                        {own && (
                          <button type="button" aria-label="Amallar" onClick={() => { haptic('light'); setMenu(menu === t.id ? null : t.id) }}
                            className="mr-2 mt-2 flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-slate-400 active:bg-slate-100">
                            <MoreHorizontal className="h-5 w-5" />
                          </button>
                        )}
                      </div>
                      {own && menu === t.id && (
                        <div className="grid grid-cols-2 gap-2 px-4 pb-3">
                          <ActionButton icon={<CalendarX className="h-4 w-4" />} label="Dars bo'lmadi" disabled={busy === `shift|${t.id}`} onClick={() => shift(t)} />
                          <ActionButton icon={<Pencil className="h-4 w-4" />} label="O'zgartirish" onClick={() => setEditing({ topic: t, after: null })} />
                          <ActionButton icon={<Plus className="h-4 w-4" />} label="Keyiniga qo'shish" onClick={() => setEditing({ topic: null, after: t.order })} />
                          <ActionButton icon={<Trash2 className="h-4 w-4" />} label="O'chirish" danger disabled={busy === `del|${t.id}`} onClick={() => remove(t)} />
                        </div>
                      )}
                    </div>
                  )
                })}
              </Card>
              {own && (
                <div className="px-4 pt-3">
                  <button type="button" className={SECONDARY} onClick={() => setEditing({ topic: null, after: topics.length ? topics[topics.length - 1].order : 0 })}>
                    + Mavzu qo'shish
                  </button>
                </div>
              )}
              <div className="h-4" />
            </>
          )
        }}
      </AsyncBlock>
    </Screen>
  )
}

function ActionButton({ icon, label, danger = false, disabled = false, onClick }) {
  return (
    <button type="button" disabled={disabled} onClick={onClick}
      className={'flex items-center justify-center gap-1.5 rounded-xl py-2.5 text-[13px] font-semibold disabled:opacity-50 ' +
        (danger ? 'bg-red-50 text-red-600' : 'bg-slate-100 text-slate-700')}>
      {icon}
      {label}
    </button>
  )
}

/** Mavzu qo'shish / o'zgartirish. Sana majburiy — chorak va hafta undan hisoblanadi. */
function TopicForm({ planId, topic, after, onBack, onSaved }) {
  const [title, setTitle] = useState(topic?.title ?? '')
  const [plannedOn, setPlannedOn] = useState(topic?.plannedExact ? topic.plannedOn : '')
  const [hours, setHours] = useState(String(topic?.hours ?? 1))
  const [section, setSection] = useState(topic?.section ?? '')
  const [goal, setGoal] = useState(topic?.goal ?? '')
  const [homework, setHomework] = useState(topic?.homework ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const ready = title.trim() && plannedOn && Number(hours) >= 1

  const save = async () => {
    if (!ready) return
    setBusy(true)
    setError(null)
    try {
      await teacherApi.saveTopic(planId, topic?.id ?? null, {
        title: title.trim(),
        plannedOn,
        hours: Number(hours),
        section: section.trim() || null,
        goal: goal.trim() || null,
        homework: homework.trim() || null,
        afterOrder: topic ? null : after,
      })
      hapticNotify('success')
      onSaved()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Saqlab bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero
        title={topic ? `${topic.order}-mavzu` : 'Yangi mavzu'}
        subtitle={topic ? "O'zgartirish" : after ? `${after}-mavzudan keyin` : 'Rejaning boshiga'}
        right={<BackPill label="Orqaga" onClick={onBack} />}
      />
      {error && <Alert>{error}</Alert>}
      <Card>
        <div className="space-y-3 px-4 py-3">
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Mavzu</span>
            <input value={title} maxLength={500} onChange={(e) => setTitle(e.target.value)} className={FIELD} />
          </label>
          <div className="grid grid-cols-2 gap-2">
            <label className="block space-y-1">
              <span className="text-[13px] font-medium text-slate-600">Dars sanasi</span>
              <input type="date" value={plannedOn} onChange={(e) => setPlannedOn(e.target.value)} className={FIELD + ' min-h-[44px]'} />
            </label>
            <label className="block space-y-1">
              <span className="text-[13px] font-medium text-slate-600">Soat</span>
              <input type="number" inputMode="numeric" min={1} max={50} value={hours} onChange={(e) => setHours(e.target.value)} className={FIELD} />
            </label>
          </div>
          <p className="text-[12px] text-slate-400">Chorak va hafta sanadan avtomatik hisoblanadi.</p>
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Bo'lim (ixtiyoriy)</span>
            <input value={section} maxLength={300} onChange={(e) => setSection(e.target.value)} className={FIELD} />
          </label>
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Maqsad (ixtiyoriy)</span>
            <textarea rows={2} value={goal} maxLength={1000} onChange={(e) => setGoal(e.target.value)} className={FIELD} />
          </label>
          <label className="block space-y-1">
            <span className="text-[13px] font-medium text-slate-600">Uy vazifasi (ixtiyoriy)</span>
            <input value={homework} maxLength={500} onChange={(e) => setHomework(e.target.value)} className={FIELD} />
          </label>
          <button type="button" className={PRIMARY} disabled={!ready || busy} onClick={() => void save()}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </Card>
      <div className="h-4" />
    </Screen>
  )
}
