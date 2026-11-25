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

### 1.1 Teacher's own plan (2026-11-17)

*"5-A va 5-B ga alohida ustoz kirishi mumkin va ularning ish rejasi farq qilishi mumkin … ustozning
ish rejasi o'zining dars soatlariga mos tushadimi yo'qmi tekshirish."*

- `curriculum_plans.teacher_id` (optional). A teacher plan's owners are the classes/groups of its grade
  (or track) where that teacher teaches the subject in the timetable. Upload rejects a teacher who
  teaches none of them. One plan per subject + grade/track + teacher; the shared plan (no teacher) stays.
- The shared plan drops a class when **every** teacher of that subject there has an own plan (a split
  class with one teacher lacking a plan stays on the shared plan).
- A teacher sees and marks only their own plan; for a key where they have one, the shared plan is hidden.
- **Fit** (`GET plans/{id}/fit`, teacher `GET {id}/fit`, also in the import dry run): per owner × quarter,
  plan hours vs lessons in the timetable (the week's assigned template, else the main one — counted as
  estimated weeks), without Sundays and holidays; a split half at the same time = one lesson; teacher
  plans count only that teacher's lessons. Exact topic dates without a lesson that day are listed.
  Substitutions are ignored.

### 1.2 Teacher uploads and edits their own plan (2026-11-18)

*"Ish rejani ustozning o'zi yuklaydigan holatga keltirish kerak. Ustoz LMS dan o'zi namunani ham yuklay
olsin … ish rejada asosan chorak / hafta / mavzu / sana bo'lishi kerak. Ustozlarga o'z ish rejalarini edit
qilish — dars holatidan kelib chiqib o'zgartirish imkoni bo'lsin … oldinda yoki orqadaligi ham ko'rinib tursin."*

| Question (asked 2026-11-18) | Answer |
|---|---|
| Approval by o'quv bo'limi | **None** — the teacher's upload is live at once; o'quv bo'limi sees it under Ish rejalar and can change or delete it |
| Required columns | **All four**: every topic has Chorak, Hafta, Mavzu and Sana, and the week must match the date — otherwise the file is refused with the row and what is missing |
| What the teacher edits | **Everything + shift**: title, date (quarter/week follow from it), hours, section, goal, homework; add (after any topic) and delete; "Dars bo'lmadi" shifts the topic and every later one by one lesson |

- Teacher API (`/api/teacher/curriculum`): `GET options` (subject + grade/track pairs from the timetable, with
  the own plan if any), `GET template`, `POST import` (`dryRun`; always the teacher's own plan, strict check,
  the uploaded file is kept as the original), `POST {planId}/topics`, `PUT/DELETE topics/{id}`, `POST shift`.
  Edits only on a plan with `teacher_id` = the teacher; the shared plan stays read-only (403).
- O'quv bo'limi gets the same edit endpoints under `/api/admin/curriculum` (any plan). Its own uploads keep the
  old, lenient check (only Mavzu required).
- Edited topic: the date is required and must fall in a quarter, not on a Sunday or a holiday; quarter and week
  are recomputed. A new topic goes after `afterOrder`, later ones renumber (marks follow the topic, not the number).
- Shift: each topic of the tail takes the next one's date; the last one moves to the next lesson day of the
  plan's subject (teacher plan — that teacher's lessons) in the timetable. Refused when a tail topic has no date
  or no lesson is left before the year ends. Marks are not touched.
- Plan page shows, per class/group, the same ahead/behind as the report (`owners[].delta`).

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

### 2.1 Oylik test — the result goes to Mavsumiy baholash (2026-11-24)

The client asked for an onlinetestpad-like platform to run the **monthly tests of grades 3–8** on
the school's lab computers. That is this module's online test, so no second engine was built.

| Question | Answer |
|---|---|
| Lab computers | Nothing installed. A browser opens `wunderkindschool.uz/monitoring`; the pupil types the 8-character personal code from the printed list ("Chop etish" on the test page) |
| How a test becomes monthly | "Oylik test" + month on the create form → `monitoring_tests.seasonal_year / seasonal_month` (both or neither, `ck_monitoring_tests_seasonal`). Fixed after creation |
| What is written | every `done` pupil's **percent** → `seasonal_marks` (monthly, the test's subject and month, class = the class they sat in, or their own class for a track group). Audit row `SeasonalMark`, actor "Onlayn test" |
| When | after every scoring: pupil finishes, deadline sweep, close, paper answers, key fix — so a corrected key follows through |
| A mark typed by hand for the same pupil/subject/month | **overwritten** by the test; its comment is kept |
| A pupil who did not sit it | no mark written, an existing one untouched |
| Who sees it | the parent, through Mavsumiy baholash ("Oylik o'sish"). A test without the flag stays o'quv bo'limi-only, as before |

### 2.2 Teachers fill the banks from Word (2026-11-24)

*"Savollar ustozlar tomonidan Wunderkind LMS ga yuklashadi va tahrirlanadi. Ular odatda testlarni word file ga tayyorlashadi."*

| Question | Answer |
|---|---|
| Where | Teacher: `/teacher/savollar` (main SPA, the same `/teacher/*` exception as Ish reja; link "Test savollarim" in the PWA Profil). O'quv bo'limi: Monitoring savollari → a bank → "Word'dan yuklash" |
| Which banks a teacher sees | banks whose subject × grade (or track) the teacher teaches in the timetable (`CurriculumService.TeacherKeysAsync`). Every such teacher edits them together; deleting a bank — only its creator or o'quv bo'limi. Tests and results stay o'quv bo'limi-only — the teacher API has none |
| Upload | `.docx` / `.txt` file or pasted text → `POST …/import/preview` (saves nothing) → the teacher checks, clicks another option to fix the answer, drops a question → `POST …/questions/bulk` appends. Excel template import stays as before |
| Text format | the assignment import's (docs/modules/test-import.md): `1.` / `A)` / `*B)` / `Javob: B` / bold option / Word auto-numbering. A wholly bold Word paragraph is passed to the parser as `**…**` |
| Word template | `GET …/word-template` → `test-savollari-namuna.docx` ("Word namunasi" on the bank list and in the import dialog): rules in grey, five sample questions — one per answer style, two with equations. Imported unchanged it yields exactly those five (a test pins it) |
| Question image | `POST …/banks/{id}/images` (teacher: `…/question-banks/{id}/images`) — PNG/JPG/GIF/WEBP ≤ 5 MB → `{ url }`; the question editor adds, replaces or removes the image. A saved `imageUrl` must be a plain `/uploads/<name>` path |
| Formulas | Word equations (OMML) become linear text: `(a+b)/c`, `x^2`, `a_n`, `√x`. The assignment importer used to drop them silently; this reader is `MonitoringWordImport` |
| Images | a picture in the question → the question's image (saved to `/uploads` at preview); a picture in an option (`A) [picture]`, or on the line after `A)`) → that option's image (`option_images`, "" where none; a picture option may have no text). More than one per question or per option → that question is refused with the reason |
| Old equations | Equation 3.0 / MathType objects and WMF/EMF pictures cannot be shown in a browser → refused with "rewrite with Insert → Equation or paste as PNG" |
| Option images by hand | the question editor has an image button per option. Option images are copied into the test with the question |

### 2.3 Test integrity — copying, AI, leaving the window (2026-11-24)

*"Test davomidagi ko'chirish, sun'iy intellekt yoki cheatinglarni qanday oldini olish yo'llari bo'lsa qo'shish kerak."*

onlinetestpad.com (reviewed 2026-11-24, public pages) offers time limits, a date window, question/option shuffling, random draw from groups, a code word, one attempt per IP/cookie and hiding correct answers. Its pages show nothing on tab-switch tracking, copy blocking, fullscreen or AI. We have the first group already (codes, device lock, window, timer, no results shown to the pupil) and add:

| Measure | How |
|---|---|
| Own order per pupil | test option "Har o'quvchiga savollar va variantlar o'z tartibida" (`monitoring_tests.shuffle`, online only, on by default in the form). The order is computed from the participant id (splitmix64) — not stored, the same on re-entry. The session sends options in the pupil's order with `keys[k]` = the original letter; answers are stored by original letter, so scoring, Rasch and item stats do not change. Turn it off for "all of the above"-type options |
| Leaving the window | the page reports `hidden` (other tab), `blur` (other program — e.g. a ChatGPT app), `fullscreen` (left fullscreen) to `POST /api/public/monitoring/event`. One trip fires several — the browser sends one per 1.5 s and the server counts leaves less than 2 s apart once. `monitoring_participants.leaves` is the count, `events` the log (≤ 200, "2026-11-24T09:15:02Z blur") |
| Limit | `monitoring_tests.max_leaves` (0–20; empty — only logged; the form suggests 2). Leaves > limit → the attempt is finished with the answers so far, `finish_reason = 'leaves'`; the pupil sees "Test to'xtatildi". O'quv bo'limi can press "Davom ettirish" (`POST …/participants/{id}/reopen`): the count restarts, the log keeps a "reopened" line. Timer finishes get `finish_reason = 'time'` |
| Fullscreen | the test opens fullscreen from the "Boshlash" click; outside it the questions are blurred under "Testga qaytish" |
| Against AI / screenshots | the questions blur whenever the window loses focus (a screen-reading AI assistant or a screenshot tool sees a blur); copy, cut, paste, right click, text selection, drag, Ctrl+P/S/U, F12 are blocked and the attempts logged (`copy`, `paste`, `print`, `devtools`); PrintScreen clears the clipboard; printing shows "Testni chop etib bo'lmaydi"; the pupil's name and code are a faint watermark over the page, so a photographed question shows whose it was |
| O'quv bo'limi sees | "Halollik" column on the codes and results tabs (count, "to'xtatildi", the log on click); "Oynadan chiqish" column in the Excel export |
| Pupil flow | `/monitoring`: code → **check** (`POST /api/public/monitoring/check` — name, class/group, test, subject, question count, minutes; binds no device, starts no clock; same refusals as start) → "Ha, bu men — boshlash" / "Bu men emasman" → fullscreen test, **one question per page** with Oldingi / Keyingi, a number panel (answered filled, current ringed, click to jump), keys ← → and A–F; resumes at the first unanswered question → "Testni yakunlash" (in-page confirm) |
| Limits of a web page | a phone camera, a second device, or an AI browser extension reading the page cannot be stopped by the page. For the lab: run Chrome as a kiosk (`chrome.exe --kiosk http://<lms>/monitoring` — no tabs, no address bar), block AI sites on the school network during tests, keep phones away, and a proctor in the room |

### 2.4 How pupils reach the test (2026-11-24)

The school's existing test portal `wunderkindedu.uz/portal` is a separate static page (Supabase): grade/track → test card → a 6-digit password shared per test → the test opens as an **external link** in a new tab. It is not part of this repository. Plan agreed with the client:

1. Add a card on that portal linking to `https://lms.wunderkindedu.uz/monitoring` — pupils keep their habit, the personal code replaces the shared password (no grade choice needed: the code identifies pupil and test).
2. Lab PCs: a desktop shortcut `chrome.exe --kiosk https://lms.wunderkindedu.uz/monitoring`; AI sites blocked on the school network during tests.
3. Teachers move the old tests into banks via Word import; the old portal then only points here.

Found on review (reported to the client, not ours to fix): the old portal checks passwords in the browser — every visitor downloads all test passwords and URLs and the admin usernames/passwords (`admins` table read with the anon key). Admin passwords should be rotated and that table closed to the anon key.

### 2.5 Test sessiyasi — several subjects in one sitting (2026-11-24)

*"Bizdagi test tizimida o'quvchilar bir kirishda bir nechta fandan test ishlashadi va bir fandan testni tugatgan o'quvchi keyingi fandan test ishlashni boshlaydi."*

| Question | Answer |
|---|---|
| What | `monitoring_sittings` (title, date, ordered `test_ids`, 2–10 online tests, a test in one sitting only) + `monitoring_sitting_codes` (one 8-char code per pupil, unique across all monitoring codes, a device lock for the whole sitting). Each subject stays an ordinary online test — own time limit, shuffle, leave limit, result, Mavsumiy baho |
| Who gets a code | every pupil of any of the tests; a pupil missing from a subject (e.g. another track) skips it |
| Pupil flow | code → check shows the sitting and the subjects (✓ done, → current, ○ next) → the current subject → "«Matematika» bo'yicha javoblaringiz saqlandi — Keyingi fanga o'tish" (fullscreen kept) → … → "Barcha fanlar topshirildi" |
| Order | the code opens the first unfinished subject; a request for a later pending one gets 409 "Avval navbatdagi fanni tugating". Requests carry `testId` so a late answer never lands in the next subject |
| Device | the first start binds the sitting code to that computer; "Kompyuterni almashtirish" on the sitting page clears it and the current subject's lock |
| Not open yet | a subject still a draft or not yet in its window → "«title»: Test ochiq emas" / "… da ochiladi" — open every test of the sitting before the sitting starts |
| O'quv bo'limi | Monitoring → "Sessiyalar (bir nechta fan)": create (title, date, pick tests, arrange order), the sitting page shows per-pupil status per subject, total leaves, printable code slips. Deleting a sitting keeps tests and results; a test in a sitting cannot be deleted |

### 2.6 Ready for the first real monthly test (2026-11-25)

| Need | How |
|---|---|
| Wi-Fi drops for a moment | the pupil page keeps each answer in a local queue (`localStorage` `wk-monitoring-q:<code>:<testId>`) and sends it in order; a network error stops the queue and it retries every 4 s and on the browser's `online` event. A server refusal (time up, test closed) drops that answer and shows why. After a reload the queued answers are put back on screen and re-sent. "Testni yakunlash" waits until the queue is empty ("Internet yo'q — N ta javob hali yuborilmagan"). Header: "Saqlandi" / "Saqlanmoqda…" / "Internet yo'q · N javob kompyuterda saqlanmoqda". Answers queued past the deadline are refused — the server finishes on time. `PUT …/answer` returns `{ saved: <order> }` and only that acknowledgement empties the queue: a Wi-Fi login page or a proxy answering "200" must not swallow an answer (found on 2026-11-25 — another program on the dev PC answered 200 on 127.0.0.1:8080 while the backend was down). A finish sent by the page's timer is recorded as `finish_reason = 'time'` |
| Live view | `GET /api/admin/monitoring/sittings/{id}/live` and `…/tests/{id}/live` (finish expired attempts first): per pupil status (pending / in_progress / between / done / absent), subject `step/steps`, answered/total, leaves, stopped, last seen, deadline. `monitoring_participants.last_seen_at` is set by start, answer, event and a 30-second `POST /api/public/monitoring/ping` (which also finishes an expired attempt and tells the page when the test was closed). Page "Jonli kuzatuv" (sitting page and online test page): refreshes every 5 s, class filter, counters, tiles with a progress bar, time left, "Chiqdi: N", "To'xtatildi", "Aloqa yo'q" (in progress and silent > 75 s) |
| Quick sitting | `POST /api/admin/monitoring/sittings/quick`: title, date, window, classes/groups, 2–10 subject rows (bank, optional random question count, minutes), shuffle, leave limit, oylik month, open now. Each row becomes an online test "<title> — <subject>" from its bank (a random subset kept in bank order when a count is given), opened if asked, then the sitting — one transaction, a bad row creates nothing and names the subject. "Yangi sessiya" opens this form; "Tayyor testlardan" keeps the old way |

### 2.7 Trying it before deploy — from any network (2026-11-25)

| Need | How |
|---|---|
| Same network | `http://<this PC's IP>:8080/monitoring` — `/monitoring` is served as the SPA on any host (Program.cs fallback, like `/ariza/`), not only on `App:Host` |
| Any network | `docker-compose.tunnel.yml` (profile `test-tunnel`): `test-gateway` (Caddy, `deploy/test-tunnel/Caddyfile`) passes only `/monitoring`, `/assets/*`, `/uploads/*`, logo/favicon and `/api/public/monitoring/*` — everything else 404, `/` → `/monitoring`; `X-Forwarded-For` = `Cf-Connecting-Ip` so the rate limit counts pupils, not the tunnel. `test-tunnel` (cloudflared quick tunnel) gives a new `https://….trycloudflare.com` on every start. Not a deploy: the database and results stay on this PC; it must stay awake. O'quv bo'limi works from `http://localhost:8080` |

### 2.7a The pupil sees their own result (2026-11-25)

*"O'quvchilar test tugagandan so'ng necha foiz natija ko'rsatdi buni bila olsin, lekin qaysi savolda xato qilganini ko[rmasin]."*

On finishing (by themselves, by the timer, by the leave limit or when o'quv bo'limi closes the test) the page shows a ring with the percent and "N / M to'g'ri javob". In a sitting the subject list shows each finished subject's percent, and "Barcha fanlar topshirildi" lists them all. The session carries `resultCorrect` / `resultPercent` only once the attempt is done; the answer key (`correctIndex`) is never sent to the page, so which questions were wrong cannot be seen. National-certificate tests also show the percent — the 0–75 points depend on everyone (Rasch) and stay with o'quv bo'limi.

**Look (2026-11-25):** *"Tizimning ko'rinishi ham https://wunderkindedu.uz/ ga o'xshash bo'lsa."* The pupil page only (not the o'quv bo'limi panel) uses the school portal's look: cream background `#FFF8F2`, coral `#FF7B54` / blue `#4A9EFF`, pastel cards (peach, sky, mint, lavender), pill buttons, the 🎓 coral logo "Wunder**kind**", Baloo 2 headings and Nunito text. The fonts are self-hosted (`@fontsource/nunito`, `@fontsource/baloo-2` 5.3.0, OFL) — the CSP allows `font-src 'self'` only. Background spots are radial gradients, not `filter: blur` (slow on weak lab PCs).

### 2.8 Pre-deploy risk review (2026-11-25)

*"Ehtimoliy muammolar, xatolar, kamchiliklar — oldindan kelib chiqishi mumkin bo'lgan texnik muammolarni oldini olish tizimini qilib chiq."*

**Fixed in code**

| Risk | What happened / would happen | Fix |
|---|---|---|
| A class finishes at once | every finish rescores the whole test and writes Mavsumiy baho for every finished pupil; two finishes at the same moment both inserted the same pupil's mark → unique index → 500, that pupil's finish not saved | `pg_advisory_xact_lock` per test (`LockTestAsync`) around start, answer, event, finish, ping-at-deadline, reopen, finish-expired (one test at a time, in id order), close, key edit, paper answers; locked rows are re-read. Test: 5 pupils finish in parallel → all 200, exactly 5 marks |
| Lab PC clock off by minutes | the countdown and the auto-finish ran on the PC's clock → early finish, lost minutes | the session carries `serverNow`; the page counts down on server time |
| Answering after time is up while offline | the page kept the questions open | a "Vaqt tugadi" overlay at 0 — answers already queued are sent |
| A Wi-Fi login page / proxy answers 200 | answers looked saved and were lost | `{ saved }` acknowledgement (§2.6) |
| Timer finish recorded as the pupil's own | — | `finish_reason = 'time'` when past the deadline |
| Whole school behind one public IP | 6 classes starting together ≈ 300 check/start a minute; the 120/min limit answered half with 429 | limit 1500/min (codes ≈ 10¹² — still unguessable); the page says "Hozir juda ko'p o'quvchi kirmoqda — 10 soniyadan keyin qayta bosing" for 429, "Serverga ulanib bo'lmadi" when unreachable |
| A script error | white screen | an error boundary: "Sahifada xatolik — javoblaringiz saqlangan, sahifani yangilang" |
| `/monitoring` on another host | the landing page | served as the SPA on any host (§2.7) |

**Prevention tools**

| Tool | Where | Checks |
|---|---|---|
| Tayyorlik tekshiruvi | sitting page, online test page (auto on open, "Qayta tekshirish"); `GET …/sittings/{id}/readiness`, `…/tests/{id}/readiness` | error: paper test, not opened, closed, window passed, no pupils, pupils without codes, no questions, question without text, empty option, image file missing on the server; warning: window shorter than the time limit, sitting total time longer than the common window, a subject opening later than the first, position-dependent options ("barcha javoblar to'g'ri", "A va B", "yuqoridagi", "all of the above") in a shuffled test, duplicate questions, leave limit 0; info: pupils / subjects / questions / minutes, pupils not in every subject |
| Kompyuterni tekshirish | pupil code screen; `GET /api/public/monitoring/time` | Chrome version (≥ 109), browser storage (incognito → a reload locks the pupil out), fullscreen, server reachable and round trip, PC clock offset, screen size |
| Jonli kuzatuv | §2.6 | "Aloqa yo'q", "Chiqdi", progress during the test |

**Known, accepted**

- A guest/incognito browser that is closed mid-test loses the device id → "Kompyuterni almashtirish" on the sitting page (the PC check warns beforehand).
- Images uploaded at Word preview but never imported stay in `/uploads` (disk only).
- Rasch (national scoring) is unstable under 30 pupils — warned on the test page already.
- An oylik test overwrites a teacher-typed Mavsumiy baho for that month (by design, §2.1).
- Nothing on a web page stops a phone camera or a second device — proctor in the room.

**Deploy checklist**

1. Back up the production database; apply the four migrations (`MonitoringSeasonalMonth`, `MonitoringIntegrity`, `MonitoringSittings`, `MonitoringLastSeen`) — all add columns/tables with defaults, no data rewrite.
2. Open `https://<lms host>/monitoring` from outside the school and from a lab PC; run "Kompyuterni tekshirish" on every lab PC.
3. Create the real sitting a day before; the readiness check must show no errors.
4. Test day: lab PCs in Chrome kiosk; open "Jonli kuzatuv"; keep "Kompyuterni almashtirish" at hand.

### 2.9 Permanent personal code + room PIN (2026-11-25)

*"Maktabdagi barcha o'quvchilarga alohida kod beriladimi, har oy test olinsa har safar kod berish kerakmi?"* — client chose a code that stays the whole year plus a room PIN written on the board on test day.

| Question | Answer |
|---|---|
| Codes | `monitoring_student_codes` (student → 8-char code, unique across every monitoring code). Monitoring → "Doimiy kodlar": "Kodsizlarga kod berish" (all active pupils without one), per-pupil "Yangi kod" (the old one stops at once), class filter, printable slips (`<host>/monitoring · + doskadagi PIN`) |
| PIN | 4 digits, `monitoring_sittings.room_pin` (on create) and `monitoring_tests.room_pin` (on opening an online test); unique among open tests and sittings of the last 60 days. Shown big on the sitting page, the online test page and the live view; "Yangi PIN" rotates it (pupils already inside are not affected) |
| Entry | the page asks for the code and "Xona PIN — doskadagi 4 raqam". A one-time code (per test or per sitting) works as before and ignores the PIN. A permanent code needs the PIN: the newest sitting with that PIN in which the pupil has a seat, else a single online test (not part of a sitting) with that PIN where the pupil is a participant; no PIN → "Doskadagi 4 xonali xona PIN'ini kiriting", wrong PIN → "PIN noto'g'ri yoki bu test sizga tegishli emas". Every later request (start, answer, event, ping, finish) carries the PIN too |
| Why both | the code alone would let a classmate who saw it sit in someone's place or a pupil start from home; the PIN alone says nothing about who. Device lock, the confirm screen with the pupil's name and the live view stay as before |

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
