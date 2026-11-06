# O'quv reja, ichki monitoring, sertifikat ballari, muddatli topshiriqlar — SPEC

Client request 2026-11-06 (four items). Every decision below was asked and answered on
2026-11-06; nothing here is our own guess unless marked **Our decision**.

## 1. Ish reja (`curriculum`)

*"O'quvchilarning ish rejasi — to'liq, ustoz aynan darsda ishlatadigan holatda … qaysi mavzu
o'tildi yo endi o'tiladi bilish uchun zarur. Buni yuklash mumkin bo'lsin … namuna ham."*

| Question | Answer |
|---|---|
| What a plan belongs to | **Subject + grade** (one plan for all 5th grades' Maths). Track groups: **track group + subject** — a track group's 9–11 pupils sit the same lesson, so no grade |
| Who uploads | **O'quv bo'limi only** (permission `curriculum`) |
| "Passed" mark | **On the plan page**, not in the journal. **Teacher and o'quv bo'limi** both mark; marks are **per class / per group** (5-A and 5-B move at their own pace) |
| Format | **Excel** (parsed into topics) **+ the original file kept** (Word/PDF/Excel, any) |
| Reports | passed / remaining %, ahead/behind plan; finish-date forecast at current pace; subjects without a plan; per-quarter planned vs passed |

Excel template columns (row 1 = header, data from row 2):
`№ | Chorak | Hafta | Bo'lim (modul) | Mavzu | Soat | Maqsad / kutilgan natija | Uy vazifasi`.
Only `Mavzu` is required; `Chorak` 1–4; `Soat` ≥ 1 (default 1).

Tables: `curriculum_plans` (id, subject_id, grade null | group_id null, title, file_url,
file_name, academic_year, created_by, timestamps; one active plan per subject+grade / subject+group),
`curriculum_topics` (plan_id, order, quarter, week, section, title, hours, goal, homework),
`curriculum_progress` (topic_id, owner_kind class|group, owner_id, passed_on, marked_by;
unique topic+owner).

A teacher sees and marks the plans of the class/group + subject pairs they teach (schedule). Teachers mark
from the **web panel and the Telegram Mini App** (asked 2026-11-06).

## 2. Ichki monitoring (`monitoring`)

*"Yo'nalish guruhlardan blok testdan tashqari ichki monitoring test olinadi … oddiy 30–100 test
yoki milliy sertifikat imtihonlariga o'xshab. 1–8 sinf o'tilgan mavzular asosida. Faqat o'quv
bo'limi tomonidan."*

| Question | Answer |
|---|---|
| Delivery | **Both**, chosen per test: paper (answers entered) or online (pupil solves, auto-checked) |
| Scoring | **Simple** (correct / %) **and national-certificate style**; for the latter the method is chosen per test: **real Rasch** (difficulty estimated from this test's answers, 0–75 scale) **or manual item weights** |
| Topics | Questions linked to curriculum topics; **per-topic analysis** in results |
| Paper entry | **Per-question answers** (answer key + each pupil's "ABDC…" string) |
| Question source | **A separate monitoring bank** (not the block-test banks) |
| Who sees results | **O'quv bo'limi only** — not teachers, not parents |
| Menu | O'quv bo'limi → new column **"O'QUV REJA VA NAZORAT"** |
| Online access | **A one-time personal code** per pupil on a public page (no pupil account), device lock like the admission test |
| Rasch score | **T = 50 + 10·Z**: θ by joint maximum likelihood on this test's answers, Z standardised among its takers, clamped to 0–75 |

National-certificate grade bands (0–75 scale): A+ ≥ 70, A ≥ 65, B+ ≥ 60, B ≥ 55, C+ ≥ 50,
C ≥ 46, below — no grade.

## 3. Sertifikat ballari

Already built (2026-10-31): `certificate_types.exam_credits` + block-test credit.

| Question | Answer |
|---|---|
| "Worth how many points" | **Block-test equivalent**: credit % × the subject's ceiling in the pupil's block |
| Where 1st / 2nd subject comes from | **The pupil's track** — a track group gets `subject1_id`, `subject2_id` |
| Ceilings | 1st subject 93 (30 × 3.1), 2nd 63 (30 × 2.1), any other subject = mandatory, 11 (10 × 1.1) |
| Views | rating tab (pupil, class/track, valid count, total points), pupil profile total, points beside each certificate |
| Expired | filter, **default: valid only** |

## 4. Xodim topshiriqlari (HR tasks)

| Question | Answer |
|---|---|
| Recurring for oneself | **Every employee** creates/edits/pauses/deletes own recurring rules in "Mening topshiriqlarim" |
| Long tasks | **Start date + due**: visible from the start day (default today) until done |
| Editing | the **giving manager** edits own given tasks; an employee edits own self tasks; **only before the due time** |
| Reminders for long tasks | **only at the start and the end**: assignment message, then 1 day and 2 hours before due; the morning list shows a long task on its start day, from the day before due, and when overdue |

A "long" task = start date earlier than the due's local date. Tasks created before this change
have start = due date, so nothing about them changes.

## 5. Built (2026-11-06)

| Part | Where |
|---|---|
| Tables | migration `CurriculumMonitoring`: `curriculum_plans/_topics/_progress`, `monitoring_banks/_questions/_tests/_items/_participants`, `study_groups.block_subject1_id/_2_id`. New tables → **rerun `deploy/init-roles.sql`** on deploy |
| Ish reja | `CurriculumService`, `CurriculumController` (`/api/admin/curriculum`, key `curriculum`), `TeacherCurriculumController` (`/api/teacher/curriculum`, own class × subject only). Pages: O'quv bo'limi → Ish rejalar / Ish reja hisoboti; teacher `/teacher/ish-reja` (SPA exception in `Program.cs`, link in the PWA profile) and the Mini App "Bugun" card |
| Owners of a grade plan | the grade's classes (and same-subject ordinary groups) that have the subject in a non-archived template; none scheduled → every class of the grade |
| Expected / forecast | a topic's planned day = its quarter week start; quarter only → spread over the quarter; neither → over the year. Forecast = remaining hours ÷ (passed hours per week since the year start) |
| Monitoring | `MonitoringService`, `MonitoringScore` (pure), `MonitoringController` (`/api/admin/monitoring`, key `monitoring`), `PublicMonitoringController` (`/api/public/monitoring`, rate policy `monitoring` 120/min). Pages: Ichki monitoring, Monitoring savollari; public `/monitoring` |
| Online attempts | finished lazily when their deadline passes (list/detail/any pupil call), and on "Yakunlash"; pupils who never started become absent |
| Certificates | `CertificatePoints`; `GET /api/admin/certificates/rating`, `GET/PUT …/track-subjects`; Sertifikatlar → Reyting tab, "Blok bali" column, profile summary |
| Not decided by the client | a pupil **without a track** (grades 1–8) has no total points (count only) — 1st/2nd subject is unknown |
