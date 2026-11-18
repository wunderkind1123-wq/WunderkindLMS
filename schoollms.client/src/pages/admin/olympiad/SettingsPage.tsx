import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, Bot, CheckCircle2, FileText, Pencil, Plus, RotateCcw, Trash2, Upload } from 'lucide-react'
import {
  addChannel,
  checkBot,
  deleteChannel,
  getChannels,
  getSettings,
  getTemplates,
  saveBotToken,
  saveSettings,
  saveTemplate,
  updateChannel,
  uploadMedia,
  type BotCheck,
  type ChannelInput,
  type OlympiadAttachment,
  type OlympiadChannel,
  type OlympiadSettings,
  type OlympiadTemplate,
  type SettingsInput,
} from '@/api/services/olympiad'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { RichText } from '@/components/ui/RichText'
import { RichTextField } from '@/components/ui/RichTextField'
import { Toast } from '@/components/ui/Toast'
import { AttachmentsEditor, Badge, Confirm, ErrorBox, SectionTitle, Switch, TestSendButton } from './components/common'
import { errText } from './components/lib'

function toInput(s: OlympiadSettings): SettingsInput {
  return {
    gradeFrom: s.gradeFrom,
    gradeTo: s.gradeTo,
    defaultOnFull: s.defaultOnFull,
    rulesText: s.rulesText,
    rulesFileUrl: s.rulesFileUrl,
    rulesFileName: s.rulesFileName,
    rulesDisplay: s.rulesDisplay,
    reminder1Enabled: s.reminder1Enabled,
    reminder1Minutes: s.reminder1Minutes,
    reminder2Enabled: s.reminder2Enabled,
    reminder2Minutes: s.reminder2Minutes,
    resultShowScore: s.resultShowScore,
    resultShowRank: s.resultShowRank,
    resultShowCertificate: s.resultShowCertificate,
    resultShowInvite: s.resultShowInvite,
    inviteText: s.inviteText,
    inviteUrl: s.inviteUrl,
    certificateFormat: s.certificateFormat,
    adminLink: s.adminLink,
  }
}

/* ======================= Bot ======================= */

function BotSection({ settings, onSaved, toast }: { settings: OlympiadSettings; onSaved: (s: OlympiadSettings) => void; toast: (m: string) => void }) {
  const [token, setToken] = useState('')
  const [check, setCheck] = useState<BotCheck | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const save = async (t: string) => {
    setBusy(true)
    setError('')
    try {
      onSaved(await saveBotToken(t))
      setToken('')
      setCheck(null)
      toast(t ? 'Bot ulandi' : 'Bot uzildi')
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle title="Bot" hint="Olimpiada uchun ALOHIDA bot — @BotFather'da yangi bot yarating (maktabning asosiy boti emas)." />
      <ErrorBox message={error} />
      {settings.botConfigured ? (
        <div className="mb-3 flex flex-wrap items-center gap-3 rounded-xl bg-emerald-50 px-4 py-3 text-sm">
          <Bot className="h-5 w-5 text-emerald-600" />
          <div className="min-w-0 flex-1">
            <p className="font-medium text-slate-800">
              {settings.botName}{' '}
              <a href={`https://t.me/${settings.botUsername}`} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                @{settings.botUsername}
              </a>
            </p>
            <p className="font-mono text-xs text-slate-500">{settings.botTokenMasked}</p>
          </div>
          <Button
            variant="secondary"
            disabled={busy}
            onClick={async () => {
              setError('')
              try {
                setCheck(await checkBot())
              } catch (e) {
                setError(errText(e))
              }
            }}
          >
            Ulanishni tekshirish
          </Button>
          <Button variant="ghost" disabled={busy} onClick={() => save('')}>
            Uzish
          </Button>
        </div>
      ) : (
        <p className="mb-3 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-700">Bot ulanmagan — ro'yxatdan o'tish, eslatma va natijalar ishlamaydi.</p>
      )}
      {check && (
        <div className={'mb-3 rounded-xl px-4 py-3 text-sm ' + (check.ok ? 'bg-slate-50 text-slate-700' : 'bg-red-50 text-red-600')}>
          {check.ok ? (
            <>
              <p className="flex items-center gap-1 font-medium">
                <CheckCircle2 className="h-4 w-4 text-emerald-600" /> @{check.username} ishlayapti ({check.mode})
              </p>
              <p>
                Botdagi foydalanuvchilar: <b>{check.botUsers}</b> · Botni bloklagan: <b>{check.blocked}</b> · Kutilayotgan yangilanishlar: {check.pendingUpdates}
              </p>
            </>
          ) : (
            <p>Xato: {check.error}</p>
          )}
        </div>
      )}
      <div className="flex flex-wrap items-end gap-2">
        <div className="min-w-[16rem] flex-1">
          <Input
            label={settings.botConfigured ? 'Yangi token (almashtirish)' : 'Bot tokeni'}
            value={token}
            onChange={(e) => setToken(e.target.value)}
            placeholder="123456789:AA…"
            autoComplete="off"
          />
        </div>
        <Button disabled={busy || !token.trim()} onClick={() => save(token.trim())}>
          Saqlash
        </Button>
      </div>
      <p className="mt-2 text-xs text-slate-400">Token saqlashdan oldin Telegramda tekshiriladi va bu yerda to'liq ko'rsatilmaydi.</p>
    </Card>
  )
}

/* ======================= Kanallar ======================= */

function ChannelModal({ channel, onClose, onSaved }: { channel: OlympiadChannel | null; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState<ChannelInput>(
    channel
      ? { title: channel.title, chatRef: channel.chatRef, inviteLink: channel.inviteLink, sortOrder: channel.sortOrder, isActive: channel.isActive }
      : { title: '', chatRef: '@', inviteLink: '', sortOrder: 0, isActive: true },
  )
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  return (
    <Modal
      open
      onClose={onClose}
      title={channel ? 'Kanalni tahrirlash' : "Kanal qo'shish"}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <Button
            disabled={busy}
            onClick={async () => {
              setBusy(true)
              setError('')
              try {
                if (channel) await updateChannel(channel.id, f)
                else await addChannel(f)
                onSaved()
              } catch (e) {
                setError(errText(e, "Saqlab bo'lmadi"))
              } finally {
                setBusy(false)
              }
            }}
          >
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <ErrorBox message={error} />
        <Input label="Nomi (tugmada)" required value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder="Wunderkind rasmiy kanali" />
        <Input label="Kanal" required value={f.chatRef} onChange={(e) => setF({ ...f, chatRef: e.target.value })} placeholder="@kanal yoki -100…" />
        <Input label="Taklif havolasi (yopiq kanal uchun shart)" value={f.inviteLink} onChange={(e) => setF({ ...f, inviteLink: e.target.value })} placeholder="https://t.me/+…" />
        <Input label="Tartib" type="number" value={f.sortOrder} onChange={(e) => setF({ ...f, sortOrder: Number(e.target.value) })} />
        <Switch label="Faol" checked={f.isActive} onChange={(isActive) => setF({ ...f, isActive })} />
        <p className="text-xs text-slate-400">Botni kanalga ADMIN qilib qo'shing — aks holda obunani tekshirib bo'lmaydi va kanal o'tkazib yuboriladi.</p>
      </div>
    </Modal>
  )
}

function ChannelsSection({ toast }: { toast: (m: string) => void }) {
  const [list, setList] = useState<OlympiadChannel[] | null>(null)
  const [editing, setEditing] = useState<OlympiadChannel | 'new' | null>(null)
  const [removing, setRemoving] = useState<OlympiadChannel | null>(null)
  const [error, setError] = useState('')
  const load = useCallback(() => {
    getChannels()
      .then(setList)
      .catch((e) => {
        setList([])
        setError(errText(e))
      })
  }, [])
  useEffect(load, [load])

  return (
    <Card>
      <SectionTitle
        title="Majburiy obuna kanallari"
        hint="Obuna bo'lmaguncha bot ishlamaydi. Ro'yxat bo'sh bo'lsa — tekshiruv yo'q."
        action={
          <Button variant="secondary" onClick={() => setEditing('new')}>
            <Plus className="h-4 w-4" /> Kanal
          </Button>
        }
      />
      <ErrorBox message={error} />
      {list === null ? (
        <Loader />
      ) : list.length === 0 ? (
        <p className="text-sm text-slate-400">Kanal qo'shilmagan</p>
      ) : (
        <ul className="divide-y divide-slate-100">
          {list.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center gap-2 py-2.5 text-sm">
              <span className="font-medium text-slate-800">{c.title}</span>
              <span className="text-slate-400">{c.chatRef}</span>
              {!c.isActive && <Badge className="bg-slate-100 text-slate-400">O'chiq</Badge>}
              {c.lastError && (
                <Badge className="bg-amber-50 text-amber-700">
                  <AlertTriangle className="h-3 w-3" /> {c.lastError}
                </Badge>
              )}
              <div className="ml-auto flex gap-1">
                <button className="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700" onClick={() => setEditing(c)}>
                  <Pencil className="h-4 w-4" />
                </button>
                <button className="rounded p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600" onClick={() => setRemoving(c)}>
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <ChannelModal
          channel={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            toast('Saqlandi')
            load()
          }}
        />
      )}
      <Confirm
        open={!!removing}
        title="Kanalni o'chirish"
        danger
        confirmLabel="O'chirish"
        onClose={() => setRemoving(null)}
        onConfirm={async () => {
          const c = removing!
          setRemoving(null)
          try {
            await deleteChannel(c.id)
            load()
          } catch (e) {
            setError(errText(e))
          }
        }}
      >
        «{removing?.title}» majburiy obunadan olib tashlansinmi?
      </Confirm>
    </Card>
  )
}

/* ======================= Shablonlar ======================= */

function TemplatesSection({ toast }: { toast: (m: string) => void }) {
  const [list, setList] = useState<OlympiadTemplate[] | null>(null)
  const [editing, setEditing] = useState<OlympiadTemplate | null>(null)
  const [text, setText] = useState('')
  const [attachments, setAttachments] = useState<OlympiadAttachment[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(() => {
    getTemplates()
      .then(setList)
      .catch((e) => {
        setList([])
        setError(errText(e))
      })
  }, [])
  useEffect(load, [load])

  const save = async (value: string, media: OlympiadAttachment[], msg: string) => {
    if (!editing) return
    setBusy(true)
    setError('')
    try {
      await saveTemplate(editing.key, value, media)
      setEditing(null)
      toast(msg)
      load()
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle title="Xabar shablonlari" hint="Botning barcha standart xabarlari. Tokenlar {…} — qiymat bilan almashtiriladi." />
      <ErrorBox message={error} />
      {list === null ? (
        <Loader />
      ) : (
        <ul className="divide-y divide-slate-100">
          {list.map((t) => (
            <li key={t.key} className="flex items-start gap-3 py-3">
              <div className="min-w-0 flex-1">
                <p className="text-sm font-medium text-slate-800">
                  {t.title} {t.customized && <Badge className="ml-1 bg-brand-50 text-brand-700">tahrirlangan</Badge>}
                </p>
                <RichText markup={t.text} className="mt-1 line-clamp-2 text-xs text-slate-500" />
                {t.attachments.length > 0 && <p className="mt-1 text-xs text-slate-400">📎 Biriktirmalar: {t.attachments.length}</p>}
              </div>
              <Button
                variant="secondary"
                onClick={() => {
                  setEditing(t)
                  setText(t.text)
                  setAttachments(t.attachments)
                }}
              >
                <Pencil className="h-4 w-4" />
              </Button>
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <Modal
          open
          onClose={() => setEditing(null)}
          title={editing.title}
          size="lg"
          footer={
            <>
              <TestSendButton text={text} attachments={attachments} onDone={toast} onError={setError} />
              {editing.customized && (
                <Button variant="secondary" disabled={busy} onClick={() => save('', [], 'Standartga qaytarildi')}>
                  <RotateCcw className="h-4 w-4" /> Standartga qaytarish
                </Button>
              )}
              <Button disabled={busy || !text.trim()} onClick={() => save(text, attachments, 'Saqlandi')}>
                Saqlash
              </Button>
            </>
          }
        >
          <div className="space-y-2">
            <ErrorBox message={error} />
            <RichTextField value={text} onChange={setText} rows={8} />
            {editing.tokens && <p className="text-xs text-slate-400">Tokenlar: {editing.tokens}</p>}
            <AttachmentsEditor value={attachments} onChange={setAttachments} />
            {editing.key === 'result' && (
              <p className="text-xs text-slate-400">
                Tadbirda o'chirilgan qism (ball yoki o'rin) bo'lsa, shu token turgan qator butunlay olib tashlanadi.
              </p>
            )}
          </div>
        </Modal>
      )}
    </Card>
  )
}

/* ======================= Sahifa ======================= */

/** Olimpiada → Sozlamalar (§12). */
export default function SettingsPage() {
  const [settings, setSettings] = useState<OlympiadSettings | null>(null)
  const [f, setF] = useState<SettingsInput | null>(null)
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getSettings()
      .then((s) => {
        setSettings(s)
        setF(toInput(s))
      })
      .catch((e) => setError(errText(e, "Yuklab bo'lmadi")))
  }, [])

  if (!settings || !f) return error ? <ErrorBox message={error} /> : <Loader label="Yuklanmoqda..." />

  const set = <K extends keyof SettingsInput>(k: K, v: SettingsInput[K]) => setF({ ...f, [k]: v })

  const save = async () => {
    setBusy(true)
    setError('')
    try {
      const s = await saveSettings(f)
      setSettings(s)
      setF(toInput(s))
      setToast('Sozlamalar saqlandi')
    } catch (e) {
      setError(errText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const grades = Array.from({ length: 11 }, (_, i) => i + 1)

  return (
    <div className="space-y-5 pb-20">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Olimpiada sozlamalari</h1>
        <p className="text-sm text-slate-400">Bot, obuna, nizom, eslatma va natija xabarlari</p>
      </div>

      <BotSection settings={settings} onSaved={(s) => setSettings({ ...s })} toast={setToast} />
      <ChannelsSection toast={setToast} />

      <Card>
        <SectionTitle title="Ro'yxatdan o'tish" />
        <div className="grid gap-3 sm:grid-cols-3">
          <Select label="Sinf (dan)" value={f.gradeFrom} onChange={(e) => set('gradeFrom', Number(e.target.value))}>
            {grades.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </Select>
          <Select label="Sinf (gacha)" value={f.gradeTo} onChange={(e) => set('gradeTo', Number(e.target.value))}>
            {grades.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </Select>
          <Select label="Limit to'lsa (yangi tadbirga)" value={f.defaultOnFull} onChange={(e) => set('defaultOnFull', e.target.value as SettingsInput['defaultOnFull'])}>
            <option value="waitlist">Kutish ro'yxatiga</option>
            <option value="close">Ro'yxatni yopish</option>
          </Select>
        </div>
      </Card>

      <Card>
        <SectionTitle title="Umumiy nizom" hint={`Joriy versiya: ${settings.rulesVersion}. O'zgarsa versiya oshadi; avval tasdiqlaganlardan qayta so'ralmaydi.`} />
        <div className="space-y-3">
          <RichTextField label="Nizom matni" value={f.rulesText} onChange={(v) => set('rulesText', v)} rows={8} />
          <div className="flex flex-wrap items-center gap-2">
            {f.rulesFileUrl ? (
              <span className="inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm">
                <FileText className="h-4 w-4 text-slate-400" />
                <a href={f.rulesFileUrl} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                  {f.rulesFileName || 'Nizom.pdf'}
                </a>
                <button type="button" onClick={() => setF({ ...f, rulesFileUrl: '', rulesFileName: '' })} className="text-slate-400 hover:text-red-600">
                  <Trash2 className="h-4 w-4" />
                </button>
              </span>
            ) : (
              <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50">
                <Upload className="h-4 w-4" /> {uploading ? 'Yuklanmoqda…' : 'PDF yuklash'}
                <input
                  type="file"
                  accept="application/pdf"
                  className="hidden"
                  onChange={async (e) => {
                    const file = e.target.files?.[0]
                    if (!file) return
                    setUploading(true)
                    try {
                      const a = await uploadMedia(file, 'document')
                      setF({ ...f, rulesFileUrl: a.url, rulesFileName: a.name, rulesDisplay: f.rulesDisplay === 'text' ? 'both' : f.rulesDisplay })
                    } catch (err) {
                      setError(errText(err, "Faylni yuklab bo'lmadi"))
                    } finally {
                      setUploading(false)
                    }
                  }}
                />
              </label>
            )}
            <Select value={f.rulesDisplay} onChange={(e) => set('rulesDisplay', e.target.value as SettingsInput['rulesDisplay'])} className="w-auto">
              <option value="text">Faqat matn</option>
              <option value="pdf">Faqat PDF</option>
              <option value="both">Matn va PDF</option>
            </Select>
          </div>
        </div>
      </Card>

      <Card>
        <SectionTitle title="Standart eslatmalar" hint="Har yangi tadbirga avtomatik qo'shiladi. Mavjud tadbirlarniki tadbir sahifasida o'zgaradi." />
        <div className="space-y-3">
          {([1, 2] as const).map((n) => {
            const enabledKey = n === 1 ? 'reminder1Enabled' : 'reminder2Enabled'
            const minutesKey = n === 1 ? 'reminder1Minutes' : 'reminder2Minutes'
            return (
              <div key={n} className="flex flex-wrap items-center gap-x-6 gap-y-2">
                <div className="w-44">
                  <Switch label={`${n}-eslatma`} checked={f[enabledKey]} onChange={(v) => set(enabledKey, v)} />
                </div>
                <div className={cn('flex items-center gap-2', !f[enabledKey] && 'opacity-50')}>
                  <Input
                    aria-label={`${n}-eslatma: necha soat oldin`}
                    type="number"
                    min={0.25}
                    step={0.25}
                    disabled={!f[enabledKey]}
                    value={f[minutesKey] / 60}
                    onChange={(e) => set(minutesKey, Math.max(1, Math.round(Number(e.target.value) * 60)))}
                    className="w-24"
                  />
                  <span className="text-sm text-slate-500">soat oldin</span>
                </div>
              </div>
            )
          })}
          <p className="text-xs text-slate-400">Matnlari — «Xabar shablonlari»da (Eslatma 1 va 2).</p>
        </div>
      </Card>

      <Card>
        <SectionTitle title="Natija xabari" hint="Yangi tadbir uchun standart qiymatlar — har tadbirda alohida o'zgartiriladi." />
        <div className="grid gap-x-8 sm:grid-cols-2">
          <Switch label="Ball va foiz" checked={f.resultShowScore} onChange={(v) => set('resultShowScore', v)} />
          <Switch label="O'rin" checked={f.resultShowRank} onChange={(v) => set('resultShowRank', v)} />
          <Switch label="Sertifikat" checked={f.resultShowCertificate} onChange={(v) => set('resultShowCertificate', v)} />
          <Switch label="Maktabga taklif tugmasi" checked={f.resultShowInvite} onChange={(v) => set('resultShowInvite', v)} />
        </div>
        <div className="mt-3 grid gap-3 sm:grid-cols-3">
          <Input label="Taklif tugmasi matni" value={f.inviteText} onChange={(e) => set('inviteText', e.target.value)} />
          <Input label="Taklif havolasi" value={f.inviteUrl} onChange={(e) => set('inviteUrl', e.target.value)} placeholder="https://…" />
          <Select label="Sertifikat formati" value={f.certificateFormat} onChange={(e) => set('certificateFormat', e.target.value as SettingsInput['certificateFormat'])}>
            <option value="pdf">PDF</option>
            <option value="png">Rasm (PNG)</option>
          </Select>
        </div>
      </Card>

      <Card>
        <SectionTitle title="Aloqa" />
        <Input
          label="«Adminga savol» havolasi"
          value={f.adminLink}
          onChange={(e) => set('adminLink', e.target.value)}
          placeholder="https://t.me/username"
        />
      </Card>

      <TemplatesSection toast={setToast} />

      <div className="sticky bottom-4 z-10 flex items-center justify-end gap-3 rounded-2xl bg-white/90 p-3 shadow-lg ring-1 ring-slate-100 backdrop-blur">
        <ErrorBox message={error} />
        <Button disabled={busy} onClick={save}>
          Sozlamalarni saqlash
        </Button>
      </div>
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
