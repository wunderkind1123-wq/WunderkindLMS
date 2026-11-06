-- People entered as teachers by mistake are staff (client, 2026-11-06): three assistants and a cook.
--
-- For each: the teacher card is archived if it is not yet (three were archived by hand the same
-- morning; their reason is kept), so history — attendance, cash payments, audit — stays attached
-- to it. The account becomes role "staff" with the given position, and the name is corrected
-- everywhere. HR picks up the position on its next people sync (it shows "O'qituvchi" only for a
-- non-archived teacher). Archiving through the app blocks the login; this script does the same for
-- the one card it archives itself, so all four end up alike.
--
-- Default is a preview, which changes nothing:
--   psql -U schoollms -d schoollms -f this.sql
-- Apply (keeps the old values in teacher_to_staff_backup_20261106):
--   psql -U schoollms -d schoollms -v apply=1 -f this.sql

\set ON_ERROR_STOP on
\if :{?apply}
\else
  \set apply 0
\endif

BEGIN;

CREATE TEMP TABLE wanted (old_name text, new_name text, position text) ON COMMIT DROP;
INSERT INTO wanted VALUES
  ('Meliboyeva Xusnida', 'Meliboyeva Husnidaxon', 'Yordamchi xodim'),
  ('Murodova Nigora',    'Murodova Nigoraxon',    'Yordamchi xodim'),
  ('Marufova Orifaxon',  'Ma''rufova Orifaxon',   'Yordamchi xodim'),
  ('Yoqubov Zokirjon',   'Yoqubov Zokirjon',      'Oshpaz');

CREATE TEMP TABLE plan ON COMMIT DROP AS
SELECT w.old_name, w.new_name, w.position, t.id AS teacher_id, t.is_archived AS was_archived,
       t.user_id, u.role AS user_role
FROM wanted w
JOIN teachers t ON t.full_name = w.old_name
LEFT JOIN users u ON u.id = t.user_id;

\echo '=== People found (each name must appear exactly once, with a teacher account) ==='
SELECT w.old_name, w.new_name, w.position, count(p.teacher_id) AS teachers_found,
       bool_or(p.was_archived) AS already_archived, max(p.user_role) AS account_role
FROM wanted w LEFT JOIN plan p ON p.old_name = w.old_name
GROUP BY w.old_name, w.new_name, w.position ORDER BY w.old_name;

\echo '=== What is attached to the teacher card (stays with the archived card) ==='
SELECT p.new_name,
  (SELECT count(*) FROM schedule_lesson x       WHERE x.teacher_id = p.teacher_id) AS schedule_lessons,
  (SELECT count(*) FROM study_group_teachers x  WHERE x.teacher_id = p.teacher_id) AS study_groups,
  (SELECT count(*) FROM class_homeroom_teachers x WHERE x.teacher_id = p.teacher_id) AS homerooms,
  (SELECT count(*) FROM cash_box_transactions x WHERE x.teacher_id = p.teacher_id) AS cash_payments,
  (SELECT count(*) FROM teacher_attendances x   WHERE x.teacher_id = p.teacher_id) AS attendance_days,
  (SELECT count(*) FROM turnstile_events x      WHERE x.teacher_id = p.teacher_id) AS turnstile_events
FROM plan p ORDER BY p.new_name;

DO $$
BEGIN
  IF (SELECT count(*) FROM plan) <> (SELECT count(*) FROM wanted)
     OR (SELECT count(DISTINCT old_name) FROM plan) <> (SELECT count(*) FROM wanted)
     OR EXISTS (SELECT 1 FROM plan WHERE user_id IS NULL OR user_role <> 'teacher') THEN
    RAISE EXCEPTION 'Not exactly one teacher card with a teacher account per name; nothing changed.';
  END IF;
END $$;

\if :apply
  CREATE TABLE teacher_to_staff_backup_20261106 AS
  SELECT p.teacher_id, t.full_name AS teacher_name, t.is_archived, t.archived_at, t.archive_reason,
         p.user_id, u.full_name AS user_name, u.role, u.position, u.password_hash, u.initial_password,
         e.id AS hr_employee_id, e.full_name AS hr_name
  FROM plan p
  JOIN teachers t ON t.id = p.teacher_id
  JOIN users u ON u.id = p.user_id
  LEFT JOIN hr_employees e ON e.user_id = p.user_id;

  -- Same as the app's archive: the card is hidden and the login blocked.
  UPDATE users u SET password_hash = '', initial_password = NULL
  FROM plan p WHERE u.id = p.user_id AND NOT p.was_archived;
  UPDATE teachers t SET is_archived = true,
         archived_at = to_char(current_date, 'YYYY-MM-DD'),
         archive_reason = 'Xato kiritilgan: o''qituvchi emas'
  FROM plan p WHERE t.id = p.teacher_id AND NOT p.was_archived;

  UPDATE teachers t SET full_name = p.new_name FROM plan p WHERE t.id = p.teacher_id;

  UPDATE users u SET full_name = p.new_name, role = 'staff', position = p.position
  FROM plan p WHERE u.id = p.user_id;

  UPDATE hr_employees e SET full_name = p.new_name, updated_at = now()
  FROM plan p WHERE e.user_id = p.user_id;

  COMMIT;
  \echo 'APPLIED. Old values are in teacher_to_staff_backup_20261106.'
\else
  ROLLBACK;
  \echo 'PREVIEW ONLY, nothing changed. Add -v apply=1 to apply.'
\endif
