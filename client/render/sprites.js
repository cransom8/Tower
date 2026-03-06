(function (global) {
  'use strict';

  function drawSpriteFrame(ctx, image, frame, x, y, width, height, opts) {
    if (!ctx || !image || !frame) return;
    const sx = Number(frame.x) || 0;
    const sy = Number(frame.y) || 0;
    const sw = Number(frame.w) || image.width;
    const sh = Number(frame.h) || image.height;
    const alpha = opts && Number.isFinite(opts.alpha) ? opts.alpha : 1;

    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.drawImage(image, sx, sy, sw, sh, x, y, width, height);
    ctx.restore();
  }

  function drawSpriteCentered(ctx, image, frame, cx, cy, width, height, opts) {
    const x = cx - (width / 2);
    const y = cy - (height / 2);
    drawSpriteFrame(ctx, image, frame, x, y, width, height, opts);
  }

  global.SpriteRenderer = {
    drawSpriteFrame: drawSpriteFrame,
    drawSpriteCentered: drawSpriteCentered
  };
})(window);
