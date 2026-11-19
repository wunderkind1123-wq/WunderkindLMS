# Bloklar — birlashgan va daraja guruhlari — 2026-11-18

Manba: `docs/TZ_Bloklar_Daraja_guruhlari.docx` (akademik direktor, 18.11.2026). Ingliz tili va
matematikada bir nechta sinf/yo'nalish bir soatda birlashadi va 2 ta guruhga bo'linadi; har guruhning
o'z ustozi, davomati va bahosi bor.

## Mijoz qarorlari (2026-11-18)

1. 3-sinf — hozir bitta sinf; blok shu sinf ichida Katta / Kichikka bo'linadi.
2. Yo'nalishli bloklarda (Fil+Ijt, Iqt, Aniq+Iqt) baho qo'yilmaydi — **oraliq test** davom etadi.
   4-sinf va 3-sinf bloklarida jurnal bahosi qo'yiladi.
3. Kun naqshi (toq — ingliz tili, juft — matematika) — **rad etish**: naqshga zid katak saqlanmaydi.
   Istisno kerak bo'lsa blok `custom` naqshga o'tkaziladi.
4. Prod bazaga ulanilmaydi; hammasi lokal / test ma'lumotida tekshiriladi.
5. Ustozlarning ikkinchi profillarini birlashtirish — faqat admin bergan "asosiy ← ikkinchi" ro'yxati
   bo'yicha, avval `--dry-run`, bir tranzaksiyada, ikkinchi profil arxivlanadi (o'chirilmaydi).
6. Bo'linish faqat ingliz tili va matematikada bloklar orqali. Boshqa fanlardagi `students.sub_group`
   (Katta/Kichik) o'zgarishsiz qoladi.
7. Blok guruhlari mavjud `study_groups` ("guruh darsi") mexanizmi ustiga quriladi.
8. Ota-onaga guruh nomi ko'rsatilmaydi — API javobida ham yo'q; faqat ustoz ismi.

## Ma'lumotlar modeli (1-bosqich, migratsiya `Blocks`)

TZ 5-bo'limining loyihaga moslashtirilgan varianti:

| TZ | Loyihada | Nega |
|---|---|---|
| `blocks.academic_year_id` | `blocks.academic_year` (`"2026-2027"`) | O'quv yili jadvali yo'q |
| `block_classes.class_id` | `owner_kind` + `owner_id` | Filologiya, Iqtisodiyot-1 ... — yo'nalish guruhlari, sinf emas |
| `block_groups.teacher_id` | `block_group_teachers` (valid_from / valid_to) | Ustoz almashganda eski baholar eski ustozda qoladi |
| `block_groups` | + `study_group_id` (unique) | Jurnal, davomat, ro'yxat, konflikt — mavjud guruh darsi mexanizmi |
| `block_groups.base_class_id` | `base_owner_kind` + `base_owner_id` | Manba yo'nalish ham bo'lishi mumkin |
| `block_group_members` | `study_group_members` + `source_class_id`, `join_reason` | joined_on/left_on = valid_from/valid_to; created_by = moved_by |
| `lessons.block_id`, `class_id` bo'sh | `schedule_lesson.block_id`; katak har manba sinf shablonida | Katak har doim shablonga (sinf/yo'nalishga) tegishli |
| `attendance`, `grades` | `journal_entries.block_group_id`, `source_class_id` | Davomat va baho bitta jadvalda |
| `gamification_points` | `discipline_points.block_group_id`, `source_class_id` | |
| — | `lesson_notes.block_group_id`, `quarter_grades.block_group_id/source_class_id` | Mavzu va chorak bahosi |
| — | `interim_tests.block_group_id`, `interim_test_results.source_class_id` | Yo'nalishli bloklar oraliq test oladi |

`source_class_id` — sinf id'si yoki yo'nalish guruhi id'si (ikkalasi uuid matn).

### Baza kafolatlari
- `ck_blocks_block_type` (`level` | `base_class`), `ck_blocks_day_pattern` (`odd` | `even` | `custom`).
- Blokda ko'pi bilan 2 ta guruh: `sort_order in (1, 2)` + `ux_block_groups_block_sort`. "Aniq 2 ta" — xizmat.
- `ux_block_classes_block_owner` — manba sinf blokda takrorlanmaydi.
- `ux_block_group_teachers_one_current` — guruhda bitta joriy ustoz.
- `ck_*_block_group` — `block_group_id` faqat `owner_kind = 'group'` qatorida.
- Baho/davomat yozilgan blok guruhi o'chirilmaydi (FK RESTRICT); blokdagi dars katagi bor blok ham.
- "O'quvchi bitta blokda bitta guruhda" — mavjud `ux_group_members_one_group_per_subject` (bitta fandan
  bitta faol guruh) + sanalar kesishmasligini xizmat tekshiradi.

Yangi blok **faol emas** (`is_active = false`): admin taqsimlab bo'lgach faollashtiradi (TZ 7-bo'lim, 2-qoida).

Mavjud ma'lumot: hamma yangi ustun nullable, DEFAULT'siz — mavjud qatorlar NULL o'qiydi, birortasi
yozilmaydi. `Down()` yangi jadval va ustunlarni olib tashlaydi, eski qatorlar joyida qoladi.

Testlar: `Migrations/BlocksMigrationTests`.

## Backend (2-bosqich) — `BlockService`, `BlocksController` (`api/admin/blocks`, ruxsat `classes`)

| Amal | Endpoint | Qoida |
|---|---|---|
| Ro'yxat, kartochka | `GET /`, `GET /{id}` | manbalar, guruhlar, joriy ustoz, o'quvchilar va taqsimlanmaganlar soni |
| Yaratish / tahrir | `POST /`, `PUT /{id}` | 6, 7-qoidalar; fan va tur o'zgarmaydi; manba bir fandan bitta blokda |
| Faollashtirish | `POST /{id}/activate` | 2- va 6-qoida; to'siq bo'lsa 422 + xatolar ro'yxati |
| Ogohlantirish | `GET /warnings` | faol bloklarda taqsimlanmaganlar (kunlik ogohlantirish uchun) |
| Taqsimlash ekrani | `GET /{id}/roster?date=` | ikki guruh + "Taqsimlanmagan", har o'quvchi manbasi bilan |
| Taqsimlash | `POST /{id}/assign` | 1- va 8-qoida |
| Ko'chirish | `POST /{id}/move` (sana, sabab) | eski a'zolik D-1 da yopiladi, yangisi D dan |
| Chiqarish | `POST /{id}/unassign` | D dan boshlab guruhda emas |
| Avto-taqsimlash | `POST /{id}/auto-assign` | manba sinf bo'yicha blok; yaratish va faollashtirishda o'zi ham ishlaydi |
| Ustoz almashtirish | `POST /groups/{gid}/teacher`, `GET /groups/{gid}/teachers` | eski ustoz D-1 gacha, yangisi D dan |
| Shu sanadagi guruh | `GET /{id}/students/{sid}/group?date=` | TZ 5.3; guruhda bo'lmasa 204 |
| Guruhlar tarixi | `GET /students/{sid}/history` | qachon, qaysi guruh, sabab, kim |
| Ko'chirishlar hisoboti | `GET /moves?from=&to=` | davrda bir guruhdan ikkinchisiga o'tganlar |

Sana qoidalari:
- A'zolik va ustoz chegaralari ikkala tomondan ham kiradi, shuning uchun bir kunda ikki guruh yo'q.
- Qo'shilgan kunning o'zida ko'chirish — xatoni tuzatish: qator joyida yangilanadi, tarixda ortiqcha qator qolmaydi.
- Eski guruhda D dan keyin baho yoki davomat bo'lsa, D dan ko'chirish (yoki ustoz almashtirish) rad etiladi.

Blokning o'quv guruhini oddiy "Guruhlar" ekranidan o'zgartirib bo'lmaydi (o'qish ochiq). Blok o'chirish faqat faol
bo'lmagan va hech qanday dars, baho yoki davomat yozilmagan blok uchun ishlaydi.

4-bosqichga qoldirilganlar:
- Blok guruhi darslarining `group_lessons_enabled` ga qaramasdan "tirik" bo'lishi.
- 3, 4, 5 va 9-qoidalar (4-qoida ustoz almashtirilganda ham tekshiriladi).
- Excel import — 3-bosqichda qilindi.

Testlar: `BlockTests` (21 ta).

## Admin UI (3-bosqich) — `schoollms.client/src/pages/admin/blocks`

Menyu: O'quv bo'limi → **Bloklar** (ruxsat `classes`).

| Ekran | Manzil | Nima qiladi |
|---|---|---|
| Ro'yxat | `/admin/blocks` | nomi, fan, sinflar, guruhlar va ustozlar, o'quvchilar soni, holat; faollashtirish, o'chirish |
| Forma | `/admin/blocks/new`, `/admin/blocks/:id` | fan → manbalar → tur → 2 guruh (nom, ustoz, sinf, xona); fan va tur tahrirda qulf |
| Taqsimlash | `/admin/blocks/:id/taqsimlash` | ikki ustun + "Taqsimlanmagan", sudrab o'tkazish va belgilab yuborish, ko'chirish oynasi |
| Ko'chirishlar | `/admin/blocks/kochirishlar` | davrda guruhdan guruhga o'tganlar (standart — oxirgi 7 kun) |
| O'quvchi profili | "Sinf va guruhlar" tab'i | "Blok guruhlari tarixi" kartasi; blok guruhlari oddiy guruhlar ro'yxatidan ajratilgan |

Ustoz almashtirish oynasi: yangi ustoz + sana + tarix. Excel import (`POST /api/admin/blocks/import`) —
ustunlar `O'quvchi ID, Blok, Guruh`. Hammasi yoki hech narsa: xato qator bo'lsa, hech narsa saqlanmaydi va
xatolar qator raqami bilan qaytadi. Shablon — blokning "Excel" tugmasi (`GET /{id}/export`).

## Jadval (4-bosqich) — `BlockScheduleService`

**Blok darsi qayerda turadi (1-bosqichdagi rejadan o'zgardi).** Avval "katak har manba sinf shablonida"
deb yozilgan edi. Lekin darslarni o'qiydigan ~45 joy (jurnal, davomat, maosh, hisobotlar) bunday katakni
sinfning 1/2-yarmi deb tushunardi: ro'yxat `students.sub_group` dan chiqib, noto'g'ri bo'lardi, ustoz
darsi esa har manba sinf uchun alohida sanalardi. Shuning uchun blok darsi **har blok guruhining o'quv
guruhi shablonida** turadi (owner_kind = 'group', `block_id` to'ldirilgan, fani — blok fani,
o'qituvchisi — guruh ustozi). Ikkala guruh shabloni bir xil kataklarga ega va ularni faqat
`BlockScheduleService` yozadi. Natijada:
- ro'yxat guruh a'zoligidan olinadi;
- ustoz darsi bir marta sanaladi;
- jurnal qatori guruhga yoziladi.

Manba sinf qatorida blok faqat ko'rinadi: `GET /api/admin/blocks/schedule-overlay`, sariq chegara.

| Narsa | Qanday |
|---|---|
| Tiriklik | Faol blokning guruhlari o'chirgichga qaramaydi: `LessonRoster.LiveBlockGroupIdsAsync`, `GroupLessonScope.LiveGroupIds` (avval `TrackIds`). Faol blok yo'q bo'lsa — bugungidek |
| Haftalar | Blok guruhi shabloni joriy haftadan barcha haftalarga avtomatik biriktiriladi; o'zgarish `LiveScheduleEdit` bilan (o'tgan haftalar nusxada qoladi, `block_id` nusxaga ko'chadi) |
| 9-qoida | Naqshga zid kun — 400, rad etish |
| 3-qoida | Manba shu soatda boshqa darsda yoki boshqa blokda — 409. Teskari: oddiy muharrir manbaga blok soatida dars qo'ymaydi (`ClashForOwnerAsync`; `ScheduleTemplatesController`, `WeekAssignmentsController`) |
| 4-qoida | Ustoz shu soatda boshqa joyda, yoki bitta ustoz ikkala guruhda — 409. Ustoz almashtirishda ham tekshiriladi |
| 5-qoida | Guruh xonasi boshqa blok guruhida yoki shu xonali sinfda band — 409 |
| Blok guruhi shabloni | Oddiy jadval muharriri uni o'zgartira olmaydi — "Bloklar" bo'limidan |
| Ustoz almashtirish | Jadvaldagi o'qituvchi joriy haftadan yangilanadi; baho qaysi ustozniki ekanini sanali `block_group_teachers` hal qiladi |

Faqat joriy va keyingi haftalarga biriktirilgan shablonlar "band" qiladi — `ScheduleConflicts` qoidasi
bilan bir xil.

API: `GET/PUT /{id}/schedule` (add/remove), `POST /{id}/schedule/pattern` (toq/juft kunlarga qo'llash),
`GET /schedule-overlay`. UI: `/admin/blocks/:id/jadval`; jadval muharririda blok katagi sariq chegara
bilan, bo'sh katak oynasida "Yoki fan o'rniga blok".

Testlar: `BlockScheduleTests` (13 ta, har biri o'z bazasida).

## Ustoz ekranlari (5-bosqich)

| Narsa | Qanday |
|---|---|
| Belgi | Blok darsining jurnal qatori, mavzusi, chorak bahosi, uy vazifasi bali va oraliq test natijasi `block_group_id` + `source_class_id` (o'quvchining shu sanadagi manbasi) oladi. Bitta joyda — `AppDbContext.SaveChangesAsync` → `BlockStamping` (har yozish yo'liga alohida qo'shilmaydi). Sinf va oddiy guruh qatoriga tegmaydi; qo'yilgan belgi qayta yozilmaydi |
| Ustoz qo'ygan ball | Mini App ball oynasi darsning egasini (`ownerId`) yuboradi — blok guruhida ball blok guruhi va manba sinfini oladi |
| Yo'nalishli blok | Manbalaridan biri yo'nalish bo'lsa jurnal bahosi va chorak bahosi rad etiladi (`BlockJournal.NoGradesMessage`), davomat/uy vazifasi — yoziladi. Oraliq test shu guruhlarda olinadi (`InterimTestService`). 3- va 4-sinf bloklarida baho odatdagidek |
| Nomlar | Ustoz ro'yxatlari va haftasi: "Ingliz tili, Katta (Fil+Ijt Ingliz)" (`BlockJournal.LabelsAsync`); `TeacherLessonDto.NoGrades` — Mini App baho tugmalarini yashiradi |
| Manba sinf | Ustoz va admin jurnal ro'yxatida blok guruhi o'quvchisining `className` — manba sinfi/yo'nalishi; ekranlar uni ism yonida ko'rsatadi |
| Ro'yxat | Guruh a'zoligidan, sanaga qarab (ko'chirilgan kundan yangi ustozda) — `LessonRoster` (4-bosqich) |

Testlar: `BlockTeacherTests` (7 ta).

Tuzatish (4-bosqich): MCP o'qish roli `app_ro` blok jadvallarini o'qiy olmasdi — `LessonRoster` ularni o'qigani
uchun ro'yxatga tayanadigan MCP vositalari yiqilardi. `mcp_guards.sql` ro'yxatiga qo'shildi, migratsiya
`BlocksMcpGrants`.

## Hisobotlar va ota-ona (6-bosqich)

| Narsa | Qanday |
|---|---|
| Ota-ona | Guruh va blok nomi API javobida ham yo'q: jadval, jurnal va bosh sahifada blok guruhi darsining `ownerName` — null; oraliq testda guruh o'rnida manba yo'nalishi. Fan va ustoz ismi ko'rinadi. `BlockParentPrivacyTests` barcha GET endpointlarni tekshiradi |
| Sinf jurnali | Sinf (yoki yo'nalish) blok fanining jurnalida blok darslari ustuni ("Blok", faqat ko'rish) va o'quvchilarning blok baholari — "K"/"Kch" (manba sinf bo'yicha blokda guruh nomi) va o'sha kungi ustoz. Bu katakka yozish rad etiladi (`JournalService.BlockColumnMessage`) |
| 17:00 hisoboti | "Blok guruhlari — ustozlar kesimida" jadvali: ustoz × guruh, darslar, baholar, ball. Sinf kesimida blok bahosi manba sinfga |
| Haftalik ota-ona hisoboti | Blok baholari ClassAttainment orqali o'zi kiradi, guruh nomisiz |
| Blok hisoboti | `GET /api/admin/blocks/report?from&to` (standart — 7 kun): guruh × ustoz va manba sinf kesimi; `GET /{id}/monthly?year&month`: oylik test guruh va sinf bo'yicha, balldan pastga. UI: `/admin/blocks/hisobot` |

Testlar: `BlockParentPrivacyTests` (1), `BlockReportTests` (3).

## Bosqichlar
1. Baza ✅ — 2. Backend (servislar, validatsiya, "o'quvchining shu sanadagi guruhi", API) ✅ — 3. Admin UI ✅ —
4. Jadval ✅ — 5. Ustoz ekranlari ✅ — 6. Hisobotlar va ota-ona API ✅ — 7. Migratsiya skripti (`--dry-run`).
