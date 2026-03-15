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

function formatRemoteContent(raw) {
  if (!raw || typeof raw !== 'object') return null;

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
    metadata: normalizeObject(raw.metadata),
    is_critical: normalizeBoolean(raw.is_critical, false),
    enabled: normalizeBoolean(raw.enabled, true),
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
  const remoteContent = formatRemoteContent(unitType.remote_content);
  return {
    ...unitType,
    remote_content: remoteContent,
  };
}

function formatPublicSkin(skin) {
  if (!skin || typeof skin !== 'object') return skin;
  const remoteContent = formatRemoteContent(skin.remote_content);
  return {
    ...skin,
    remote_content: remoteContent,
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

  const criticalContent = [];
  for (const unit of units) {
    if (unit.remote_content?.enabled && unit.remote_content?.is_critical) {
      criticalContent.push({
        kind: 'unit',
        key: unit.key,
        content_key: unit.remote_content.content_key,
      });
    }
  }
  for (const skin of skinEntries) {
    if (skin.remote_content?.enabled && skin.remote_content?.is_critical) {
      criticalContent.push({
        kind: 'skin',
        key: skin.skin_key,
        content_key: skin.remote_content.content_key,
      });
    }
  }

  return {
    generated_at: new Date().toISOString(),
    units,
    skins: skinEntries,
    critical_content: criticalContent,
  };
}

module.exports = {
  buildContentManifest,
  formatPublicSkin,
  formatPublicUnitType,
  formatRemoteContent,
};
