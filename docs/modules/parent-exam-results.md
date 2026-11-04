# Test results and monthly growth in the family's cabinet — 2026-11-03

Client: *"ota ona profiliga oylik va blok test natijalarini ko'rinadigan qism qo'sh. Ota onalar natijalar
kiritilgandan so'ng ko'ra olsin. Bundan tashqari ularda har oylik natijalar hisob-kitobi ham bo'lsin. Bu orqali ota
ona farzandidagi o'sishni kuzata olsin."*

- **Where:** Mini App → "Baholar" tab, above the quarter picker: cards "Oylik o'sish" and "Test natijalari". The web
  portal (`/parent`) has finance and news only and stays so. A pupil's own login sees the same.
- **API:** `GET /api/student/exam-results` → `{ months, exams, showTests }` (`PortalExamResultsService`). Behind
  `show_learning_progress_in_parent_dashboard`, like the grades: the school closing progress closes this too.
- **Who sees what (client, 2026-11-04):** grades 3–8 take a monthly test per subject, entered in Mavsumiy baholash, and
  see only "Oylik o'sish". Block tests are taken only by the track groups (filologiya, ijtimoiy, tabiiy fan, aniq fan,
  iqtisodiyot), entered DTM-style; those pupils see both cards, the block test counting in "Oylik o'sish" too.
  `showTests` = an active track group or grade 9+; otherwise the server sends no exams and no test percent.
- **Every month stays open** in "Oylik o'sish" so a parent can compare them; a month also shows each subject's change.
- **Which results:** exams of the Imtihonlar module with `kind = block`, not cancelled, the pupil `finished` or
  `absent`. Visible as soon as entered — no approval step, drafts with entered scores included. Only the child's own
  scores; no ranking, no other pupils.
- **Partial results:** teachers enter one subject each. Entered blocks show immediately, "kiritilmagan" for the rest;
  total and percent appear only when every block is entered, and a partial sitting stays out of the monthly mean.
- **Monthly calculation:** the month is the exam date's. Test percent = mean of the month's complete tests' percents,
  each test weighing the same whatever its maximum. "Kelmadi" is left out of the mean and counted apart. Monthly
  seasonal marks (`seasonal_marks`, kind `monthly`, 0–100) are a separate mean, never mixed with the test percent.
  Change = difference from the previous month having that figure. A subject's test percent sums all its blocks.
- Seasonal comments are not shown: they were written for staff.
- No migration. Tests: `PortalExamResultsTests`.
