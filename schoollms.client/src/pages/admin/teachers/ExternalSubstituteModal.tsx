import { useEffect, useState } from 'react'
import type { Subject } from '@/types'
import { getSubjects } from '@/api/services/subjects'
import { createExternalSubstitute, getExternalSubstitutes, type ExternalSubstitute } from '@/api/services/substitutions'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { localDigits } from '@/lib/phone'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'

/**
 * Tashqi o'rindosh kartasi (client, 2026-11-11): someone from outside the CRM covers a lesson. No
 * login, never in the teachers list or the timetable. The phone is checked as it is typed — a card
 * that already exists is offered instead of being typed twice.
 */
export function ExternalSubstituteModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean
  onClose: () => void
  onSaved: (substitute: ExternalSubstitute, existed: boolean) => void
}) {
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [fullName, setFullName] = useState('')
  const [phone, setPhone] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [found, setFound] = useState<{ digits: string; substitute: ExternalSubstitute | null } | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    getSubjects(undefined, true)
      .then((rows) => setSubjects([...rows].sort((a, b) => a.name.localeCompare(b.name))))
      .catch(() => setSubjects([]))
  }, [open])

  // Takrorlanish: to'liq raqam kiritilishi bilan shu telefonli karta qidiriladi.
  const digits = localDigits(phone)
  useEffect(() => {
    if (digits.length !== 9) return
    let alive = true
    getExternalSubstitutes(digits)
      .then((rows) => alive && setFound({ digits, substitute: rows.find((r) => localDigits(r.phone) === digits) ?? null }))
      .catch(() => alive && setFound(null))
    return () => {
      alive = false
    }
  }, [digits])
  const match = found?.digits === digits ? found.substitute : null

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      const res = await createExternalSubstitute({
        fullName: fullName.trim(),
        phone,
        subjectId: subjectId || null,
      })
      onSaved(res.substitute, res.existed)
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const valid = fullName.trim().length >= 3 && digits.length === 9

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title="Tashqi o'rindosh ustoz"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          {match ? (
            <Button onClick={() => onSaved(match, true)}>Shu ustozni tanlash</Button>
          ) : (
            <Button onClick={save} disabled={saving || !valid}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-xs text-slate-400">
          CRM'da yo'q, tashqaridan kelgan ustoz uchun. Unga login berilmaydi, ustozlar ro'yxati va dars jadvalida ko'rinmaydi.
        </p>
        <PhoneInput label="Telefon" required value={phone} onChange={setPhone} autoFocus />
        {match ? (
          <div className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            Bu raqam bilan ustoz allaqachon bor: <b>{match.fullName}</b>
            {match.subjectName && <> · {match.subjectName}</>}. Qayta kiritish shart emas — tanlang.
          </div>
        ) : (
          <>
            <Input label="F.I.Sh." required value={fullName} maxLength={200} onChange={(e) => setFullName(e.target.value)} />
            <Select label="Fani" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">— tanlanmagan —</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
