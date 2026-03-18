'use strict';

function normalizeString(value) {
  if (value === undefined || value === null) return null;
  const trimmed = String(value).trim();
  return trimmed || null;
}

function normalizeBoolean(value, fallback = false) {
  if (value === undefined || value === null) return fallback;
  return !!value;
}

function normalizeArray(value) {
  if (Array.isArray(value)) {
    return value
      .map((entry) => normalizeString(entry))
      .filter(Boolean);
  }
  return [];
}

function normalizeObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function normalizeTier(value, fallback = null) {
  const normalized = normalizeString(value);
  if (!normalized) return fallback;

  const lowered = normalized.toLowerCase();
  switch (lowered) {
    case 't0':
    case 't1':
    case 't2':
    case 't3':
      return lowered;
    default:
      return fallback;
  }
}

function deriveTier(raw, fallbackTier = 't2') {
  const metadata = normalizeObject(raw?.metadata);
  const explicitTier = normalizeTier(raw?.tier ?? metadata.tier, null);
  if (explicitTier) return explicitTier;

  if (normalizeBoolean(raw?.is_critical, false))
    return 't1';

  return fallbackTier;
}

function formatRemoteContent(raw, fallbackTier = 't2') {
  if (!raw || typeof raw !== 'object') return null;

  const metadata = normalizeObject(raw.metadata);

  const formatted = {
    id: raw.id ?? null,
    content_key: normalizeString(raw.content_key),
    addressables_label: normalizeString(raw.addressables_label),
    prefab_address: normalizeString(raw.prefab_address),
    placeholder_key: normalizeString(raw.placeholder_key),
    catalog_url: normalizeString(raw.catalog_url),
    content_url: normalizeString(raw.content_url),
    version_tag: normalizeString(raw.version_tag),
    content_hash: normalizeString(raw.content_hash),
    dependency_keys: normalizeArray(raw.dependency_keys),
    metadata,
    is_critical: normalizeBoolean(raw.is_critical, false),
    enabled: normalizeBoolean(raw.enabled, true),
    tier: deriveTier(raw, fallbackTier),
    preload_reason: normalizeString(raw.preload_reason ?? metadata.preload_reason),
  };

  if (
    !formatted.content_key &&
    !formatted.addressables_label &&
    !formatted.prefab_address &&
    !formatted.catalog_url &&
    !formatted.content_url &&
    formatted.dependency_keys.length === 0 &&
    Object.keys(formatted.metadata).length === 0
  ) {
    return null;
  }

  return formatted;
}

function formatPublicUnitType(unitType) {
  if (!unitType || typeof unitType !== 'object') return unitType;
  const remoteContent = formatRemoteContent(unitType.remote_content, 't2');
  return {
    ...unitType,
    remote_content: remoteContent,
  };
}

function formatPublicSkin(skin) {
  if (!skin || typeof skin !== 'object') return skin;
  const remoteContent = formatRemoteContent(skin.remote_content, 't2');
  return {
    ...skin,
    remote_content: remoteContent,
  };
}

function deriveManifestAddress(kind, remoteContent, extra = {}) {
  return normalizeString(
    extra.address
      ?? remoteContent?.prefab_address
      ?? remoteContent?.addressables_label
      ?? remoteContent?.content_url
      ?? remoteContent?.catalog_url
  );
}

function deriveManifestReason(kind, key, tier, remoteContent, extra = {}) {
  const explicit = normalizeString(extra.reason ?? remoteContent?.preload_reason);
  if (explicit) return explicit;

  if (tier === 't0') {
    switch (kind) {
      case 'unit':
        return `Required before lobby entry because ${key} is classified as T0 content.`;
      case 'skin':
        return `Required before lobby entry because ${key} is classified as T0 content.`;
      default:
        return `Required before lobby entry because ${key} is classified as T0 content.`;
    }
  }

  if (tier === 't1') {
    switch (kind) {
      case 'unit':
        return `Required before the first playable match because gameplay can spawn ${key}.`;
      case 'skin':
        return `Required before the earliest dependent screen because ${key} is classified as T1 content.`;
      case 'environment':
        return `Required before the first playable match starts so ${key} geometry is present.`;
      default:
        return `Required before the earliest dependent screen because ${key} is classified as T1 content.`;
    }
  }

  return null;
}

function buildManifestContentEntry(kind, key, remoteContent, extra = {}) {
  if (!remoteContent?.enabled || !remoteContent?.content_key) return null;

  const tier = normalizeTier(remoteContent.tier, 't2');
  const address = deriveManifestAddress(kind, remoteContent, extra);
  const reason = deriveManifestReason(kind, key, tier, remoteContent, extra);

  if ((tier === 't0' || tier === 't1') && !address) {
    return null;
  }

  return {
    kind,
    key,
    content_key: remoteContent.content_key,
    tier,
    address,
    reason,
  };
}

function buildContentManifest({ unitTypes = [], skins = [] } = {}) {
  const units = unitTypes
    .map((unitType) => formatPublicUnitType(unitType))
    .filter(Boolean)
    .map((unitType) => ({
      key: unitType.key,
      name: unitType.name,
      enabled: !!unitType.enabled,
      remote_content: unitType.remote_content,
    }));

  const skinEntries = skins
    .map((skin) => formatPublicSkin(skin))
    .filter(Boolean)
    .map((skin) => ({
      skin_key: skin.skin_key,
      unit_type: skin.unit_type,
      name: skin.name,
      remote_content: skin.remote_content,
    }));

  const t0Content = [];
  const t1Content = [];
  const criticalContent = [];

  const sharedT1Content = [
    {
      kind: 'environment',
      key: 'game_ml',
      content_key: 'environment_game_ml',
      tier: 't1',
      address: 'environment/game_ml',
      reason: 'Required before the first playable match starts so match geometry is present.',
    },
  ];

  for (const unit of units) {
    const entry = buildManifestContentEntry('unit', unit.key, unit.remote_content);
    if (!entry) continue;

    if (entry.tier === 't0') t0Content.push(entry);
    if (entry.tier === 't1') {
      t1Content.push(entry);
      criticalContent.push(entry);
    }
  }

  for (const skin of skinEntries) {
    const entry = buildManifestContentEntry('skin', skin.skin_key, skin.remote_content);
    if (!entry) continue;

    if (entry.tier === 't0') t0Content.push(entry);
    if (entry.tier === 't1') {
      t1Content.push(entry);
      criticalContent.push(entry);
    }
  }

  t1Content.push(...sharedT1Content);

  return {
    manifest_version: 2,
    generated_at: new Date().toISOString(),
    units,
    skins: skinEntries,
    t0_content: t0Content,
    t1_content: t1Content,
    critical_content: criticalContent,
  };
}

module.exports = {
  buildContentManifest,
  formatPublicSkin,
  formatPublicUnitType,
  formatRemoteContent,
};
