import { render } from './map.js';

const LABELS = {
  random: 'Random',
  alphabetical: 'Alphabetical (by SKU id)',
  coi: 'COI (cube-per-order index)',
  'coi-swap': 'COI + co-occurrence swaps',
  's-shape': 'S-shape',
  return: 'Return',
  'largest-gap': 'Largest gap',
  'nearest-neighbor': 'Nearest neighbour',
  '2-opt': 'Nearest neighbour + 2-opt',
};
const BASELINE = { slotting: 'random', routing: 's-shape' };

const $ = (id) => document.getElementById(id);
const metres = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const decimals = new Intl.NumberFormat('en', { maximumFractionDigits: 3 });
const percent = new Intl.NumberFormat('en', { maximumFractionDigits: 1, signDisplay: 'exceptZero' });

let layouts = [];
const views = { before: null, after: null };
const cancels = { before: () => {}, after: () => {} };

async function api(path, body) {
  const options = body
    ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
    : undefined;
  const response = await fetch(path, options);
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const details = problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title;
    throw new Error(details || `HTTP ${response.status}`);
  }
  return response.json();
}

function fillSelect(select, values, selected) {
  select.replaceChildren(...values.map((value) => new Option(LABELS[value] ?? value, value, false, value === selected)));
}

function currentLayout() {
  return layouts.find((l) => l.name === $('layout').value);
}

function updateOrderIds() {
  const layout = currentLayout();
  $('order-ids').replaceChildren(...layout.orderIds.map((id) => new Option(id)));
  $('order').value = '';
}

function renderKpis(sim) {
  const { baseline, candidate, improvementPercent } = sim;
  $('kpi-baseline').textContent = `${metres.format(baseline.totalDistance)} m`;
  $('kpi-optimized').textContent = `${metres.format(candidate.totalDistance)} m`;
  $('kpi-optimized-note').textContent = `${LABELS[candidate.slotting]} + ${LABELS[candidate.routing]}`;
  $('kpi-improvement').textContent = `${percent.format(improvementPercent)} %`;
  $('kpi-ppm').textContent = decimals.format(candidate.picksPerMeter);
  $('kpi-ppm-note').textContent = `baseline ${decimals.format(baseline.picksPerMeter)}`;
}

function renderMatrix(matrix) {
  const rows = ['A', 'B', 'C'].map((abc) => {
    const row = document.createElement('tr');
    const head = document.createElement('th');
    head.scope = 'row';
    head.textContent = abc;
    row.append(head, ...['X', 'Y', 'Z'].map((xyz) => {
      const cell = document.createElement('td');
      cell.textContent = String(matrix[`${abc}${xyz}`] ?? 0);
      return cell;
    }));
    return row;
  });
  $('matrix-body').replaceChildren(...rows);
}

function describe(route) {
  const stops = new Set(route.picks.map((p) => p.location)).size;
  return `Order ${route.orderId}: ${route.picks.length} lines at ${stops} stops, ${metres.format(route.distance)} m with ${LABELS[route.routing]}.`;
}

function draw(animate) {
  for (const key of ['before', 'after']) {
    const view = views[key];
    if (!view) continue;
    cancels[key]();
    const canvas = $(`map-${key}`);
    canvas.setAttribute('aria-label', `Warehouse map ${key} optimization. ${describe(view.route)}`);
    cancels[key] = render(canvas, view, { animate });
  }
}

// Controls stay enabled (disabling the focused button would drop keyboard focus); repeated submits are ignored instead.
let busy = false;
function setBusy(value) {
  busy = value;
  document.querySelector('main').setAttribute('aria-busy', String(value));
}

async function run() {
  if (busy) return;
  const layout = currentLayout();
  const slotting = $('slotting').value;
  const routing = $('routing').value;
  const seed = Math.min(2147483647, Math.max(0, Number.parseInt($('seed').value, 10) || 0)); // server seed is an Int32
  let orderId = $('order').value.trim();
  if (!orderId) {
    orderId = layout.orderIds[Math.floor(Math.random() * layout.orderIds.length)];
    $('order').value = orderId;
  }

  setBusy(true);
  $('status').textContent = `Replaying ${metres.format(layout.orders)} orders on “${layout.name}”…`;
  try {
    const abc = (s) => api(`/api/abc?layout=${encodeURIComponent(layout.name)}&slotting=${encodeURIComponent(s)}&seed=${seed}`);
    const [sim, heatBefore, heatAfter, routeBefore, routeAfter] = await Promise.all([
      api('/api/simulate', { layout: layout.name, slotting, routing, seed }),
      abc(BASELINE.slotting),
      abc(slotting),
      api('/api/route', { layout: layout.name, ...BASELINE, seed, orderId }),
      api('/api/route', { layout: layout.name, slotting, routing, seed, orderId }),
    ]);

    renderKpis(sim);
    renderMatrix(heatAfter.matrix);
    views.before = { layout, heat: heatBefore, route: routeBefore };
    views.after = { layout, heat: heatAfter, route: routeAfter };
    $('dist-before').textContent = `${metres.format(routeBefore.distance)} m`;
    $('dist-after').textContent = `${metres.format(routeAfter.distance)} m`;
    $('after-label').textContent = `${LABELS[slotting]} + ${LABELS[routing]}`;
    draw(true);

    const orderGain = routeBefore.distance === 0 ? 0 : (1 - routeAfter.distance / routeBefore.distance) * 100;
    $('status').textContent =
      `Order ${orderId}: ${metres.format(routeBefore.distance)} m before, ${metres.format(routeAfter.distance)} m after ` +
      `(${percent.format(orderGain)} %). All orders: ${percent.format(sim.improvementPercent)} % less travel.`;
  } catch (error) {
    $('status').textContent = `Could not run the simulation: ${error.message}`;
  } finally {
    setBusy(false);
  }
}

async function init() {
  try {
    const [layoutList, strategies] = await Promise.all([api('/api/layouts'), api('/api/strategies')]);
    layouts = layoutList;
    fillSelect($('layout'), layouts.map((l) => l.name), layouts[0]?.name);
    fillSelect($('slotting'), strategies.slotting, 'coi-swap');
    fillSelect($('routing'), strategies.routing, '2-opt');
    updateOrderIds();
  } catch (error) {
    $('status').textContent = `Could not load layouts: ${error.message}`;
    return;
  }

  $('layout').addEventListener('change', updateOrderIds);
  $('controls').addEventListener('submit', (event) => {
    event.preventDefault();
    run();
  });
  $('random-order').addEventListener('click', () => {
    $('order').value = '';
    run();
  });

  // Redraw on width changes only: drawing itself changes the canvas height.
  let resizeTimer = 0;
  let lastWidth = 0;
  new ResizeObserver(([entry]) => {
    if (entry.contentRect.width === lastWidth) return;
    lastWidth = entry.contentRect.width;
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => draw(false), 100);
  }).observe(document.querySelector('.maps'));

  run();
}

init();
