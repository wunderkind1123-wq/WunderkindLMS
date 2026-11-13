/**
 * ISH REJA — o'qituvchi o'z sinf/guruhida o'tgan mavzusiga "o'tildi" belgisini qo'yadi
 * (mijoz, 2026-11-06; docs/modules/curriculum-monitoring.md §1). Rejani o'quv bo'limi yuklaydi;
 * ro'yxatni server cheklaydi (dars jadvalida shu o'qituvchining shu fandan darsi bor sinf/guruhlar).
 */
import { useState } from 'react'
import { BookOpenCheck, Check, ChevronLeft, ChevronRight } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const request = (promise) =>
  promise.catch((e) => {
    throw new Error(e?.message || "Ish rejani olib bo'lmadi")
  })

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

export function CurriculumScreen({ onBack }) {
  const [open, setOpen] = useState(null)
  const list = useAsync(() => request(teacherApi.curriculum()), [])

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

  return (
    <Screen>
      <Hero title="Ish reja" subtitle="Darsda o'tgan mavzuingizni belgilang" right={<BackPill label="Bugun" onClick={onBack} />} />
      <AsyncBlock query={list} loadingLabel="Ish rejalar yuklanmoqda…">
        {(items) =>
          items.length === 0 ? (
            <Card>
              <EmptyState icon={<BookOpenCheck className="h-9 w-9" />} title="Ish reja yo'q"
                note="O'quv bo'limi siz dars beradigan fan uchun ish reja yuklaganda bu yerda chiqadi." />
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
    </Screen>
  )
}

function PlanScreen({ item, onBack }) {
  const q = useAsync(() => request(teacherApi.curriculumPlan(item.planId)), [])
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)
  const today = new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Tashkent' })

  const toggle = async (topic, passed) => {
    setBusy(topic.id)
    setError(null)
    try {
      await teacherApi.markTopic({ topicId: topic.id, ownerKind: item.ownerKind, ownerId: item.ownerId, passed: !passed })
      hapticNotify('success')
      q.reload()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Belgilab bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  return (
    <Screen>
      <AsyncBlock query={q} loadingLabel="Mavzular yuklanmoqda…">
        {(plan) => {
          const topics = plan.topics
          const passed = topics.filter((t) => t.marks.some((m) => m.ownerId === item.ownerId)).length
          return (
            <>
              <Hero title={`${item.ownerName} · ${item.subjectName}`} subtitle={plan.title} right={<BackPill label="Orqaga" onClick={onBack} />}>
                <HeroProgress value={passed} max={Math.max(topics.length, 1)} lead={`${passed}/${topics.length}`}
                  note={['mavzu o\'tildi', paceText(item), forecastText(item)].filter(Boolean).join(' · ')} />
              </Hero>
              {error && <p className="mx-4 mt-3 rounded-xl bg-red-50 px-3 py-2 text-[13px] text-red-600">{error}</p>}
              <Card title="Mavzular">
                {topics.map((t) => {
                  const mark = t.marks.find((m) => m.ownerId === item.ownerId)
                  const overdue = !mark && t.plannedOn != null && t.plannedOn < today
                  return (
                    <button
                      key={t.id}
                      type="button"
                      disabled={busy === t.id}
                      onClick={() => void toggle(t, !!mark)}
                      className="flex w-full items-start gap-3 border-t border-slate-100 px-4 py-3 text-left first:border-t-0 disabled:opacity-50"
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
                  )
                })}
              </Card>
              <div className="h-4" />
            </>
          )
        }}
      </AsyncBlock>
    </Screen>
  )
}
