'use strict';

const CAMERA_TILT_MIN = 0;
const CAMERA_TILT_MAX = 52;
const CAMERA_ZOOM_MIN = 4;
const CAMERA_ZOOM_MAX = 80;

const DEFAULT_PLAYER_PREFERENCES = Object.freeze({
  camera: Object.freeze({
    tilt: null,
    zoom: null,
    rotation: null,
  }),
  visuals: Object.freeze({
    showEngagementCircles: true,
    showHealthBars: true,
  }),
  audio: Object.freeze({
    masterVolume: 1,
    sfxVolume: 1,
    ambientVolume: 0.5,
  }),
});

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function roundPreferenceNumber(value) {
  return Math.round(value * 100) / 100;
}

function clampNumber(value, min, max) {
  if (!Number.isFinite(value))
    return null;

  return roundPreferenceNumber(Math.min(max, Math.max(min, Number(value))));
}

function clampUnitInterval(value, fallback) {
  const resolved = clampNumber(value, 0, 1);
  return resolved === null ? fallback : resolved;
}

function normalizeAngle(value) {
  if (!Number.isFinite(value))
    return null;

  let angle = Number(value) % 360;
  if (angle > 180) angle -= 360;
  if (angle <= -180) angle += 360;
  return roundPreferenceNumber(angle);
}

function normalizeCameraPreferences(raw) {
  const source = isPlainObject(raw) ? raw : {};
  return {
    tilt: clampNumber(source.tilt, CAMERA_TILT_MIN, CAMERA_TILT_MAX),
    zoom: clampNumber(source.zoom, CAMERA_ZOOM_MIN, CAMERA_ZOOM_MAX),
    rotation: normalizeAngle(source.rotation),
  };
}

function normalizeVisualPreferences(raw) {
  const source = isPlainObject(raw) ? raw : {};
  return {
    showEngagementCircles: typeof source.showEngagementCircles === 'boolean'
      ? source.showEngagementCircles
      : DEFAULT_PLAYER_PREFERENCES.visuals.showEngagementCircles,
    showHealthBars: typeof source.showHealthBars === 'boolean'
      ? source.showHealthBars
      : DEFAULT_PLAYER_PREFERENCES.visuals.showHealthBars,
  };
}

function normalizeAudioPreferences(raw) {
  const source = isPlainObject(raw) ? raw : {};
  return {
    masterVolume: clampUnitInterval(source.masterVolume, DEFAULT_PLAYER_PREFERENCES.audio.masterVolume),
    sfxVolume: clampUnitInterval(source.sfxVolume, DEFAULT_PLAYER_PREFERENCES.audio.sfxVolume),
    ambientVolume: clampUnitInterval(source.ambientVolume, DEFAULT_PLAYER_PREFERENCES.audio.ambientVolume),
  };
}

function normalizePlayerPreferences(raw) {
  const source = isPlainObject(raw) ? raw : {};
  return {
    camera: normalizeCameraPreferences(source.camera),
    visuals: normalizeVisualPreferences(source.visuals),
    audio: normalizeAudioPreferences(source.audio),
  };
}

function createDefaultPlayerPreferences() {
  return normalizePlayerPreferences(DEFAULT_PLAYER_PREFERENCES);
}

module.exports = {
  CAMERA_TILT_MAX,
  CAMERA_TILT_MIN,
  CAMERA_ZOOM_MAX,
  CAMERA_ZOOM_MIN,
  DEFAULT_PLAYER_PREFERENCES,
  createDefaultPlayerPreferences,
  normalizePlayerPreferences,
};
