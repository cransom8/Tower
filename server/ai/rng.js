"use strict";

/**
 * Deterministic string/number hashing to uint32.
 * @param {string|number} seed
 * @returns {number}
 */
function hashSeed(seed) {
  const str = String(seed ?? "");
  let h = 2166136261 >>> 0; // FNV-1a
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/**
 * Mulberry32 RNG with reproducible sequence.
 * @param {number|string} seed
 * @returns {{next: () => number, nextInt: (min:number, max:number)=>number, pick:<T>(arr:T[])=>T|null, fork:(salt:string)=>any}}
 */
function createRng(seed) {
  let state = hashSeed(seed) || 0x6d2b79f5;
  function next() {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  }
  function nextInt(min, max) {
    const lo = Math.ceil(Math.min(min, max));
    const hi = Math.floor(Math.max(min, max));
    if (hi <= lo) return lo;
    return lo + Math.floor(next() * (hi - lo + 1));
  }
  function pick(arr) {
    if (!Array.isArray(arr) || arr.length === 0) return null;
    return arr[nextInt(0, arr.length - 1)];
  }
  return {
    next,
    nextInt,
    pick,
    fork(salt) {
      return createRng(`${state}:${String(salt || "")}`);
    },
  };
}

module.exports = { createRng, hashSeed };

