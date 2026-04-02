'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');

const {
  normalizePlayerPreferences,
  createDefaultPlayerPreferences,
} = require('../lib/playerPreferences');

test('createDefaultPlayerPreferences returns the expected defaults', () => {
  assert.deepEqual(createDefaultPlayerPreferences(), {
    camera: {
      tilt: null,
      zoom: null,
      rotation: null,
    },
    visuals: {
      showEngagementCircles: true,
      showHealthBars: true,
      showTooltips: true,
    },
    audio: {
      masterVolume: 1,
      sfxVolume: 1,
      ambientVolume: 0.5,
      menuMusicVolume: 0.5,
      gameplayMusicVolume: null,
    },
  });
});

test('normalizePlayerPreferences clamps numeric fields and fills defaults', () => {
  assert.deepEqual(normalizePlayerPreferences({
    camera: {
      tilt: 90,
      zoom: 1,
      rotation: 450,
    },
    visuals: {
      showEngagementCircles: false,
    },
    audio: {
      masterVolume: 1.4,
      sfxVolume: -3,
      ambientVolume: 0.3333,
      menuMusicVolume: 0.8,
      gameplayMusicVolume: 1.5,
    },
  }), {
    camera: {
      tilt: 52,
      zoom: 4,
      rotation: 90,
    },
    visuals: {
      showEngagementCircles: false,
      showHealthBars: true,
      showTooltips: true,
    },
    audio: {
      masterVolume: 1,
      sfxVolume: 0,
      ambientVolume: 0.33,
      menuMusicVolume: 0.8,
      gameplayMusicVolume: 1,
    },
  });
});

test('normalizePlayerPreferences migrates legacy ambient music volume to menu music and lets gameplay follow menu until it is explicitly set', () => {
  assert.deepEqual(normalizePlayerPreferences({
    audio: {
      ambientVolume: 0.25,
    },
  }), {
    camera: {
      tilt: null,
      zoom: null,
      rotation: null,
    },
    visuals: {
      showEngagementCircles: true,
      showHealthBars: true,
      showTooltips: true,
    },
    audio: {
      masterVolume: 1,
      sfxVolume: 1,
      ambientVolume: 0.25,
      menuMusicVolume: 0.25,
      gameplayMusicVolume: null,
    },
  });
});

test('normalizePlayerPreferences ignores invalid shapes', () => {
  assert.deepEqual(normalizePlayerPreferences({
    camera: 'bad',
    visuals: [],
    audio: null,
  }), createDefaultPlayerPreferences());
});
