# Dars jadvalini boshqarish — xonalar, tekshiruv, yuklama va avtomatik tuzish

Mijoz, 2026-11-20/21: *"dars jadvali bilan bog'liq nima narsalar bo'lsa shuni boshqarish imkoniyatini
yarata olamizmi"* — 1–7 bandlar. Qoidalar bo'yicha qaror: *"2 va 3, keyin yana qoidalar kiritamiz"*.

## Nima qayerda

| Band | Ekran | Server |
|---|---|---|
| 1. Xona | Katak oynasida "Xona" (bo'sh — sinfning o'z xonasi); katakda xona nomi | `schedule_lesson.room_id`; `ScheduleTemplatesController.RoomErrorAsync`; `GET /api/admin/schedule/rooms` |
| 2. Ish reja soatlari | Umumiy jadval → "Tekshiruv" → "Ish reja soatiga mos emas" (farq ≥ 1) | `GET /api/admin/schedule/curriculum-norms` (`CurriculumService.Norms.cs`) |
| 3. Tekshiruv | Umumiy jadval → "Tekshiruv" paneli (`ScheduleChecks.tsx`) | brauzerda hisoblanadi |
| 4. Nomini o'zgartirish / o'chirish | Umumiy jadval → ✏️ / 🗑 (faol jadval o'chirilmaydi) | mavjud shablon endpointlari |
| 5. Ortga qaytarish | "Ortga" tugmasi, Ctrl+Z (oxirgi 30 ta saqlash) | oddiy `live-cells` saqlash |
| 6. Excel / chop etish | "Excel", "Chop etish" (`/admin/schedule/print?name=`) | `GET /api/admin/schedule/export?name=` (`TimetableExport`) |
| 7. Avtomatik tuzish | `/admin/schedule/builder` — Yuklama, O'qituvchilar bandligi, Avtomatik tuzish | `TimetableBuilderController`, `TeachingLoadService`, `TimetableSolver` |

## Xona qoidasi

Darsning amaldagi xonasi = `room_id`, bo'lmasa (butun sinf darsida) sinfning o'z xonasi —
`SchoolClass.Room` matni reyestrdagi xona nomiga teng bo'lsa (`BlockScheduleService` 5-qoidasi bilan bir xil).
Ikki sinf bitta xonani ko'rsatsa, ular uchun o'z xonasi hisobga olinmaydi. Xona bandligi — ogohlantirish
(Tekshiruv paneli, katak oynasi), saqlashni to'xtatmaydi; serverda faqat xona mavjud va faolligi tekshiriladi.

## Avtomatik tuzish

* **Yuklama** (`teaching_loads`): ega × fan × yarmi (0 — butun, 1/2 — yarmlari) → soat/hafta, ustoz, maxsus xona.
  "Joriy jadvaldan to'ldirish" — joriy haftadagi jadvaldan sanaydi (blok darslari olinmaydi).
* **Bandlik** (`teacher_unavailability`): kun (soat 0 — butun kun) yoki aniq soat.
* **O'quv kunlari** (`owner_study_days`): har sinf/guruh 5 yoki 6 kun; saqlanmagan bo'lsa sukut — 1–3-sinf 5 kun, qolganlari 6 kun (mijoz, 2026-11-21).
* **Natija** — har egaga yangi nomli QORALAMA shablon; haftaga biriktirilmaydi, "Nashr qilish" kerak.
* **Qattiq qoidalar:** egada bir soatda bitta dars (yarmlari bir soatda, turli ustoz/xona), ustoz va xona
  bir vaqtda bitta joyda (qo'ng'iroq jadvallari har xil bo'lsa — vaqt kesishuvi), ustoz bandligi,
  blok soatlari va generatsiyaga kirmagan egalarning joriy jadvali band.
* **Yumshoq:** oynasiz (yoqilsa), bir fan kuniga ≤ ⌈soat/kunlar⌉, kunlar bo'yicha teng, ustoz oynalari kam.

### Yangi qoida qo'shish

1. `GenerateScheduleRequest` ga maydon (sukut — bugungi xatti-harakat).
2. `SolverInput` ga uzatish (`TeachingLoadService.GenerateAsync`).
3. Qattiq bo'lsa — `TimetableSolver.Attempt.Fits`; yumshoq bo'lsa — `Score` ga jarima.
4. `TimetableBuilderPage` → "Qoidalar" ro'yxatiga belgi; `TimetableSolverTests` ga test.
