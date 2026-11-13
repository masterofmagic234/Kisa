// Mirrors the PNG compositing contract in scripts/intro_stage.gd.
window.drawIntroStage = function (ctx, plan, images, seconds, width = 1280, height = 720) {
  const frameWidth = 768;
  const frameHeight = 432;
  const scale = Math.min(width / frameWidth, height / frameHeight);
  const left = (width - frameWidth * scale) / 2;
  const top = (height - frameHeight * scale) / 2;
  const shot = plan.shots.find(entry => seconds < entry.end) || plan.shots.at(-1);
  const elapsed = Math.max(0, seconds - shot.start);
  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

  function track(keys, time, ease) {
    let previous = keys[0];
    for (const next of keys.slice(1)) {
      if (time < next[0]) {
        let weight = clamp((time - previous[0]) / (next[0] - previous[0]), 0, 1);
        if (ease) weight = weight * weight * (3 - 2 * weight);
        return previous.slice(1).map((value, i) => value + (next[i + 1] - value) * weight);
      }
      previous = next;
    }
    return previous.slice(1);
  }

  function sprite(name, x, y, size, view, target, opacity = 1, visibleWidth = 1) {
    const [path, sx, sy, sw, sh] = plan.art[name];
    const [vx, vy, vw, vh] = view;
    const [tx, ty, tw, th] = target;
    const x0 = Math.max(x, vx);
    const y0 = Math.max(y, vy);
    const w = Math.min(x + sw * size * visibleWidth, vx + vw) - x0;
    const h = Math.min(y + sh * size, vy + vh) - y0;
    if (w <= 0 || h <= 0 || opacity <= 0) return;
    ctx.globalAlpha = opacity;
    ctx.drawImage(images[path], sx + (x0 - x) / size, sy + (y0 - y) / size, w / size, h / size,
      tx + (x0 - vx) * tw / vw, ty + (y0 - vy) * th / vh, w * tw / vw, h * th / vh);
  }

  function layer(entry, view, target, time) {
    const [dx, dy, opacity] = entry.motion ? track(entry.motion, time, entry.ease ?? true) : [0, 0, 1];
    const [x, y] = entry.at;
    let name = entry.art;
    if (entry.pose) {
      const frames = plan.poses[entry.pose];
      const [phase] = track(entry.phase, time, true);
      name = frames[Math.round(clamp(phase, 0, 1) * (frames.length - 1))];
    }
    const visibleWidth = entry.wipe ? track(entry.wipe, time, false)[0] : 1;
    sprite(name, x + dx, y + dy, entry.scale ?? 1, view, target, opacity, visibleWidth);
  }

  function traffic(entry, view, target, time) {
    const distance = (entry.offset ?? 0) + time * entry.rate;
    const fleet = plan.formations[entry.fleet];
    for (const [name, x, depthOrY, nearWidth, ax, ay] of fleet.vehicles) {
      const [, , , w, h] = plan.art[name];
      let size, gx, gy;
      if (fleet.projection === 'avenue') {
        const depth = depthOrY - distance;
        if (depth < 0.58) continue;
        size = nearWidth / (w * depth);
        gx = 689 + (x - 689) / depth;
        gy = 117 + (450 - 117) / depth;
      } else {
        size = nearWidth / w;
        gx = x + distance;
        gy = depthOrY;
      }
      const ox = gx - w * ax * size;
      const oy = gy - h * ay * size;
      sprite(name, ox, oy, size, view, target);
      for (const [pose, wx, wy] of plan.wheels[name] || []) {
        const frames = plan.poses[pose];
        const phase = Math.floor(0.11 * distance / size);
        const frame = ((phase % frames.length) + frames.length) % frames.length;
        sprite(frames[frame], ox + wx * size, oy + wy * size, size, view, target);
      }
    }
  }

  function drawPanel(panel, time) {
    const [x, y, w, h] = panel.rect || [0, 0, frameWidth, frameHeight];
    const target = [left + x * scale, top + y * scale, w * scale, h * scale];
    ctx.save();
    if (panel.mirror) {
      ctx.translate(2 * target[0] + target[2], 0);
      ctx.scale(-1, 1);
    }
    const [px, py, pw, ph] = panel.crop;
    const [dx, dy] = panel.camera ? track(panel.camera, time, true) : [0, 0];
    const vw = Math.min(pw, ph * w / h);
    const vh = Math.min(ph, pw * h / w);
    const view = [px + dx + (pw - vw) / 2, py + dy + (ph - vh) / 2, vw, vh];
    ctx.globalAlpha = 1;
    if (panel.art) sprite(panel.art, 0, 0, 1, view, target);
    for (const entry of panel.behind || []) layer(entry, view, target, time);
    if (panel.traffic) traffic(panel.traffic, view, target, time);
    for (const entry of panel.layers || []) layer(entry, view, target, time);
    if (panel.tone) {
      ctx.globalAlpha = 1;
      ctx.globalCompositeOperation = 'multiply';
      ctx.fillStyle = `rgb(${panel.tone.map(value => Math.round(255 * value)).join(' ')})`;
      ctx.fillRect(...target);
      ctx.globalCompositeOperation = 'source-over';
    }
    ctx.restore();
  }

  ctx.save();
  ctx.fillStyle = '#141713';
  ctx.fillRect(0, 0, width, height);
  for (const panel of shot.panels) {
    const reveal = panel.reveal ?? 0;
    if (elapsed >= reveal) drawPanel(panel, elapsed - reveal);
  }
  const labelOpacity = clamp((4.4 - elapsed) * 1.5, 0, 1);
  if (shot.label && labelOpacity > 0) {
    const x = left + 28 * scale;
    const y = top + 46 * scale;
    ctx.globalAlpha = labelOpacity;
    ctx.font = `${Math.max(12, Math.round(14 * scale))}px Body`;
    ctx.textAlign = 'left';
    ctx.fillStyle = '#141712e0';
    ctx.fillRect(x - 10 * scale, y - 20 * scale, ctx.measureText(shot.label).width + 20 * scale, 28 * scale);
    ctx.fillStyle = '#f0e6cc';
    ctx.fillText(shot.label, x, y);
  }
  ctx.restore();
  return shot;
};
