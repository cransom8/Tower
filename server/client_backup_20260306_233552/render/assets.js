(function (global) {
  'use strict';

  const DEFAULT_MANIFEST = Object.freeze({
    images: Object.freeze({
      unit_icon_runner: 'assets/units/runner.svg',
      unit_icon_footman: 'assets/units/footman.png',
      unit_icon_ironclad: 'assets/units/ironclad.svg',
      unit_icon_warlock: 'assets/units/warlock.svg',
      unit_icon_golem: 'assets/units/golem.svg',
      unit_atlas_runner: 'assets/units/atlas/runner.png',
      unit_atlas_footman: 'assets/units/atlas/footman.png',
      unit_atlas_ironclad: 'assets/units/atlas/ironclad.png',
      unit_atlas_warlock: 'assets/units/atlas/warlock.png',
      unit_atlas_golem: 'assets/units/atlas/golem.png',
      bridge_lane_tiles: 'assets/Bridge/lane_tiles.jpg',
      wall_straight: 'assets/walls/wall_straight.png',
      wall_corner: 'assets/walls/wall_corner.png',
      wall_t: 'assets/walls/wall_t.png',
      wall_plus: 'assets/walls/wall_plus.png',
      canyon_bg: 'assets/canvas/canyon_bg_chatgpt_2026-03-04.png'
    })
  });

  function cloneManifest(manifest) {
    const src = manifest && typeof manifest === 'object' ? manifest : DEFAULT_MANIFEST;
    const images = Object.assign({}, (src.images || {}));
    return { images: images };
  }

  function loadImage(src) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.decoding = 'async';
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('image failed: ' + src));
      img.src = src;
    });
  }

  class RenderAssetsManager {
    constructor(manifest) {
      this.manifest = cloneManifest(manifest || DEFAULT_MANIFEST);
      this.images = {};
      this.ready = false;
    }

    async loadAll() {
      const entries = Object.entries(this.manifest.images || {});
      const results = await Promise.all(entries.map(async ([key, src]) => {
        const img = await loadImage(src);
        return [key, img];
      }));
      results.forEach(([key, img]) => {
        this.images[key] = img;
      });
      this.ready = true;
      return this;
    }

    getImage(key) {
      return this.images[key] || null;
    }

    getSource(key) {
      const images = this.manifest.images || {};
      return images[key] || '';
    }

    /**
     * Apply skin pack overrides from an array of pack items.
     * Each item: { unit_type_key, asset_slot, url }
     * asset_slot 'icon'      → overrides unit_icon_<key>
     * asset_slot 'sprite'    → overrides unit_atlas_<key>
     * asset_slot 'animation' → overrides unit_atlas_<key> (same slot, animated src)
     * Reloads affected images into this.images.
     * @param {Array<{unit_type_key:string, asset_slot:string, url:string}>} items
     * @returns {Promise<void>}
     */
    async applyPackOverrides(items) {
      if (!Array.isArray(items) || !items.length) return;
      const affected = [];
      for (const item of items) {
        const { unit_type_key: typeKey, asset_slot: slot, url } = item;
        let manifestKey = null;
        if (slot === 'icon') {
          manifestKey = 'unit_icon_' + typeKey;
        } else if (slot === 'sprite' || slot === 'animation') {
          manifestKey = 'unit_atlas_' + typeKey;
        }
        if (manifestKey) {
          this.manifest.images[manifestKey] = url;
          affected.push(manifestKey);
        }
      }
      await Promise.all(affected.map(async key => {
        try {
          this.images[key] = await loadImage(this.manifest.images[key]);
        } catch (_) { /* ignore failed overrides */ }
      }));
    }
  }

  global.RenderAssets = {
    manifest: DEFAULT_MANIFEST,
    createManager: function createManager(manifest) {
      return new RenderAssetsManager(manifest || DEFAULT_MANIFEST);
    },
    loadImage: loadImage
  };
})(window);

