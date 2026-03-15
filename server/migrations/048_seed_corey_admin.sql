-- Seed the primary RansomForge admin account for Google SSO.
-- Password stays NULL so the account can be linked on first Google admin login.

UPDATE admin_users
SET
  display_name = 'Corey Ransom',
  role = 'owner',
  active = true
WHERE lower(email) = lower('corey@ransomforge.com');

INSERT INTO admin_users (email, display_name, password_hash, role, active)
SELECT
  'corey@ransomforge.com',
  'Corey Ransom',
  NULL,
  'owner',
  true
WHERE NOT EXISTS (
  SELECT 1
  FROM admin_users
  WHERE lower(email) = lower('corey@ransomforge.com')
);
