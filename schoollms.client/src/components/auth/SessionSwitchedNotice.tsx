import { UserRoundCog } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { roleLabels } from '@/config/navigation'
import type { User } from '@/types'

interface Props {
  /** Bu oyna ochilgan akkaunt. */
  current: User
  /** Boshqa oynada kirilgan — hozir so'rovlar shu akkaunt nomidan ketadi. */
  active: User
  /** Umumiy sessiyani tozalab, login sahifasiga o'tadi. */
  onSwitchAccount: () => void
}

/**
 * Bir brauzerda boshqa akkaunt bilan kirilganda eski oynani to'sadi (mijoz, 2026-11-03).
 *
 * Hamma panellar tokenni bitta `localStorage` kalitida saqlaydi, shuning uchun yangi kirish ochiq
 * turgan oynaning ham sessiyasini almashtiradi. Bu oyna esa eski akkaunt sahifasini ko'rsatishda davom
 * etadi va server so'rovlarni rad etib, tushunarsiz "yuklab bo'lmadi" chiqarardi. Yopish tugmasi
 * yo'q: yopilsa, o'sha xatolar qaytadi.
 */
export function SessionSwitchedNotice({ current, active, onSwitchAccount }: Props) {
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-sm">
      <div role="alertdialog" aria-modal="true" aria-labelledby="session-switched-title" className="w-full max-w-md rounded-2xl bg-white p-6 shadow-xl">
        <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-amber-50 text-amber-600">
          <UserRoundCog className="h-6 w-6" />
        </div>
        <h2 id="session-switched-title" className="mt-4 font-semibold text-slate-800">
          Bu brauzerda boshqa akkaunt bilan kirilgan
        </h2>
        <p className="mt-2 text-sm text-slate-600">
          Siz hozir <span className="font-medium text-slate-800">{active.fullName}</span> ({roleLabels[active.role]}) sifatida
          kirgansiz, shuning uchun {current.fullName} ({roleLabels[current.role]}) sahifasi bu oynada ishlamaydi.
        </p>
        <p className="mt-2 text-xs text-slate-400">
          Ikki akkaunt bilan birga ishlash uchun ikkinchisini Incognito oynada yoki boshqa brauzerda oching.
        </p>
        <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={() => window.location.assign('/')}>
            {roleLabels[active.role]} sifatida davom etish
          </Button>
          <Button onClick={onSwitchAccount}>Qayta kirish</Button>
        </div>
      </div>
    </div>
  )
}
