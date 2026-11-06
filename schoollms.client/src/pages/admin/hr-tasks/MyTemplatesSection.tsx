import { useState } from 'react'
import { Pencil, Plus, Repeat, Trash2 } from 'lucide-react'
import { deleteMyTemplate, getMyTemplates, hrErrorMessage, toggleMyTemplate } from '@/api/services/hrTasks'
import type { HrTaskTemplate } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, ConfirmModal, EmptyState, Skeleton, Switch } from '@/components/hr-tasks/ui'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { hrRecurrenceLabel, hrTaskKindLabel } from '@/components/hr-tasks/lib'
import { TemplateModal } from './TemplateModal'

/**
 * "Takroriy" tab of "Mening topshiriqlarim" (client, 2026-11-06: "o'zimga o'zim ham takroriy
 * topshiriq qo'sha olay"). Only the rules the employee made for themself — a rule a manager
 * aimed at them is the manager's and is not listed here.
 */
export function MyTemplatesSection({
  employeeId,
  onMessage,
  onError,
}: {
  employeeId: string
  onMessage: (message: string) => void
  onError: (message: string) => void
}) {
  const [version, setVersion] = useState(0)
  const list = useLoad(`my-templates|${version}`, getMyTemplates)
  const [editing, setEditing] = useState<HrTaskTemplate | 'new' | null>(null)
  const [confirm, setConfirm] = useState<HrTaskTemplate | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = (message: string) => {
    onMessage(message)
    setVersion((v) => v + 1)
  }

  async function toggle(t: HrTaskTemplate): Promise<void> {
    try {
      const next = await toggleMyTemplate(t.id)
      reload(next.isActive ? 'Takroriy topshiriq yoqildi' : 'Takroriy topshiriq to‘xtatildi')
    } catch (err) {
      onError(hrErrorMessage(err))
    }
  }

  async function remove(): Promise<void> {
    if (!confirm) return
    setBusy(true)
    try {
      await deleteMyTemplate(confirm.id)
      setConfirm(null)
      reload('Takroriy topshiriq o‘chirildi')
    } catch (err) {
      onError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  const items = list.data ?? []

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-slate-500">
          Belgilangan kunlarda topshiriq sizga avtomatik qo‘shiladi.
        </p>
        <Button onClick={() => setEditing('new')}>
          <Plus className="h-4 w-4" />
          Takroriy topshiriq
        </Button>
      </div>

      {list.loading && items.length === 0 ? (
        <Skeleton className="h-28 w-full rounded-2xl" />
      ) : items.length === 0 ? (
        <Card>
          {list.error ? (
            <EmptyState
              title="Ro‘yxatni yuklab bo‘lmadi"
              hint={list.error}
              action={
                <Button variant="secondary" onClick={list.reload}>
                  Qayta urinish
                </Button>
              }
            />
          ) : (
            <EmptyState
              icon={<Repeat className="h-5 w-5" />}
              title="O‘zingiz uchun takroriy topshiriq yo‘q"
              hint="Masalan: har dushanba haftalik rejani yozish yoki har kuni kundalikni tekshirish."
            />
          )}
        </Card>
      ) : (
        <ul className="space-y-3">
          {items.map((t) => (
            <li key={t.id} className="rounded-2xl border border-slate-200/80 bg-white p-3 shadow-sm sm:p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="break-words font-medium text-slate-800">{t.title}</p>
                  {t.description && <p className="mt-1 whitespace-pre-line break-words text-sm text-slate-500">{t.description}</p>}
                </div>
                <Switch checked={t.isActive} ariaLabel={`«${t.title}» yoqilgan`} onChange={() => void toggle(t)} />
              </div>
              <div className="mt-2 flex flex-wrap items-center gap-1.5">
                <Badge tone="info">{hrRecurrenceLabel(t)}</Badge>
                <Badge>{t.dueTime ? `soat ${t.dueTime.slice(0, 5)} gacha` : 'kun oxirigacha'}</Badge>
                <Badge>{hrTaskKindLabel(t.kind)}</Badge>
                {!t.isActive && <Badge tone="warning">To‘xtatilgan</Badge>}
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button variant="secondary" onClick={() => setEditing(t)}>
                  <Pencil className="h-4 w-4" />
                  Tahrirlash
                </Button>
                <Button variant="ghost" onClick={() => setConfirm(t)}>
                  <Trash2 className="h-4 w-4" />
                  O‘chirish
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {editing && (
        <TemplateModal
          key={editing === 'new' ? 'new' : editing.id}
          template={editing === 'new' ? null : editing}
          employees={[]}
          positions={[]}
          canManagePositions={false}
          selfEmployeeId={employeeId}
          onClose={() => setEditing(null)}
          onSaved={(message) => {
            setEditing(null)
            reload(message)
          }}
        />
      )}

      <ConfirmModal
        open={confirm !== null}
        onClose={() => setConfirm(null)}
        onConfirm={() => void remove()}
        danger
        busy={busy}
        confirmLabel="O‘chirish"
        title="Takroriy topshiriqni o‘chirish"
        description={`«${confirm?.title ?? ''}» o‘chiriladi va yangi nusxalar yaratilmaydi. Avval yaratilgan topshiriqlar tarixda qoladi. Vaqtincha to‘xtatish uchun tugmani o‘chiring.`}
      />
    </div>
  )
}
