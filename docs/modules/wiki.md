# Bilimlar bazasi (shaxsiy eslatmalar)

HR → **Bilimlar bazasi** (`/admin/bilimlar`). Notion'ga o'xshash shaxsiy eslatmalar: chapda sahifalar daraxti,
o'ngda muharrir (BlockNote). Modul WunderkindLC CRM'idan ko'chirildi (mijoz, 2026-11-03; paket
`bilimlar-bazasi.zip`) va LMS'ga moslandi.

## Mijoz qarorlari (2026-11-03)

| Savol | Javob |
|---|---|
| Kim yozadi | **Barcha xodimlar + o'qituvchilar** — staff, admin, superadmin, kassir (HR menyusida) va o'qituvchi (`/teacher/eslatmalar`). Ruxsat kaliti yo'q. |
| Kim o'qiydi | **Faqat rahbariyat** — admin, superadmin va HR'da «direktor» (faol `hr_employees.hr_role = director`). Menejerlar bo'ysunuvchilarnikini KO'RMAYDI, hamkasblar bir-birinikini ko'rmaydi. |

Shuning uchun asl CRM'dagi menejer ko'rinishi (`TeamNoteOwners`), `WikiSettings` jadvali va sozlama oynasi,
`wiki.team` ruxsat kaliti **ko'chirilmadi**.

## Manzillar va API

| Qism | Klient | API |
|---|---|---|
| Eslatmalarim | `/admin/bilimlar/eslatmalar/:pageId?`, o'qituvchi: `/teacher/eslatmalar/:pageId?` | `MyNotesController` — `api/me/notes` |
| Xodimlar (faqat rahbariyat) | `/admin/bilimlar/xodimlar/:userId?/:pageId?` | `WikiTeamController` — `api/admin/wiki/team` |

- `/admin/bilimlar/*` — `/admin` daraxtidan TASHQARIDA (`ProtectedRoute roles=[staff, admin, superadmin, cashier]`),
  xuddi «Mening topshiriqlarim» kabi: `/admin` darvozasi kassirni kiritmaydi.
- `/teacher/eslatmalar` — asosiy SPA sahifasi; `Program.cs` `MapFallback` da «Dars tahlilim» bilan bir xil istisno.
- `GET team/members` rahbariyat bo'lmaganga `{ isLeader: false, members: [] }` qaytaradi — «Xodimlar» tabi chiqmaydi.
  Qolgan `team/*` endpointlari rahbariyat bo'lmaganga **404** (eslatma borligi ham oshkor bo'lmasin).
- `MyNotesController`: o'quvchi/ota-ona — 403; begona eslatma — 404.

## Qoidalar (asl CRM'dagidek)

- **Faqat matn.** Bloklar: paragraph, heading, quote, 4 xil ro'yxat, codeBlock, table, divider. Rasm/video/fayl yo'q —
  klient `wikiModel.ts` (`WIKI_BLOCK_TYPES`) va server `WikiRules.AllowedBlockTypes` AYNAN bir xil bo'lishi shart.
- **Havola** faqat `http(s):`, `mailto:`, `tel:`. Sahifa ≤ 1 MB, ichma-ichlik ≤ 32, bir odamda ≤ 2000 eslatma.
- **Avtosaqlash** 1,2 s; eski versiya ustidan saqlash — **409** (avtosaqlash to'xtaydi, «Sahifani yangilash»).
- **Qulf** — faqat rahbariyat; pastga meros. Egasi qulflangan joyda saqlay, qo'sha, ko'chira, o'chira olmaydi (403).
- **Bo'limlar** (papkalar) — `IsSection = true`, faqat yuqori darajada.
- **Tarixga yozilmaydi** — sarlavhalar shaxsiy.
- Muharrir `lazy()` bilan alohida chunk. **BlockNote 0.51.4 ga qat'iy bog'langan** (`^` siz): 0.52+ `npm install` ni
  ERESOLVE bilan yiqitadi. Mantine'ning global uslublari olinmaydi.

## Kod

- Domain: `SchoolLms.Domain/Wiki.cs` (`WikiPage`, jadval `wiki_pages`, migratsiya `AddWikiNotes`).
- Application: `SchoolLms.Application/Wiki/` — `WikiRules` (sof qoidalar), `WikiPageOps` (yaratish/saqlash/ko'chirish),
  `WikiAccess` (rahbariyatmi), `WikiDtos`.
- Server: `MyNotesController`, `WikiTeamController`.
- Klient: `api/services/wiki.ts`, `pages/admin/wiki/` (`NotesPage` — ikkala tab, `components/`, `lib/`).
- Testlar: `WikiRulesTests` (sof qoidalar), `WikiApiTests` (HTTP: rollar, 404, qulf, 409, faqat matn, no-store).
