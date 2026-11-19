import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { AlertTriangle, ArrowRightLeft, BarChart3, CalendarRange, CheckCircle2, Pencil, Plus, Power, Trash2, Upload, Users } from 'lucide-react'
import type { Block } from '@/api/services/blocks'
import {
  DAY_PATTERN_LABELS,
  activateBlock,
  apiError,
  deactivateBlock,
  deleteBlock,
  getBlocks,
} from '@/api/services/blocks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { BlockImportModal } from './BlockImportModal'

/**
 * Bloklar ro'yxati — TZ 6.1 (`docs/modules/blocks.md`): nomi, fan, sinflar, guruhlar,
 * ustozlar, o'quvchilar soni. Yangi blok faol emas: o'quvchilar taqsimlanib bo'lgach
 * faollashtiriladi (2-qoida).
 */
export function BlocksPage() {
  const navigate = useNavigate()
  const [blocks, setBlocks] = useState<Block[]>([])
  const [loading, setLoading] = useState(true)
  const [importOpen, setImportOpen] = useState(false)
  const [notice, setNotice] = useState<{ blockId: string; errors: string[] } | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    getBlocks()
      .then(setBlocks)
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumot kelganda ro'yxatni to'ldiramiz (loyihadagi mavjud naqsh)
    load()
  }, [load])

  const handleActivate = async (b: Block) => {
    try {
      const res = await activateBlock(b.id)
      setNotice(res.isActive ? null : { blockId: b.id, errors: res.errors })
      load()
    } catch (e) {
      alert(apiError(e, "Faollashtirib bo'lmadi"))
    }
  }

  const handleDeactivate = async (b: Block) => {
    if (!confirm(`"${b.name}" blokini faolsizlantirasizmi?`)) return
    await deactivateBlock(b.id).catch((e) => alert(apiError(e, "Faolsizlantirib bo'lmadi")))
    load()
  }

  const handleDelete = async (b: Block) => {
    if (!confirm(`"${b.name}" blokini o'chirasizmi? Guruhlar va taqsimlash ham o'chadi.`)) return
    await deleteBlock(b.id).catch((e) => alert(apiError(e, "O'chirib bo'lmadi")))
    load()
  }

  if (loading && blocks.length === 0) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Bloklar</h1>
          <p className="text-sm text-slate-400">
            Bir nechta sinf yoki yo'nalish bir soatda birlashib, 2 ta guruhga bo'linadigan darslar — jami{' '}
            {blocks.length} ta
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => navigate('/admin/blocks/hisobot')}>
            <BarChart3 className="h-4 w-4" /> Hisobot
          </Button>
          <Button variant="secondary" onClick={() => navigate('/admin/blocks/kochirishlar')}>
            <ArrowRightLeft className="h-4 w-4" /> Ko'chirishlar
          </Button>
          <Button variant="secondary" onClick={() => setImportOpen(true)}>
            <Upload className="h-4 w-4" /> Excel'dan taqsimlash
          </Button>
          <Button onClick={() => navigate('/admin/blocks/new')}>
            <Plus className="h-4 w-4" /> Yangi blok
          </Button>
        </div>
      </div>

      {notice && (
        <Card className="border-amber-200 bg-amber-50 text-sm text-amber-800">
          <div className="flex items-start gap-2">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div>
              <p className="font-medium">Blok faollashmadi:</p>
              <ul className="mt-1 list-disc pl-5">
                {notice.errors.map((e) => (
                  <li key={e}>{e}</li>
                ))}
              </ul>
            </div>
          </div>
        </Card>
      )}

      <Card className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">Blok</th>
                <th className="px-4 py-3">Fan</th>
                <th className="px-4 py-3">Sinflar</th>
                <th className="px-4 py-3">Guruhlar va ustozlar</th>
                <th className="px-4 py-3">O'quvchilar</th>
                <th className="px-4 py-3">Holat</th>
                <th className="px-4 py-3 text-right">Amallar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {blocks.map((b) => (
                <tr
                  key={b.id}
                  className="cursor-pointer hover:bg-slate-50/60"
                  onClick={() => navigate(`/admin/blocks/${b.id}/taqsimlash`)}
                >
                  <td className="px-4 py-3">
                    <div className="font-medium text-slate-800">{b.name}</div>
                    <div className="text-xs text-slate-400">{DAY_PATTERN_LABELS[b.dayPattern]}</div>
                  </td>
                  <td className="px-4 py-3 text-slate-600">{b.subjectName}</td>
                  <td className="px-4 py-3 text-slate-600">{b.sources.map((s) => s.name).join(', ')}</td>
                  <td className="px-4 py-3 text-slate-600">
                    {b.groups.map((g) => (
                      <div key={g.id}>
                        <span className="font-medium text-slate-700">{g.name}</span> · {g.teacherName ?? '—'}{' '}
                        <span className="text-slate-400">({g.memberCount})</span>
                      </div>
                    ))}
                  </td>
                  <td className="px-4 py-3 text-slate-600">
                    {b.studentCount} ta
                    {b.unassignedCount > 0 && (
                      <div className="text-xs text-amber-600">{b.unassignedCount} ta taqsimlanmagan</div>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={cn(
                        'rounded-md px-2 py-0.5 text-xs font-medium',
                        b.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
                      )}
                    >
                      {b.isActive ? 'Faol' : 'Faol emas'}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-0.5" onClick={(e) => e.stopPropagation()}>
                      <IconBtn icon={Users} title="Taqsimlash" onClick={() => navigate(`/admin/blocks/${b.id}/taqsimlash`)} />
                      <IconBtn icon={CalendarRange} title="Jadval" onClick={() => navigate(`/admin/blocks/${b.id}/jadval`)} />
                      <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => navigate(`/admin/blocks/${b.id}`)} />
                      {b.isActive ? (
                        <IconBtn icon={Power} title="Faolsizlantirish" onClick={() => handleDeactivate(b)} />
                      ) : (
                        <>
                          <IconBtn icon={CheckCircle2} title="Faollashtirish" onClick={() => handleActivate(b)} />
                          <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(b)} />
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {blocks.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-12 text-center text-slate-400">
                    Blok yo'q. "Yangi blok" tugmasi bilan oching.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <BlockImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onDone={() => {
          setImportOpen(false)
          load()
        }}
      />
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

export function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
