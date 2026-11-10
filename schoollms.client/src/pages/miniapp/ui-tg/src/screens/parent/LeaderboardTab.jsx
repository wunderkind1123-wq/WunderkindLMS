/**
 * LIDERBORD — ota-ona/o'quvchi uchun (mijoz, 2026-11-10): "ota-onalar uchun faqat o'zini farzandini
 * sinfi yoki yo'nalishi bo'yicha liderbord menyuida ko'rinishi kerak". Maktab bo'yicha umumiy reyting
 * YO'Q. Boshqa o'quvchilardan faqat ism va jami ball; farzandning ballari esa qismlari bilan.
 *
 * Hisobni server qiladi (`/api/student/leaderboard`, admin leaderboardi bilan bir xil): baholar (yo'nalish
 * o'quvchisida — oraliq testlar foizi), davomat, oylik test, gamifikatsiya va qo'shimcha ballar.
 */
import { useState } from 'react'
import { Crown, Trophy } from 'lucide-react'
import { Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { haptic } from '../../lib/telegram'
import { getLeaderboard } from '../../lib/parentApi'
import { AsyncBlock } from './shared'

const fmt = (v, digits = 1) => (v == null ? '—' : Number(v).toFixed(digits).replace('.', ','))
const signed = (p) => (p > 0 ? `+${p}` : String(p))

export function LeaderboardTab({ child }) {
  const state = useAsync(() => getLeaderboard(child.id), [child.id])
  return (
    <AsyncBlock state={state} loadingLabel="Liderbord hisoblanmoqda…">
      {(data) => <Board data={data} />}
    </AsyncBlock>
  )
}

function Board({ data }) {
  const scopes = data.scopes ?? []
  const [kind, setKind] = useState(scopes[0]?.kind ?? 'class')
  const scope = scopes.find((s) => s.kind === kind) ?? scopes[0]

  if (!data.me || !scope) {
    return (
      <Card>
        <EmptyState
          icon={<Trophy className="h-9 w-9" />}
          title="Liderbord hali tayyor emas"
          note="Farzandingiz sinfga biriktirilgach va baholar kiritila boshlagach shu yerda o'rni ko'rinadi."
        />
      </Card>
    )
  }

  return (
    <>
      {scopes.length > 1 && (
        <div className="mx-4 mt-3 flex rounded-2xl bg-white p-1">
          {scopes.map((s) => (
            <button
              key={s.kind + s.id}
              type="button"
              onClick={() => {
                haptic('light')
                setKind(s.kind)
              }}
              className={
                'flex-1 truncate rounded-xl px-3 py-2.5 text-[14px] font-semibold transition ' +
                (s.kind === scope.kind ? 'bg-brand text-brand-ink' : 'text-slate-500')
              }
            >
              {s.kind === 'class' ? `${s.name} sinf` : s.name}
            </button>
          ))}
        </div>
      )}

      <MyPlace me={data.me} scope={scope} />
      <Ranking scope={scope} />

      <p className="mx-4 mt-3 px-1 text-[12px] leading-relaxed text-slate-400">
        Ball butun o'quv yili bo'yicha hisoblanadi:{' '}
        {data.me.isTrack ? 'oraliq testlar' : 'baholar'} {data.weights.grades}%, davomat {data.weights.attendance}%, oylik test{' '}
        {data.weights.monthly}%, gamifikatsiya {data.weights.behaviour}%. Maktab bergan qo'shimcha ballar ustiga qo'shiladi.
      </p>
    </>
  )
}

function MyPlace({ me, scope }) {
  const parts = [
    me.isTrack
      ? { label: 'Oraliq testlar', value: me.interim == null ? '—' : `${fmt(me.interim)}%`, note: me.interimCount ? `${me.interimCount} ta test` : null }
      : { label: "O'rtacha baho", value: fmt(me.grades, 2), note: me.gradeCount ? `${me.gradeCount} ta baho` : null },
    { label: 'Davomat', value: me.attendance == null ? '—' : `${fmt(me.attendance)}%`, note: me.absent ? `${me.absent} ta dars qoldirilgan` : null },
    { label: 'Oylik test', value: me.monthlyTest == null ? '—' : `${fmt(me.monthlyTest)}%`, note: me.monthlyCount ? `${me.monthlyCount} ta test` : null },
    { label: 'Gamifikatsiya', value: String(me.behaviour), note: 'ball' },
  ]
  if (me.manualPoints !== 0) parts.push({ label: "Qo'shimcha ball", value: signed(me.manualPoints), note: null })

  return (
    <Card>
      <div className="flex items-center gap-4 px-4 pb-3 pt-4">
        <span className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-brand text-brand-ink">
          {scope.myRank === 1 ? <Crown className="h-6 w-6" /> : <Trophy className="h-6 w-6" />}
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-[26px] font-extrabold leading-none">
            {scope.myRank ?? '—'}
            <span className="text-[16px] font-bold">-o'rin</span>
          </p>
          <p className="mt-1 text-[13px] text-slate-500">
            {scope.kind === 'class' ? `${scope.name} sinfdagi` : `«${scope.name}» yo'nalishidagi`} {scope.count} o'quvchidan
          </p>
        </div>
        <div className="shrink-0 text-right">
          <p className="text-[22px] font-extrabold leading-none">{fmt(me.total)}</p>
          <p className="mt-1 text-[12px] text-slate-500">ball</p>
        </div>
      </div>
      {parts.map((p) => (
        <div key={p.label} className="flex items-center justify-between gap-3 border-t border-slate-100 px-4 py-2.5">
          <span className="min-w-0">
            <span className="block text-[14px]">{p.label}</span>
            {p.note && <span className="block text-[12px] text-slate-400">{p.note}</span>}
          </span>
          <span className="shrink-0 text-[15px] font-bold tabular-nums">{p.value}</span>
        </div>
      ))}
    </Card>
  )
}

function Ranking({ scope }) {
  return (
    <Card title={scope.kind === 'class' ? `${scope.name} sinf reytingi` : `${scope.name} reytingi`}>
      {scope.rows.map((r, i) => (
        <div
          key={i}
          className={
            'flex items-center gap-3 border-t border-slate-100 px-4 py-2.5 first:border-t-0 ' + (r.isMe ? 'bg-brand/20' : '')
          }
        >
          <span
            className={
              'flex h-8 min-w-8 shrink-0 items-center justify-center rounded-full px-1.5 text-[13px] font-bold ' +
              (r.rank === 1
                ? 'bg-amber-400 text-white'
                : r.rank === 2
                  ? 'bg-slate-400 text-white'
                  : r.rank === 3
                    ? 'bg-orange-400 text-white'
                    : 'bg-slate-100 text-slate-500')
            }
          >
            {r.rank}
          </span>
          <div className="min-w-0 flex-1">
            <p className={'truncate text-[15px] ' + (r.isMe ? 'font-bold' : 'font-medium')}>{r.fullName}</p>
            {scope.kind === 'track' && <p className="truncate text-[12px] text-slate-400">{r.className}</p>}
          </div>
          <span className="shrink-0 text-[15px] font-bold tabular-nums">{fmt(r.total)}</span>
        </div>
      ))}
    </Card>
  )
}
