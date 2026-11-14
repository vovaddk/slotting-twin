// Canvas rendering of the warehouse: racks coloured by ABC class, the depot, and the animated picker route.
// World coordinates come from the API in metres: x across aisles, y from the front cross-aisle (0) to the back.

const PADDING = 16;
const CLASS_RANK = { A: 0, B: 1, C: 2 };

function themeColours() {
  const css = getComputedStyle(document.documentElement);
  const read = (name) => css.getPropertyValue(name).trim();
  return {
    floor: read('--floor'), empty: read('--slot-empty'), route: read('--route'), depot: read('--depot'),
    text: read('--text'), panel: read('--panel'),
    A: read('--abc-a'), B: read('--abc-b'), C: read('--abc-c'),
  };
}

/** Sums picks per bay side and keeps the best ABC class found there. */
function aggregateHeat(locations) {
  const bays = new Map();
  for (const loc of locations) {
    const key = `${loc.aisle}:${loc.bay}:${loc.side}`;
    const bay = bays.get(key) ?? { aisle: loc.aisle, bay: loc.bay, side: loc.side, picks: 0, abc: null };
    bay.picks += loc.picks;
    if (loc.abc && (bay.abc === null || CLASS_RANK[loc.abc] < CLASS_RANK[bay.abc])) bay.abc = loc.abc;
    bays.set(key, bay);
  }
  return [...bays.values()];
}

function geometry(canvas, layout) {
  const s = layout.aisleSpacing;
  const w = layout.bayWidth;
  const yMin = -1.5 * w;
  const yMax = (layout.baysPerAisle + 1.5) * w;
  const cssWidth = canvas.clientWidth || 600;
  const scale = (cssWidth - 2 * PADDING) / (layout.aisles * s);
  const cssHeight = (yMax - yMin) * scale + 2 * PADDING;
  return {
    s, w, scale, cssWidth, cssHeight,
    x: (x) => PADDING + (x + s / 2) * scale,
    y: (y) => PADDING + (yMax - y) * scale,
  };
}

function resize(canvas, g) {
  const dpr = window.devicePixelRatio || 1;
  canvas.style.height = `${g.cssHeight}px`;
  canvas.width = Math.round(g.cssWidth * dpr);
  canvas.height = Math.round(g.cssHeight * dpr);
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return ctx;
}

/** Rack centre for a pick: racks sit between a quarter and a half of the aisle spacing from the aisle centre line. */
function rackX(g, pick) {
  return pick.x + (pick.side === 'Left' ? -0.375 : 0.375) * g.s;
}

function drawScene(ctx, g, view, colours) {
  const { layout, heat } = view;
  ctx.fillStyle = colours.floor;
  ctx.fillRect(0, 0, g.cssWidth, g.cssHeight);

  const bays = aggregateHeat(heat.locations);
  const maxPicks = Math.max(1, ...bays.map((b) => b.picks));
  for (const bay of bays) {
    const centre = bay.aisle * g.s;
    const left = bay.side === 'Left' ? centre - g.s / 2 : centre + g.s / 4;
    const yTop = (bay.bay + 1.5) * g.w;
    ctx.globalAlpha = bay.abc ? 0.35 + 0.65 * Math.sqrt(bay.picks / maxPicks) : 1;
    ctx.fillStyle = bay.abc ? colours[bay.abc] : colours.empty;
    ctx.fillRect(g.x(left) + 0.5, g.y(yTop) + 0.5, (g.s / 4) * g.scale - 1, g.w * g.scale - 1);
  }
  ctx.globalAlpha = 1;

  const depotX = g.x(layout.depotAisle * g.s);
  const depotY = g.y(0);
  const size = Math.max(8, g.scale * 0.9);
  ctx.fillStyle = colours.depot;
  ctx.fillRect(depotX - size / 2, depotY - size / 2, size, size);
  ctx.fillStyle = colours.text;
  ctx.font = '600 11px system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText('Depot', depotX, depotY + size / 2 + 2);
}

function drawRoute(ctx, g, route, length, colours) {
  const points = route.path.map((p) => [g.x(p.x), g.y(p.y)]);
  if (points.length === 0) return;

  ctx.strokeStyle = colours.route;
  ctx.lineWidth = 2;
  ctx.lineJoin = 'round';
  ctx.beginPath();
  ctx.moveTo(...points[0]);
  let remaining = length;
  let head = points[0];
  for (let i = 1; i < points.length && remaining > 0; i++) {
    const [x0, y0] = points[i - 1];
    const [x1, y1] = points[i];
    const segment = Math.hypot(x1 - x0, y1 - y0);
    const t = segment === 0 ? 1 : Math.min(1, remaining / segment);
    head = [x0 + (x1 - x0) * t, y0 + (y1 - y0) * t];
    ctx.lineTo(...head);
    remaining -= segment;
  }
  ctx.stroke();

  const radius = Math.max(7, g.scale * 0.45);
  ctx.font = `700 ${Math.round(radius * 1.1)}px system-ui, sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const drawn = new Set();
  for (const pick of route.picks) {
    if (drawn.has(pick.location)) continue;
    drawn.add(pick.location);
    const x = g.x(rackX(g, pick));
    const y = g.y(pick.y);
    ctx.beginPath();
    ctx.arc(x, y, radius, 0, 2 * Math.PI);
    ctx.fillStyle = colours.route;
    ctx.fill();
    ctx.fillStyle = colours.panel;
    ctx.fillText(String(pick.stop), x, y + 0.5);
  }

  ctx.beginPath();
  ctx.arc(head[0], head[1], radius * 0.7, 0, 2 * Math.PI);
  ctx.fillStyle = colours.depot;
  ctx.fill();
}

function pathLength(g, route) {
  let total = 0;
  for (let i = 1; i < route.path.length; i++) {
    const a = route.path[i - 1];
    const b = route.path[i];
    total += Math.hypot((b.x - a.x) * g.scale, (b.y - a.y) * g.scale);
  }
  return total;
}

/**
 * Draws a view ({ layout, heat, route }) and animates the route unless the user prefers reduced motion.
 * Returns a function that cancels the animation.
 */
export function render(canvas, view, { animate = true } = {}) {
  const g = geometry(canvas, view.layout);
  const ctx = resize(canvas, g);
  const colours = themeColours();
  const total = pathLength(g, view.route);
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const frame = (length) => {
    drawScene(ctx, g, view, colours);
    drawRoute(ctx, g, view.route, length, colours);
  };

  if (!animate || reduced || total === 0) {
    frame(total);
    return () => {};
  }

  const duration = Math.min(4000, Math.max(1200, total * 4));
  const start = performance.now();
  let handle = 0;
  const step = (now) => {
    const t = Math.min(1, (now - start) / duration);
    frame(total * t);
    if (t < 1) handle = requestAnimationFrame(step);
  };
  handle = requestAnimationFrame(step);
  return () => cancelAnimationFrame(handle);
}
