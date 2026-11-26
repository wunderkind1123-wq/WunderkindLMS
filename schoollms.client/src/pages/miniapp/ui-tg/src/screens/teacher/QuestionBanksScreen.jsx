/**
 * TEST SAVOLLARIM — ustoz oylik test savollarini Word'dan yuklaydi (curriculum-monitoring.md §2.2).
 * Web'dagi `/teacher/savollar` bilan bir xil API (`/api/teacher/question-banks`): banklar faqat ustoz
 * jadvalda dars beradigan fan × sinf/yo'nalish bo'yicha. Bu yerda — bank yaratish, Word namunasini
 * bot chatiga olish, .docx yuklash → oldindan ko'rish (to'g'ri javobni tuzatish, keraksizini olib
 * tashlash) → saqlash, savolni o'chirish. Savol/variant rasmini tahrirlash — kompyuterdagi LMS'da.
 */
import { useState } from 'react'
import { ChevronLeft, ChevronRight, FileDown, FileQuestion, Plus, Trash2, Upload } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, inTelegram } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const LETTERS = ['A', 'B', 'C', 'D', 'E', 'F']
const FIELD =
  'w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white'
const PRIMARY = 'w-full rounded-2xl bg-brand py-3 text-[16px] font-bold text-brand-ink disabled:opacity-50'

const request = (promise, fallback) =>
  promise.catch((e) => {
    throw new Error(e?.message || fallback)
  })

/** Word'dagi butunlay qalin qator `**…**` bo'lib keladi — telefonda oddiy matn. */
const plain = (s) => (s || '').replace(/\*\*/g, '')

const bankLevel = (b) => (b.groupName ? b.groupName : b.grade != null ? `${b.grade}-sinf` : '')

/** "Bugun" dagi karta. */
export function QuestionBanksCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <FileQuestion className="h-5 w-5" />
          </span>
        }
        title="Test savollarim"
        subtitle="Oylik test savollarini Word'dan yuklash"
        right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
      />
    </Card>
  )
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

export function QuestionBanksScreen({ onBack }) {
  const [openId, setOpenId] = useState(null)
  const [creating, setCreating] = useState(false)
  const [note, setNote] = useState(null)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const list = useAsync(() => request(teacherApi.questionBanks(), "Banklarni olib bo'lmadi"), [])

  if (creating) {
    return (
      <CreateBankScreen
        onBack={() => setCreating(false)}
        onCreated={(id) => {
          setCreating(false)
          list.reload()
          setOpenId(id)
        }}
      />
    )
  }

  if (openId) {
    return (
      <BankScreen
        key={openId}
        bankId={openId}
        onBack={() => {
          setOpenId(null)
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
      await teacherApi.questionTemplateToTelegram()
      hapticNotify('success')
      setNote("Word namunasi bot chatingizga yuborildi — shu ko'rinishda yozib, bankka yuklang.")
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Namunani yuborib bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero title="Test savollarim" subtitle="Oylik test savollari — testni o'quv bo'limi tuzadi" right={<BackPill label="Bugun" onClick={onBack} />} />
      {error && <Alert>{error}</Alert>}
      {note && <Alert tone="ok">{note}</Alert>}
      <Card>
        <Row
          onClick={busy ? undefined : () => void template()}
          lead={<span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-600"><FileDown className="h-5 w-5" /></span>}
          title="Word namunasini olish"
          subtitle={inTelegram ? 'Namuna .docx bot chatingizga keladi' : 'Telegram ichida oching — namuna bot chatiga yuboriladi'}
        />
        <Row
          onClick={() => { haptic('light'); setCreating(true) }}
          lead={<span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink"><Plus className="h-5 w-5" /></span>}
          title="Yangi bank"
          subtitle="Fan va sinf uchun savollar to'plami"
          right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
        />
      </Card>
      <AsyncBlock query={list} loadingLabel="Banklar yuklanmoqda…">
        {(banks) =>
          banks.length === 0 ? (
            <Card>
              <EmptyState icon={<FileQuestion className="h-9 w-9" />} title="Bank yo'q"
                note="«Yangi bank» bilan fan va sinfni tanlang, keyin Word fayldagi testni yuklang." />
            </Card>
          ) : (
            <Card title="Banklarim">
              {banks.map((b) => (
                <Row
                  key={b.id}
                  onClick={() => { haptic('light'); setOpenId(b.id) }}
                  title={b.title}
                  subtitle={`${b.subjectName} · ${bankLevel(b)}`}
                  right={
                    <span className="flex items-center gap-1.5">
                      <Badge tone={b.questionCount > 0 ? 'success' : 'neutral'}>{b.questionCount} ta</Badge>
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

function CreateBankScreen({ onBack, onCreated }) {
  const opts = useAsync(() => request(teacherApi.questionBankOptions(), "Fanlarni olib bo'lmadi"), [])
  const [keyIdx, setKeyIdx] = useState('')
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const keys = opts.data?.keys ?? []
  const key = keyIdx === '' ? null : keys[Number(keyIdx)]

  const save = async () => {
    if (!key) return
    setBusy(true)
    setError(null)
    try {
      const { id } = await teacherApi.createQuestionBank({
        title: title.trim() || `${key.label} — oylik test`,
        subjectId: key.subjectId,
        grade: key.groupId ? null : key.grade,
        groupId: key.groupId,
      })
      hapticNotify('success')
      onCreated(id)
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Bankni yaratib bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero title="Yangi bank" subtitle="Bir fan va sinf uchun savollar" right={<BackPill label="Orqaga" onClick={onBack} />} />
      <AsyncBlock query={opts} loadingLabel="Fanlar yuklanmoqda…">
        {() =>
          keys.length === 0 ? (
            <Card>
              <EmptyState icon={<FileQuestion className="h-9 w-9" />} title="Fan topilmadi"
                note="Dars jadvalida sizga dars biriktirilmagan. O'quv bo'limiga murojaat qiling." />
            </Card>
          ) : (
            <Card>
              <div className="space-y-3 px-4 py-3">
                <label className="block space-y-1">
                  <span className="text-[13px] font-medium text-slate-600">Fan va sinf</span>
                  <select value={keyIdx} onChange={(e) => setKeyIdx(e.target.value)} className={FIELD + ' h-12'}>
                    <option value="">— Tanlang —</option>
                    {keys.map((k, i) => (
                      <option key={k.label + i} value={String(i)}>{k.label}</option>
                    ))}
                  </select>
                </label>
                <label className="block space-y-1">
                  <span className="text-[13px] font-medium text-slate-600">Nomi (ixtiyoriy)</span>
                  <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200}
                    placeholder={key ? `${key.label} — oylik test` : 'Masalan: 5-sinf tarix — noyabr'} className={FIELD} />
                </label>
                <button type="button" className={PRIMARY} disabled={!key || busy} onClick={() => void save()}>
                  {busy ? 'Kuting…' : 'Yaratish'}
                </button>
              </div>
            </Card>
          )
        }
      </AsyncBlock>
      {error && <Alert>{error}</Alert>}
      <div className="h-4" />
    </Screen>
  )
}

/** Savol ko'rinishi: matn, rasm, variantlar (to'g'risi yashil). `onPick` bo'lsa — variant bosilib to'g'ri qilinadi. */
function QuestionView({ n, q, onPick, onDelete, deleting }) {
  return (
    <div className="border-t border-slate-100 px-4 py-3 first:border-t-0">
      <div className="flex items-start gap-2">
        <p className="min-w-0 flex-1 whitespace-pre-line text-[14px] font-medium text-slate-800">{n}. {plain(q.text)}</p>
        {onDelete && (
          <button type="button" onClick={onDelete} disabled={deleting} aria-label="O'chirish"
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-slate-400 active:bg-red-50 active:text-red-500 disabled:opacity-40">
            <Trash2 className="h-4 w-4" />
          </button>
        )}
      </div>
      {q.imageUrl && <img src={q.imageUrl} alt="" className="mt-2 max-h-48 rounded-lg border border-slate-100" />}
      <div className="mt-2 space-y-1">
        {q.options.map((o, i) => {
          const right = i === q.correctIndex
          const img = q.optionImages?.[i]
          return (
            <button key={i} type="button" disabled={!onPick} onClick={onPick ? () => { haptic('light'); onPick(i) } : undefined}
              className={'flex w-full items-start gap-2 rounded-lg px-2 py-1.5 text-left text-[14px] ' +
                (right ? 'bg-emerald-50 font-semibold text-emerald-700' : 'text-slate-600 ' + (onPick ? 'active:bg-slate-100' : ''))}>
              <span className="shrink-0">{LETTERS[i]})</span>
              <span className="min-w-0 flex-1">
                {plain(o)}
                {img && <img src={img} alt="" className="mt-1 max-h-24 rounded border border-slate-100" />}
              </span>
              {right && <span className="shrink-0">✓</span>}
            </button>
          )
        })}
      </div>
    </div>
  )
}

function BankScreen({ bankId, onBack }) {
  const q = useAsync(() => request(teacherApi.questionBank(bankId), "Bankni olib bo'lmadi"), [])
  const [preview, setPreview] = useState(null) // { rows, problems }
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)
  const [note, setNote] = useState(null)

  const read = async (file) => {
    if (!file) return
    setBusy('read')
    setError(null)
    setNote(null)
    try {
      const p = await teacherApi.previewBankImport(bankId, file)
      setPreview({ rows: p.questions, problems: p.problems })
      if (p.questions.length === 0) hapticNotify('error')
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Faylni o'qib bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  const save = async () => {
    setBusy('save')
    setError(null)
    try {
      const { added } = await teacherApi.saveBankQuestions(bankId, preview.rows)
      hapticNotify('success')
      setPreview(null)
      setNote(`${added} ta savol bankka qo'shildi.`)
      q.reload()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Saqlab bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  const remove = async (question, n) => {
    if (!window.confirm(`${n}-savol o'chirilsinmi?`)) return
    setBusy(question.id)
    setError(null)
    try {
      await teacherApi.deleteBankQuestion(bankId, question.id)
      hapticNotify('success')
      q.reload()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "O'chirib bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  const bank = q.data?.bank

  if (preview) {
    const { rows, problems } = preview
    const setRows = (next) => setPreview({ ...preview, rows: next })
    // Pastki menyu ustiga chiqmasin — sticky emas; uzun ro'yxatda tepada ham.
    const saveButton = rows.length > 0 && (
      <div className="px-4 pt-3">
        <button type="button" className={PRIMARY} disabled={busy != null} onClick={() => void save()}>
          {busy === 'save' ? 'Saqlanmoqda…' : `${rows.length} ta savolni bankka qo'shish`}
        </button>
      </div>
    )
    return (
      <Screen>
        <Hero title="Tekshirish" subtitle="Hali saqlanmagan — to'g'ri javobni tekshiring" right={<BackPill label="Bekor" onClick={() => setPreview(null)} />} />
        {problems.length > 0 && (
          <Alert tone="warn">
            <p className="font-semibold">O'qilmagan savollar — Word'da tuzatib qayta yuklang:</p>
            <ul className="mt-1 list-disc space-y-0.5 pl-4">
              {problems.slice(0, 40).map((p) => <li key={p}>{p}</li>)}
            </ul>
          </Alert>
        )}
        {error && <Alert>{error}</Alert>}
        {rows.length > 3 && saveButton}
        {rows.length === 0 ? (
          <Card>
            <EmptyState icon={<FileQuestion className="h-9 w-9" />} title="Savol topilmadi"
              note="Savollar «1.» bilan, variantlar «A)» bilan boshlansin. Word namunasiga qarang." />
          </Card>
        ) : (
          <Card title={`${rows.length} ta savol · variantni bossangiz to'g'ri javob bo'ladi`}>
            {rows.map((r, i) => (
              <QuestionView
                key={i}
                n={i + 1}
                q={r}
                onPick={(oi) => setRows(rows.map((x, k) => (k === i ? { ...x, correctIndex: oi } : x)))}
                onDelete={() => setRows(rows.filter((_, k) => k !== i))}
              />
            ))}
          </Card>
        )}
        {saveButton}
        <div className="h-4" />
      </Screen>
    )
  }

  return (
    <Screen>
      <Hero
        title={bank?.title ?? 'Bank'}
        subtitle={bank ? `${bank.subjectName} · ${bankLevel(bank)} · ${q.data.questions.length} ta savol` : ''}
        right={<BackPill label="Banklar" onClick={onBack} />}
      />
      {error && <Alert>{error}</Alert>}
      {note && <Alert tone="ok">{note}</Alert>}
      <Card>
        <label className="block">
          <Row
            lead={<span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink"><Upload className="h-5 w-5" /></span>}
            title={busy === 'read' ? "Fayl o'qilmoqda…" : "Word'dan yuklash"}
            subtitle="Fayl (.docx) tanlang — avval tekshirasiz, keyin saqlanadi"
            right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
          />
          <input type="file" accept=".docx,.txt" className="hidden" disabled={busy != null}
            onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; void read(f) }} />
        </label>
        <p className="px-4 pb-3 text-[12px] text-slate-400">
          To'g'ri javob: <b>*B)</b>, alohida qatorda <b>Javob: C</b> yoki variantni qalin qiling. Rasm va formulalar Word'dan o'qiladi.
          Savol yoki variant rasmini tahrirlash — kompyuterdagi LMS'da (Test savollarim).
        </p>
      </Card>
      <AsyncBlock query={q} loadingLabel="Savollar yuklanmoqda…">
        {(d) =>
          d.questions.length === 0 ? (
            <Card>
              <EmptyState icon={<FileQuestion className="h-9 w-9" />} title="Hali savol yo'q" note="Word fayldagi testni yuklang." />
            </Card>
          ) : (
            <Card title="Bankdagi savollar">
              {d.questions.map((x, i) => (
                <QuestionView key={x.id} n={i + 1} q={x} deleting={busy === x.id} onDelete={() => void remove(x, i + 1)} />
              ))}
            </Card>
          )
        }
      </AsyncBlock>
      <div className="h-4" />
    </Screen>
  )
}
