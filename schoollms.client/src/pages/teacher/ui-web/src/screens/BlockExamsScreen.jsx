import { Award, ChevronRight } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { TapScale } from '../components/AppCard'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { blockExamErrorMessage } from '../lib/blockExams'

// Blok test natijalari — har (imtihon, yo'nalish) uchun bir qator (mijoz, 2026-11-03;
// docs/modules/track-groups-as-classes.md). Ro'yxatni server cheklaydi: dars jadvalida shu
// o'qituvchiga biriktirilgan yo'nalish·fan. Ekran hech narsani filtrlamaydi.
export default function BlockExamsScreen({ onNavigate, onBack }) {
  const list = useFetch(() => api.blockExams(), [])
  const items = list.data

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title="Blok test natijalari" subtitle="O'z fan va yo'nalishingiz bo'yicha" onBack={onBack} />
      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        {list.loading && !items ? (
          <Loading label="Imtihonlar yuklanmoqda…" />
        ) : list.error ? (
          <ErrorState error={{ message: blockExamErrorMessage(list.error) }} onRetry={list.reload} />
        ) : !items || items.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><Award size={30} /></EmptyIllustration>}
            title="Natija kiritiladigan imtihon yo'q"
            subtitle="Imtihon e'lon qilinganda va dars jadvalida shu yo'nalish·fan sizga biriktirilganda bu yerda chiqadi."
          />
        ) : (
          items.map((item) => (
            <div key={`${item.examId}|${item.ownerId}`} className="mb-2.5">
              <TapScale onClick={() => onNavigate?.('blockExamEntry', { item })}>
                <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3">
                  <div className="w-12 h-12 shrink-0 rounded-xl bg-primary flex items-center justify-center text-white text-[13px] font-extrabold">
                    {(item.ownerName || '').slice(0, 4)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-[15px] font-bold text-text truncate">
                      {item.ownerName} · {item.subjects.map((s) => s.name).join(', ')}
                    </p>
                    <p className="text-[12px] text-muted truncate">
                      {item.title}
                      {item.examDate ? ` · ${item.examDate}` : ''}
                    </p>
                  </div>
                  <span className="shrink-0 text-[13px] font-bold text-muted">
                    {item.doneCount}/{item.pupilCount}
                  </span>
                  <ChevronRight size={20} className="text-faint shrink-0" />
                </div>
              </TapScale>
            </div>
          ))
        )}
      </div>
    </div>
  )
}
