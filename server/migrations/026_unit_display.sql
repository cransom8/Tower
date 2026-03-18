-- 026_unit_display.sql
-- Adds display_to_players flag to unit_types and unit_display_fields table
-- for controlling which unit data fields appear in the player-facing Kanban UI.

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS display_to_players BOOLEAN NOT NULL DEFAULT TRUE;

CREATE TABLE IF NOT EXISTS unit_display_fields (
  field_key          TEXT PRIMARY KEY,
  label              TEXT NOT NULL,
  visible_to_players BOOLEAN NOT NULL DEFAULT TRUE,
  sort_order         INTEGER NOT NULL DEFAULT 0
);

INSERT INTO unit_display_fields (field_key, label, visible_to_players, sort_order) VALUES
  ('behavior_mode',        'Role',            TRUE,   5),
  ('hp',                   'HP',              TRUE,  10),
  ('attack_damage',        'Attack Damage',   TRUE,  20),
  ('attack_speed',         'Attack Speed',    TRUE,  30),
  ('range',                'Range',           TRUE,  40),
  ('path_speed',           'Move Speed',      TRUE,  50),
  ('damage_type',          'Damage Type',     TRUE,  60),
  ('armor_type',           'Armor Type',      TRUE,  70),
  ('damage_reduction_pct', 'Armor %',         FALSE, 80),
  ('send_cost',            'Send Cost',       TRUE,  90),
  ('build_cost',           'Build Cost',      TRUE, 100),
  ('income',               'Income',          FALSE, 110)
ON CONFLICT (field_key) DO NOTHING;
