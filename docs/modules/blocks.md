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

## Bosqichlar
1. Baza ✅ — 2. Backend (servislar, validatsiya, "o'quvchining shu sanadagi guruhi", API) — 3. Admin UI —
4. Jadval — 5. Ustoz ekranlari — 6. Hisobotlar va ota-ona API — 7. Migratsiya skripti (`--dry-run`).
