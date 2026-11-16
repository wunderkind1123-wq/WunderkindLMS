import { useCallback, useEffect, useRef, useState } from 'react'
import { Award, Download, Eye, FileSpreadsheet, RefreshCw, Send, Upload } from 'lucide-react'
import {
  confirmImport,
  downloadCertificate,
  downloadResultTemplate,
  exportEvent,
  fmtDateTime,
  getRegistrations,
  getResultSample,
  previewImport,
  sendResults,
  setScore,
  testResult,
  type ImportRow,
  type OlympiadEvent,
  type OlympiadRegistration,
  type ResultSample,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { RichText } from '@/components/ui/RichText'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { Badge, ErrorBox, RegBadge } from './common'
import { TH, errText } from './lib'

const num = (v: number | null | undefined) => (v === null || v === undefined ? '' : String(v))

/** Bitta katakdagi ballni tahrirlash: Enter yoki fokus ketganda saqlanadi. */
function ScoreCell({ row, max, onSaved, onError }: { row: OlympiadRegistration; max: number; onSaved: (r: OlympiadRegistration) => void; onError: (m: string) => void }) {
  const [v, setV] = useState(num(row.score))
  const [saving, setSaving] = useState(false)

  const commit = async () => {
    const t = v.trim().replace(',', '.')
    if (t === num(row.score)) return
    const score = t === '' ? null : Number(t)
    if (score !== null && (Number.isNaN(score) || score < 0 || score > max)) {
      onError(`Ball 0 dan ${max} gacha`)
      setV(num(row.score))
      return
    }
    setSaving(true)
    try {
      onSaved(await setScore(row.id, score))
    } catch (e) {
      onError(errText(e))
      setV(num(row.score))
    } finally {
      setSaving(false)
    }
  }

  return (
    <input
      value={v}
      disabled={saving}
      inputMode="decimal"
      onChange={(e) => setV(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
      className="w-20 rounded-lg border border-slate-200 px-2 py-1 text-right text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
    />
  )
}

function ImportModal({ event, onClose, onDone }: { event: OlympiadEvent; onClose: () => void; onDone: (saved: number) => void }) {
  const file = useRef<HTMLInputElement>(null)
  const [rows, setRows] = useState<ImportRow[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const valid = (rows ?? []).filter((r) => r.status !== 'error' && r.registrationId && r.score !== null)
  const errors = (rows ?? []).filter((r) => r.status === 'error').length

  return (
    <Modal
      open
      onClose={onClose}
      title="Natijalarni Exceldan yuklash"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button
            disabled={busy || valid.length === 0}
            onClick={async () => {
              setBusy(true)
              try {
                const r = await confirmImport(event.id, valid.map((x) => ({ registrationId: x.registrationId!, score: x.score! })))
                onDone(r.saved)
              } catch (e) {
                setError(errText(e))
              } finally {
                setBusy(false)
              }
            }}
          >
            Tasdiqlash ({valid.length})
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-500">
          1) Shablonni yuklab oling — unda ro'yxatdagilar raqami va ismi bor. 2) «Ball» ustunini to'ldiring. 3) Faylni yuklang va
          tekshirib, tasdiqlang. Ism faqat tekshirish uchun — asosiy kalit ishtirokchi raqami.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => downloadResultTemplate(event.id).catch((e) => setError(errText(e)))}>
            <FileSpreadsheet className="h-4 w-4" /> Shablon
          </Button>
          <Button variant="secondary" disabled={busy} onClick={() => file.current?.click()}>
            <Upload className="h-4 w-4" /> Fayl tanlash (.xlsx)
          </Button>
          <input
            ref={file}
            type="file"
            accept=".xlsx"
            className="hidden"
            onChange={async (e) => {
              const f = e.target.files?.[0]
              e.target.value = ''
              if (!f) return
              setBusy(true)
              setError('')
              try {
                setRows(await previewImport(event.id, f))
              } catch (err) {
                setError(errText(err, "Faylni o'qib bo'lmadi"))
              } finally {
                setBusy(false)
              }
            }}
          />
        </div>
        <ErrorBox message={error} />
        {rows && (
          <>
            <p className="text-sm text-slate-600">
              Topildi: <b>{valid.length}</b> · Xato: <b className={errors ? 'text-red-600' : ''}>{errors}</b> · Ogohlantirish:{' '}
              <b>{rows.filter((r) => r.status === 'warning').length}</b>
            </p>
            <div className="max-h-96 overflow-auto rounded-xl border border-slate-100">
              <table className="w-full text-sm">
                <thead className={TH}>
                  <tr>
                    <th className="px-3 py-2">Qator</th>
                    <th className="px-3 py-2">Raqam</th>
                    <th className="px-3 py-2">Exceldagi ism</th>
                    <th className="px-3 py-2">Bazadagi ism</th>
                    <th className="px-3 py-2">Ball</th>
                    <th className="px-3 py-2">Holat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((r) => (
                    <tr key={r.row} className={cn(r.status === 'error' && 'bg-red-50/60', r.status === 'warning' && 'bg-amber-50/60')}>
                      <td className="px-3 py-1.5 text-slate-400">{r.row}</td>
                      <td className="px-3 py-1.5 font-mono text-xs">{r.number}</td>
                      <td className="px-3 py-1.5">{r.name}</td>
                      <td className="px-3 py-1.5">{r.foundName ?? '—'}</td>
                      <td className="px-3 py-1.5">{r.scoreText}</td>
                      <td className="px-3 py-1.5 text-xs">
                        {r.status === 'ok' ? <span className="text-emerald-600">OK</span> : <span className={r.status === 'error' ? 'text-red-600' : 'text-amber-700'}>{r.error}</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}

/** Tadbir → Natijalar (§9). */
export function ResultsTab({ event, onChanged }: { event: OlympiadEvent; onChanged: () => void }) {
  const [rows, setRows] = useState<OlympiadRegistration[] | null>(null)
  const [importing, setImporting] = useState(false)
  const [sample, setSample] = useState<ResultSample | null>(null)
  const [sendMode, setSendMode] = useState<'all' | 'changed' | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    getRegistrations(event.id)
      .then((list) =>
        setRows(
          list
            .filter((r) => r.status !== 'cancelled' && r.status !== 'waitlist')
            .sort((a, b) => (a.rank ?? 1e9) - (b.rank ?? 1e9) || a.numberValue - b.numberValue),
        ),
      )
      .catch((e) => {
        setRows([])
        setError(errText(e, "Yuklab bo'lmadi"))
      })
  }, [event.id])
  useEffect(load, [load])

  const openSample = async (mode: 'all' | 'changed' | null) => {
    setError('')
    try {
      setSample(await getResultSample(event.id))
      setSendMode(mode)
    } catch (e) {
      setError(errText(e))
    }
  }

  const changed = (rows ?? []).filter((r) => r.resultChanged).length
  const scored = (rows ?? []).filter((r) => r.score !== null).length
  const unsent = (rows ?? []).filter((r) => r.score !== null && r.status === 'attended' && !r.resultSentAt && r.hasTelegram).length

  const doSend = async () => {
    setBusy(true)
    try {
      const r = await sendResults(event.id, sendMode === 'changed')
      setToast(`Navbatga qo'yildi: ${r.queued} ta. Bir necha daqiqada yetib boradi.`)
      setSample(null)
      setSendMode(null)
      load()
      onChanged()
    } catch (e) {
      setError(errText(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <p className="mr-auto text-sm text-slate-500">
          Ball kiritilgan: <b>{scored}</b> / {rows?.length ?? 0} · Maksimal ball: <b>{event.maxScore}</b>
          {event.resultsSentAt && <> · Natija yuborilgan: {fmtDateTime(event.resultsSentAt)}</>}
        </p>
        <Button variant="secondary" onClick={() => setImporting(true)}>
          <Upload className="h-4 w-4" /> Exceldan yuklash
        </Button>
        <Button variant="secondary" onClick={() => exportEvent(event.id, 'results').catch((e) => setError(errText(e)))}>
          <Download className="h-4 w-4" /> Excel
        </Button>
        <Button variant="secondary" disabled={scored === 0} onClick={() => openSample(null)}>
          <Eye className="h-4 w-4" /> Namuna
        </Button>
        {changed > 0 && (
          <Button variant="secondary" onClick={() => openSample('changed')}>
            <RefreshCw className="h-4 w-4" /> Tuzatilganni qayta yuborish ({changed})
          </Button>
        )}
        <Button disabled={unsent === 0} onClick={() => openSample('all')}>
          <Send className="h-4 w-4" /> Natijalarni yuborish{unsent > 0 ? ` (${unsent})` : ''}
        </Button>
      </div>

      <ErrorBox message={error} />

      <Card className="overflow-hidden p-0">
        {rows === null ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">Ro'yxatda hech kim yo'q</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className={TH}>
                <tr>
                  <th className="px-4 py-3">O'rin</th>
                  <th className="px-4 py-3">Raqam</th>
                  <th className="px-4 py-3">F.I.Sh</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Ball</th>
                  <th className="px-4 py-3 text-right">Foiz</th>
                  <th className="px-4 py-3">Kiritdi</th>
                  <th className="px-4 py-3">Yuborildi</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={r.id} className="text-slate-700">
                    <td className="px-4 py-2 font-semibold">{r.rank ?? '—'}</td>
                    <td className="whitespace-nowrap px-4 py-2 font-mono text-xs">{r.number}</td>
                    <td className="px-4 py-2 font-medium">{r.fullName}</td>
                    <td className="px-4 py-2">{r.grade}</td>
                    <td className="px-4 py-2">
                      <RegBadge status={r.status} />
                    </td>
                    <td className="px-4 py-2 text-right">
                      <ScoreCell
                        key={`${r.id}:${r.score}`}
                        row={r}
                        max={event.maxScore}
                        onError={setError}
                        onSaved={() => {
                          setError('')
                          load()
                          onChanged()
                        }}
                      />
                    </td>
                    <td className="px-4 py-2 text-right">{r.percent !== null ? `${r.percent}%` : '—'}</td>
                    <td className="whitespace-nowrap px-4 py-2 text-xs text-slate-400">
                      {r.scoreUpdatedBy}
                      {r.scoreUpdatedAt && <span className="block">{fmtDateTime(r.scoreUpdatedAt)}</span>}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2 text-xs">
                      {r.resultSentAt ? (
                        <span className="text-emerald-600">{fmtDateTime(r.resultSentAt)}</span>
                      ) : !r.hasTelegram ? (
                        <span className="text-slate-400">Telegram yo'q</span>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                      {r.resultChanged && <Badge className="ml-1 bg-amber-50 text-amber-700">tuzatilgan</Badge>}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2 text-right">
                      {r.score !== null && (
                        <button
                          title="Sertifikat"
                          onClick={() => downloadCertificate(r.id).catch((e) => setError(errText(e)))}
                          className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Award className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
      <p className="text-xs text-slate-400">
        O'rin — ball bo'yicha, teng ballga bir xil o'rin (1, 2, 2, 4). Natija faqat kelganlarga va Telegram orqali yozilganlarga boradi.
      </p>

      {importing && (
        <ImportModal
          event={event}
          onClose={() => setImporting(false)}
          onDone={(saved) => {
            setImporting(false)
            setToast(`Saqlandi: ${saved} ta`)
            load()
            onChanged()
          }}
        />
      )}

      {sample && (
        <Modal
          open
          onClose={() => {
            setSample(null)
            setSendMode(null)
          }}
          title={sendMode ? (sendMode === 'changed' ? 'Tuzatilgan natijalarni yuborish' : 'Natijalarni yuborish') : 'Natija xabari namunasi'}
          footer={
            <>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={async () => {
                  try {
                    await testResult(sample.registrationId)
                    setToast('Telegramingizga yuborildi')
                  } catch (e) {
                    setError(errText(e))
                  }
                }}
              >
                <Send className="h-4 w-4" /> Menga test
              </Button>
              {sample.withCertificate && (
                <Button variant="secondary" onClick={() => downloadCertificate(sample.registrationId).catch((e) => setError(errText(e)))}>
                  <Award className="h-4 w-4" /> Sertifikat namunasi
                </Button>
              )}
              {sendMode && (
                <Button disabled={busy} onClick={doSend}>
                  Yuborish
                </Button>
              )}
            </>
          }
        >
          <div className="space-y-3">
            <ErrorBox message={error} />
            <div className="rounded-2xl bg-sky-50 p-4 text-sm text-slate-800">
              <RichText html={sample.html ?? ''} />
              {(sample.buttons ?? []).map((b) => (
                <div key={b.url} className="mt-3 rounded-lg bg-white px-3 py-2 text-center text-sm font-medium text-sky-700">
                  {b.text}
                </div>
              ))}
              {sample.withCertificate && <p className="mt-2 text-xs text-slate-500">+ sertifikat fayli</p>}
            </div>
            {sendMode && (
              <p className="text-sm text-slate-600">
                {sendMode === 'changed'
                  ? `Bali tuzatilgan ${changed} ta ishtirokchiga qayta yuboriladi.`
                  : `Yuboriladi: ${unsent} ta (kelgan, balli, Telegramda yozilgan). Kelmaganlarga bormaydi.`}
                {sample.noTelegram > 0 && ` CRM'da qo'shilgan ${sample.noTelegram} ta bolaga Telegram yo'qligi uchun bormaydi.`}
              </p>
            )}
          </div>
        </Modal>
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
