-- Disable the in-progress Toony Tiny RTS skins for launch readiness.
-- They remain seeded in skin_catalog, but are hidden from public catalog,
-- manifest generation, equip flows, and match runtime until fully onboarded.

UPDATE skin_catalog
   SET enabled = false
 WHERE skin_key LIKE 'tt\_%' ESCAPE '\';
