/**
 * LIDERBORD — sinf rahbari uchun (mijoz, 2026-11-27): "lider bordni sinf yoki guruhga biriktirilgan sinf
 * rahbariga ko'rinadigan qilsak". FAQAT KO'RISH — ball qo'shish/ayirish adminda qoladi. Doira: rahbarlikdagi
 * sinflar va "Sinf rahbari" qilib biriktirilgan yo'nalish guruhlari. O'rin faqat sinf/guruh ichida (ota-onalardagi
 * kabi), har o'quvchining ballari qismlari bilan (bosilsa ochiladi). Hisobni server qiladi
 * (`/api/teacher/homeroom/leaderboard`, admin leaderboardi bilan bir xil).
 */
import { useState } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, Trophy } from 'lucide-react'
import { Card, EmptyState, Hero, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { haptic } from '../../lib/telegram'
import { teacherApi } from '../../lib/teacherApi'
import { AsyncBlock } from './shared'

const fmt = (v, digits = 1) => (v == null ? '—' : Number(v).toFixed(digits).replace('.', ','))
const signed = (p) => (p > 0 ? `+${p}` : String(p))
const scopeLabel = (s) => (s.kind === 'class' ? `${s.name} sinf` : s.name)

/** "Bugun" dagi karta — faqat rahbarligi bor o'qituvchida ko'rsatiladi. */
export function HomeroomLeaderboardCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <Trophy className="h-5 w-5" />
          </span>
        }
        title="Liderbord"
        subtitle="Sinfim va guruhim reytingi"
        right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
      />
    </Card>
  )
}

export function HomeroomLeaderboardScreen({ onBack }) {
  const state = useAsync(() => teacherApi.homeroomLeaderboard(), [])
  return (
    <Screen>
      <Hero
        title="Liderbord"
        subtitle="O'quv yili bo'yicha reyting"
        right={
          <button
            type="button"
            onClick={onBack}
            aria-label="Orqaga"
            className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
          >
            <ChevronLeft className="h-4 w-4" />
            Bugun
          </button>
        }
      />
      <AsyncBlock query={state} loadingLabel="Liderbord hisoblanmoqda…">
        {(data) => <Board data={data} />}
      </AsyncBlock>
    </Screen>
  )
}

function Board({ data }) {
  const scopes = data.scopes ?? []
  const [key, setKey] = useState(scopes[0] ? scopes[0].kind + scopes[0].id : null)
  const scope = scopes.find((s) => s.kind + s.id === key) ?? scopes[0]

  if (!scope) {
    return (
      <Card>
        <EmptyState
          icon={<Trophy className="h-9 w-9" />}
          title="Sinf yoki guruh biriktirilmagan"
          note="Siz sinf rahbari bo'lgan sinf yoki yo'nalish guruhi bo'lsa, reyting shu yerda ko'rinadi."
        />
      </Card>
    )
  }

  return (
    <>
      {scopes.length > 1 && (
        <div className="mx-4 mt-3 flex gap-1 overflow-x-auto rounded-2xl bg-white p-1">
          {scopes.map((s) => (
            <button
              key={s.kind + s.id}
              type="button"
              onClick={() => {
                haptic('light')
                setKey(s.kind + s.id)
              }}
              className={
                'shrink-0 grow truncate rounded-xl px-3 py-2.5 text-[14px] font-semibold transition ' +
                (s.kind + s.id === scope.kind + scope.id ? 'bg-brand text-brand-ink' : 'text-slate-500')
              }
            >
              {scopeLabel(s)}
            </button>
          ))}
        </div>
      )}

      <Ranking key={scope.kind + scope.id} scope={scope} />

      <p className="mx-4 mt-3 px-1 text-[12px] leading-relaxed text-slate-400">
        Ball butun o'quv yili bo'yicha hisoblanadi: baholar (yo'nalishda — oraliq testlar) {data.weights.grades}%, davomat{' '}
        {data.weights.attendance}%, oylik test {data.weights.monthly}%, gamifikatsiya {data.weights.behaviour}%. Maktab
        bergan qo'shimcha ballar ustiga qo'shiladi. O'quvchini bossangiz, ballari qismlari bilan ochiladi.
      </p>
    </>
  )
}

function Ranking({ scope }) {
  const [open, setOpen] = useState(null)
  return (
    <Card title={`${scopeLabel(scope)} reytingi · ${scope.count} o'quvchi`}>
      {scope.rows.length === 0 && (
        <p className="px-4 py-4 text-[14px] text-slate-400">Bu yerda hali o'quvchi yo'q.</p>
      )}
      {scope.rows.map((r) => {
        const isOpen = open === r.studentId
        return (
          <div key={r.studentId} className="border-t border-slate-100 first:border-t-0">
            <button
              type="button"
              onClick={() => {
                haptic('light')
                setOpen(isOpen ? null : r.studentId)
              }}
              className="flex w-full items-center gap-3 px-4 py-2.5 text-left"
            >
              <RankBadge rank={r.rank} />
              <div className="min-w-0 flex-1">
                <p className="truncate text-[15px] font-medium">{r.fullName}</p>
                {scope.kind === 'track' && <p className="truncate text-[12px] text-slate-400">{r.className}</p>}
              </div>
              <span className="shrink-0 text-[15px] font-bold tabular-nums">{fmt(r.total)}</span>
              <ChevronDown className={'h-4 w-4 shrink-0 text-slate-300 transition ' + (isOpen ? 'rotate-180' : '')} />
            </button>
            {isOpen && <Parts row={r} />}
          </div>
        )
      })}
    </Card>
  )
}

function RankBadge({ rank }) {
  const tone =
    rank === 1
      ? 'bg-amber-400 text-white'
      : rank === 2
        ? 'bg-slate-400 text-white'
        : rank === 3
          ? 'bg-orange-400 text-white'
          : 'bg-slate-100 text-slate-500'
  return (
    <span className={'flex h-8 min-w-8 shrink-0 items-center justify-center rounded-full px-1.5 text-[13px] font-bold ' + tone}>
      {rank}
    </span>
  )
}

function Parts({ row }) {
  const parts = [
    row.isTrack
      ? { label: 'Oraliq testlar', value: row.interim == null ? '—' : `${fmt(row.interim)}%`, note: row.interimCount ? `${row.interimCount} ta test` : null }
      : { label: "O'rtacha baho", value: fmt(row.grades, 2), note: row.gradeCount ? `${row.gradeCount} ta baho` : null },
    { label: 'Davomat', value: row.attendance == null ? '—' : `${fmt(row.attendance)}%`, note: row.absent ? `${row.absent} ta dars qoldirilgan` : null },
    { label: 'Oylik test', value: row.monthlyTest == null ? '—' : `${fmt(row.monthlyTest)}%`, note: row.monthlyCount ? `${row.monthlyCount} ta test` : null },
    { label: 'Gamifikatsiya', value: String(row.behaviour), note: 'ball' },
  ]
  if (row.manualPoints !== 0) parts.push({ label: "Qo'shimcha ball", value: signed(row.manualPoints), note: null })
  return (
    <div className="mx-4 mb-3 rounded-xl bg-slate-50">
      {parts.map((p) => (
        <div key={p.label} className="flex items-center justify-between gap-3 border-t border-slate-100 px-3 py-2 first:border-t-0">
          <span className="min-w-0">
            <span className="block text-[14px]">{p.label}</span>
            {p.note && <span className="block text-[12px] text-slate-400">{p.note}</span>}
          </span>
          <span className="shrink-0 text-[14px] font-bold tabular-nums">{p.value}</span>
        </div>
      ))}
    </div>
  )
}
