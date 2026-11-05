-- Puts staff names in "Surname Name" order (client, 2026-11-05).
--
-- Names are one free-text column, so the order can only be inferred. Only names where the SECOND
-- word has a surname ending (-ov, -ova, -ev, -eva, -yev, -yeva) and the first does not are changed.
-- An optional third word is kept only when it is a patronymic. Anything else is listed for a person
-- to check and is left alone.
--
-- Default is a preview, which changes nothing:
--   psql -U schoollms -d schoollms -f this.sql
-- Apply (keeps the old values in staff_name_order_backup_20261105):
--   psql -U schoollms -d schoollms -v apply=1 -f this.sql

\set ON_ERROR_STOP on
\if :{?apply}
\else
  \set apply 0
\endif

BEGIN;

CREATE TEMP TABLE staff_names ON COMMIT DROP AS
SELECT 'teachers' AS source, id, full_name FROM teachers
UNION ALL
SELECT 'users', id, full_name FROM users WHERE role NOT IN ('student', 'parent')
UNION ALL
SELECT 'hr_employees', id, full_name FROM hr_employees;

CREATE TEMP TABLE staff_name_plan ON COMMIT DROP AS
WITH parts AS (
    SELECT source, id, full_name, regexp_split_to_array(btrim(full_name), '\s+') AS w
    FROM staff_names
)
SELECT source, id, full_name,
       CASE
           WHEN w[1] !~* '(ov|ova|ev|eva)$'
                AND w[2] ~* '(ov|ova|ev|eva)$'
                AND (cardinality(w) = 2
                     OR (cardinality(w) = 3
                         AND w[3] ~* '(ovich|evich|ovna|evna|o.?g.?li|qizi)$'))
           THEN array_to_string(ARRAY[w[2], w[1]] || w[3:], ' ')
       END AS new_name,
       w[1] ~* '(ov|ova|ev|eva)$' AS surname_first
FROM parts;

\echo '=== Will change ==='
SELECT source, full_name AS old_name, new_name FROM staff_name_plan
WHERE new_name IS NOT NULL ORDER BY new_name, source;

\echo '=== Could not decide, left as is, check by hand ==='
SELECT source, full_name FROM staff_name_plan
WHERE new_name IS NULL AND NOT surname_first ORDER BY full_name, source;

\if :apply
  CREATE TABLE staff_name_order_backup_20261105 AS
  SELECT source, id, full_name FROM staff_name_plan WHERE new_name IS NOT NULL;

  UPDATE teachers t SET full_name = p.new_name FROM staff_name_plan p
  WHERE p.source = 'teachers' AND p.id = t.id AND p.new_name IS NOT NULL;
  UPDATE users u SET full_name = p.new_name FROM staff_name_plan p
  WHERE p.source = 'users' AND p.id = u.id AND p.new_name IS NOT NULL;
  UPDATE hr_employees e SET full_name = p.new_name FROM staff_name_plan p
  WHERE p.source = 'hr_employees' AND p.id = e.id AND p.new_name IS NOT NULL;

  COMMIT;
  \echo 'APPLIED. Old values are in staff_name_order_backup_20261105.'
\else
  ROLLBACK;
  \echo 'PREVIEW ONLY, nothing changed. Add -v apply=1 to apply.'
\endif
