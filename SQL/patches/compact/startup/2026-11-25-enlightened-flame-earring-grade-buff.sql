-- The Enlightened Ayanad Flame Earring uses the upgraded evasion series from grade 7.
DELETE FROM item_grade_buffs
WHERE id = 9001718 AND item_id = 9001894 AND item_grade_id = 7 AND buff_id = 7946
  AND EXISTS (
      SELECT 1 FROM item_grade_buffs
      WHERE id = 9001863 AND item_id = 9001894 AND item_grade_id = 7 AND buff_id = 9001198
  );
