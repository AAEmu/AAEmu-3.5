-- Buff 807 describes 30% lower spell accuracy. The accuracy multiplier uses thousandths.
UPDATE unit_modifiers
SET value = -300
WHERE owner_type = 'Buff'
  AND owner_id = 807
  AND unit_attribute_id = 88
  AND unit_modifier_type_id = 0
  AND linear_level_bonus = 0
  AND value = -3000;
