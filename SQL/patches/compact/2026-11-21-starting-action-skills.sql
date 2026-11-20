-- Restore the racial starting skills for Nuians, Dwarves and Warborn in the 3.5 compact database.

DELETE FROM character_default_skills
WHERE (character_id, default_skill_id) IN (
    SELECT id, CASE char_race_id WHEN 1 THEN 167 WHEN 3 THEN 168 WHEN 8 THEN 162 END
    FROM characters
    WHERE char_race_id IN (1, 3, 8)
);

INSERT INTO character_default_skills (character_id, default_skill_id)
SELECT c.id, ds.id
FROM characters c
JOIN default_skills ds ON ds.id = 161 AND ds.skill_id = 35420
WHERE c.char_race_id = 1 AND NOT EXISTS (
    SELECT 1 FROM character_default_skills cds
    WHERE cds.character_id = c.id AND cds.default_skill_id = ds.id
);
