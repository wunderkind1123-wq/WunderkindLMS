-- ===========================================================================
--  Arxivlash sabablari GURUHLARI (mijoz, 2026-11-13) — docs/modules/archive-reason-groups.md.
--
--  Uchta boshlang'ich guruh — arxiv doskasining ustunlari. Maktab ularni keyin o'zi
--  nomlaydi, bo'yaydi, o'chiradi; shuning uchun ON CONFLICT DO NOTHING: qayta ishga
--  tushganda (yoki maktab o'z guruhini shu nom bilan allaqachon qo'shgan bo'lsa) hech
--  narsa buzilmaydi.
--
--  parity_wave2_seed.sql dagi sabablar guruhlarga qo'yiladi — FAQAT hali guruhsiz
--  bo'lsa (maktab o'zi qo'ygan guruh ustidan yozilmaydi). "Boshqa" (…a9) ataylab
--  guruhsiz qoladi: u bilan ketganlar doskada "Sababi aniqlanmagan"da turadi va
--  tartibga keltiriladi.
-- ===========================================================================

INSERT INTO student_archive_reason_groups (id, name, color, position, is_active) VALUES
    ('00000000-0000-0000-0000-0000000000b1', 'O''zi ketdi',       'rose',  0, true),
    ('00000000-0000-0000-0000-0000000000b2', 'Maktab chiqardi',   'amber', 1, true),
    ('00000000-0000-0000-0000-0000000000b3', 'Tabiiy sabab',      'slate', 2, true)
ON CONFLICT (name) DO NOTHING;

-- Har bir UPDATE guruh haqiqatan mavjud bo'lsagina ishlaydi (nom to'qnashib INSERT
-- o'tkazib yuborilgan bo'lsa ham FK buzilmaydi).
UPDATE student_archive_reasons r SET group_id = g.id
FROM student_archive_reason_groups g
WHERE r.group_id IS NULL
  AND (
       (g.id = '00000000-0000-0000-0000-0000000000b1' AND r.id IN (
            '00000000-0000-0000-0000-0000000000a2',   -- Boshqa maktabga o'tdi
            '00000000-0000-0000-0000-0000000000a4',   -- Oilaviy sabablarga ko'ra
            '00000000-0000-0000-0000-0000000000a5'))  -- To'lov qiyinchiligi
    OR (g.id = '00000000-0000-0000-0000-0000000000b2' AND r.id IN (
            '00000000-0000-0000-0000-0000000000a6'))  -- O'qishdan chetlashtirildi
    OR (g.id = '00000000-0000-0000-0000-0000000000b3' AND r.id IN (
            '00000000-0000-0000-0000-0000000000a1',   -- Maktabni bitirdi
            '00000000-0000-0000-0000-0000000000a3'))  -- Boshqa shaharga ko'chdi
  );
