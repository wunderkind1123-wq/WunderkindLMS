# Arxivlash sabablari guruhlari, arxiv doskasi, lidlarga qaytarish

Status: built 2026-11-13. Client request: *"maktabdan ketgan o'quvchilarni arxivga o'tkazamiz. Endi u yerda
kim nima uchun ketgan yoki biz ketkizganimizni aniqlash qiyin bo'lyapti … lidlarga o'xshab ustun qilsakmikan"*.
Follow-ups: groups and columns are the school's to add, edit and delete; columns and "return to leads"
are both wanted.

## Why

The reason catalog (§2.2) was a flat list and the catalog reason was **optional** at archiving — only the
free-text note was required. Many pupils were archived without one ("Boshqa" in the report), and there
was no way to see *who decided*: the family, or the school.

## Data

| Table / column | What |
|---|---|
| `student_archive_reason_groups` (id, name unique, color, position, is_active) | A group = one board column. Colors: the lead-stage palette (`slate blue emerald amber violet rose cyan orange`). |
| `student_archive_reasons.group_id` → groups, **RESTRICT** | Each reason sits in at most one group; a group with reasons cannot be deleted. |
| `leads.from_student_id` (no FK) + `source = 'archive'` | A lead made from an archived pupil. `ck_leads_source_student`: `(source = 'archive') = (from_student_id is not null)`. |
| `lead_conversions.source` | Now also `archive`. |

Seed (`archive_reason_groups_seed.sql`, idempotent): **O'zi ketdi** (rose), **Maktab chiqardi** (amber),
**Tabiiy sabab** (slate). Seeded reasons are put into them only while still ungrouped: other school,
family, payment → O'zi ketdi; expelled → Maktab chiqardi; graduated, moved city → Tabiiy sabab.
"Boshqa" stays ungrouped on purpose.

## Rules

- **The catalog reason is required** on every manual archive: one pupil, several, a class with pupils
  in it (an empty class needs none). 400 `"Sabab turini tanlang"`. The note stays required as before.
- The group is never picked by hand: it follows from the reason. Moving a reason to another group moves
  everyone who left with it.
- A reason without a group, or no reason at all (old rows) → board column **"Sababi aniqlanmagan"**
  (system column, first, cannot be dropped into). Since 2026-11-17 it is shown **only while it has
  pupils** — the client sorted everyone out and wanted it gone; a pupil archived with an ungrouped
  reason brings it back, so nobody disappears from the board.
- An inactive group keeps its column only while pupils are in it ("(faol emas)").

## API

| | |
|---|---|
| `GET/POST /api/admin/archive-reason-groups`, `PUT/DELETE …/{id}`, `PUT …/order` | Groups. `settings` key; GET open to staff. |
| `POST/PUT /api/admin/archive-reasons` | Now take `groupId` (always written; null = ungrouped). |
| `GET /api/admin/students/archive-board` | Columns with cards: name, class at departure (latest `left` event), date, reason, note, parent phone, open lead. `students` or `students:view`. |
| `PUT /api/admin/students/{id}/archive-reason` `{ reasonId, note? }` | Pupil card **and** latest departure event change together; audited (`archive-reason`). Full `students` key. |
| `POST /api/admin/students/{id}/to-lead` `{ note? }` | Lead in the first board column, note = left on / class / reason / note. One open lead per pupil (409). Staff also need `leads`. |

## Lead loop

1. Archive card → **Lidga** → lead appears on the Leads board (source "Arxivdan qaytarilgan" in the funnel).
2. The pupil stays archived. Enrolling that lead as a **new** pupil is refused (409) — it would split
   debts, history and attendance across two cards.
3. **Arxivdan qaytarish** on the pupil closes the lead (deleted, counted in `lead_conversions` as
   `archive`). The design-frozen board files are untouched.

## Screens

- O'quvchilar → Arxiv: **Jadval | Ustunlar** switch (remembered per browser). Columns: drag a card to
  another column → pick the exact reason of that group (+ optional note). Reason chips in a column filter
  it. Card buttons: Sabab, Lidga, Qaytarish.
- Sozlamalar → Arxivlash sabablari: **Guruhlar** (add, edit name/colour/active, up/down, delete when
  empty) above the reasons table, which gains a Guruh column and a group picker.
- Archive dialogs (pupil, several, class): one required select, reasons grouped by `<optgroup>`.
- O'quvchilar oqimi: a stacked bar of departures per group above the reason bars; Excel gets a
  "Guruhlar" sheet.

Code: `ArchiveReasonGroupsController`, `StudentArchiveBoardController`, `StudentFlowReport` (by group),
`pages/admin/students/ArchiveBoard.tsx`, `ArchiveReasonSelect.tsx`, `settings/ArchiveReasonsSettings.tsx`.
Tests: `ArchiveReasonGroupsTests`.
