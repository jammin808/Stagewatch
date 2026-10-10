// The screen designer: place the timer, text with placeholders, messages, bars, labels, boxes and images on a live
// preview (real show data), snap to centres and edges, undo anything, and ask the copilot for a first draft from a
// sentence. Saving needs the PIN or a control API key; every screen showing the layout updates within a second.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var KINDS = { timer: 'Timer', text: 'Text', message: 'Message', progress: 'Progress bar', labels: 'Labels', box: 'Box', image: 'Image' };
  var FONTS = ['', 'Segoe UI', 'Arial', 'Helvetica', 'Verdana', 'Georgia', 'Consolas', 'Bahnschrift', 'Impact'];
  var PLACEHOLDERS = ['time', 'title', 'speaker', 'next', 'nextStart', 'nextIn', 'item', 'group', 'end', 'showEnd', 'clock', 'date', 'room', 'message', 'cue', 'aux', 'slide'];

  var pin = SW.q('pin') || SW.store('sw-pin') || '';
  var layout = starter('lobby');
  var sel = -1, history = [], future = [], saved = '', draftBackup = null, images = [];
  var render = SWLayout.create($('canvas'));

  function starter(name) {
    return { name: name, background: '#000000', font: '', elements: [
      { kind: 'text', x: 4, y: 4, w: 92, h: 10, text: '{title}', size: 7, color: '', bg: '', align: 'center', bold: true, image: '' },
      { kind: 'timer', x: 4, y: 18, w: 92, h: 42, text: '', size: 34, color: '', bg: '', align: 'center', bold: true, image: '' },
      { kind: 'progress', x: 10, y: 62, w: 80, h: 2, text: '', size: 6, color: '', bg: '', align: 'center', bold: false, image: '' },
      { kind: 'message', x: 4, y: 68, w: 92, h: 14, text: '', size: 7, color: '', bg: '', align: 'center', bold: true, image: '' },
      { kind: 'text', x: 4, y: 86, w: 92, h: 8, text: 'Next: {next}  ·  {nextStart}', size: 4.5, color: '#94A3B8', bg: '', align: 'center', bold: false, image: '' }
    ] };
  }
  function clone(o) { return JSON.parse(JSON.stringify(o)); }
  function status(t, bad) { $('status').textContent = t || ''; $('status').style.color = bad ? '#f87171' : ''; }
  function dirty() { return JSON.stringify(layout) !== saved; }

  // ---- history: every change is one step (drags merge into one)
  function change(fn, merge) {
    if (!merge) { history.push(clone(layout)); if (history.length > 100) history.shift(); future = []; }
    fn();
    refresh(!$('props').contains(document.activeElement));   // don't rebuild the field you're typing in
  }
  function undo() { if (!history.length) return; future.push(clone(layout)); layout = history.pop(); sel = Math.min(sel, layout.elements.length - 1); refresh(true); }
  function redo() { if (!future.length) return; history.push(clone(layout)); layout = future.pop(); refresh(true); }

  // ---- stage sizing
  function fitStage() {
    var a = $('aspect').value.split(':'), ar = +a[0] / +a[1];
    var wrap = $('stagewrap'), W = wrap.clientWidth - 32, H = wrap.clientHeight - 32;
    var w = Math.min(W, H * ar), h = w / ar;
    $('stage').style.width = w + 'px'; $('stage').style.height = h + 'px';
  }

  // ---- handles over the preview
  function drawHandles() {
    var hs = $('handles');
    hs.textContent = '';
    layout.elements.forEach(function (e, i) {
      var d = SW.el('div', 'h' + (i === sel ? ' sel' : ''));
      d.style.left = e.x + '%'; d.style.top = e.y + '%'; d.style.width = e.w + '%'; d.style.height = e.h + '%';
      d.appendChild(SW.el('span', 'tag', KINDS[e.kind] + (e.kind === 'text' ? ': ' + e.text.slice(0, 24) : '')));
      var g = SW.el('div', 'grip');
      d.appendChild(g);
      d.addEventListener('pointerdown', function (ev) { startDrag(ev, i, ev.target === g); });
      hs.appendChild(d);
    });
  }

  var SNAP = 1.2;
  function snap(v, size, others, guide) {
    // Snap an element's start, centre or end to the screen centre/edges and to other elements' edges and centres.
    var targets = [0, 50, 100];
    others.forEach(function (o) { targets.push(o[0], o[0] + o[1] / 2, o[0] + o[1]); });
    var best = null;
    [[v, 0], [v + size / 2, size / 2], [v + size, size]].forEach(function (p) {
      targets.forEach(function (t) { var d = Math.abs(p[0] - t); if (d < SNAP && (!best || d < best.d)) best = { d: d, v: t - p[1], line: t }; });
    });
    var g = $(guide);
    if (best) { g.style.display = 'block'; if (guide === 'gx') { g.style.left = best.line + '%'; g.style.top = 0; g.style.bottom = 0; g.style.width = '1px'; } else { g.style.top = best.line + '%'; g.style.left = 0; g.style.right = 0; g.style.height = '1px'; } return best.v; }
    g.style.display = 'none';
    return Math.round(v * 2) / 2;
  }

  function startDrag(ev, i, resize) {
    ev.preventDefault(); ev.stopPropagation();
    select(i);
    var st = $('stage').getBoundingClientRect(), e0 = clone(layout.elements[i]), x0 = ev.clientX, y0 = ev.clientY, first = true;
    var others = layout.elements.filter(function (_, k) { return k !== i; });
    function move(m) {
      var dx = (m.clientX - x0) / st.width * 100, dy = (m.clientY - y0) / st.height * 100;
      change(function () {
        var e = layout.elements[i];
        if (resize) {
          e.w = Math.max(1, Math.min(100 - e.x, snap(e0.x + e0.w + dx, 0, others.map(function (o) { return [o.x, o.w]; }), 'gx') - e.x));
          e.h = Math.max(1, Math.min(100 - e.y, snap(e0.y + e0.h + dy, 0, others.map(function (o) { return [o.y, o.h]; }), 'gy') - e.y));
        } else {
          e.x = Math.max(0, Math.min(100 - e.w, snap(e0.x + dx, e.w, others.map(function (o) { return [o.x, o.w]; }), 'gx')));
          e.y = Math.max(0, Math.min(100 - e.h, snap(e0.y + dy, e.h, others.map(function (o) { return [o.y, o.h]; }), 'gy')));
        }
      }, !first);
      first = false;
    }
    function up() { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); $('gx').style.display = $('gy').style.display = 'none'; }
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  // ---- properties of the selected element
  function select(i) { sel = i; drawHandles(); drawProps(); }

  function field(label, input) { var r = SW.el('div', 'row'); r.appendChild(SW.el('label', '', label)); r.appendChild(input); return r; }
  function num(key, step, e) {
    var inp = document.createElement('input'); inp.type = 'number'; inp.step = step; inp.value = e[key];
    inp.onchange = function () { var v = parseFloat(inp.value); if (!isNaN(v)) change(function () { e[key] = v; }); };
    return inp;
  }
  function colour(key, e, defaultText) {
    var wrap = SW.el('span', 'row');
    var c = document.createElement('input'); c.type = 'color'; c.value = e[key] || '#ffffff';
    var d = document.createElement('input'); d.type = 'checkbox'; d.checked = !e[key]; d.title = defaultText;
    c.oninput = function () { change(function () { e[key] = c.value.toUpperCase(); d.checked = false; }, true); };
    c.onchange = function () { change(function () { e[key] = c.value.toUpperCase(); }); };
    d.onchange = function () { change(function () { e[key] = d.checked ? '' : c.value.toUpperCase(); }); };
    wrap.appendChild(c); wrap.appendChild(d); wrap.appendChild(SW.el('span', 'muted', defaultText));
    return wrap;
  }

  function drawProps() {
    var p = $('props');
    p.textContent = '';
    var e = layout.elements[sel];
    if (!e) { p.className = 'muted'; p.textContent = 'Click an element (Tab cycles). Arrows move it (Shift = 5%), Delete removes, Ctrl+D duplicates.'; return; }
    p.className = '';
    p.appendChild(SW.el('div', '', KINDS[e.kind]));
    if (e.kind === 'text' || e.kind === 'message') {
      var t = document.createElement('textarea'); t.value = e.text;
      t.placeholder = e.kind === 'message' ? 'Optional prefix before the message' : 'Text — use {placeholders}';
      t.oninput = function () { change(function () { e.text = t.value; }, true); };
      t.onfocus = function () { history.push(clone(layout)); };
      p.appendChild(t);
      var ph = document.createElement('select');
      ph.appendChild(new Option('Insert a placeholder…', ''));
      var vals = render.values();
      PLACEHOLDERS.forEach(function (k) { ph.appendChild(new Option('{' + k + '}' + (vals[k] ? '  — ' + String(vals[k]).slice(0, 24) : ''), k)); });
      ph.onchange = function () {
        if (!ph.value) return;
        var at = t.selectionStart || t.value.length, ins = '{' + ph.value + '}';
        change(function () { e.text = t.value.slice(0, at) + ins + t.value.slice(at); });
        ph.value = '';
      };
      p.appendChild(ph);
    }
    if (e.kind === 'image') {
      var im = document.createElement('select');
      im.appendChild(new Option(images.length ? 'Choose a holding image…' : 'No holding images yet (add them in Stagewatch)', ''));
      images.forEach(function (h) { im.appendChild(new Option(h.name, h.id)); });
      im.value = e.image;
      im.onchange = function () { change(function () { e.image = im.value; }); };
      p.appendChild(field('Image', im));
    }
    if (e.kind !== 'box' && e.kind !== 'image' && e.kind !== 'progress') p.appendChild(field('Size', num('size', 0.5, e)));
    p.appendChild(field('Colour', colour('color', e, e.kind === 'timer' || e.kind === 'progress' ? 'cue colour' : 'default')));
    p.appendChild(field('Fill', colour('bg', e, 'none')));
    if (e.kind !== 'box' && e.kind !== 'image' && e.kind !== 'progress') {
      var al = document.createElement('select');
      ['left', 'center', 'right'].forEach(function (a) { al.appendChild(new Option(a, a)); });
      al.value = e.align;
      al.onchange = function () { change(function () { e.align = al.value; }); };
      var b = document.createElement('input'); b.type = 'checkbox'; b.checked = e.bold;
      b.onchange = function () { change(function () { e.bold = b.checked; }); };
      var r = field('Align', al); r.appendChild(b); r.appendChild(SW.el('span', 'muted', 'bold'));
      p.appendChild(r);
    }
    var pos = SW.el('div', 'row');
    ['x', 'y', 'w', 'h'].forEach(function (k) { pos.appendChild(SW.el('label', '', k.toUpperCase())); pos.appendChild(num(k, 0.5, e)); });
    p.appendChild(pos);
    var acts = SW.el('div', 'row');
    [['Centre ↔', function () { e.x = (100 - e.w) / 2; }], ['Full width', function () { e.x = 3; e.w = 94; }], ['Forward', function () { move(1); }], ['Back', function () { move(-1); }],
     ['Duplicate', dup], ['Delete', remove]].forEach(function (a) {
      var btn = SW.el('button', '', a[0]);
      btn.onclick = function () { if (a[1] === dup || a[1] === remove) a[1](); else change(a[1]); };
      acts.appendChild(btn);
    });
    p.appendChild(acts);
  }

  function move(dir) {
    var j = sel + dir;
    if (j < 0 || j >= layout.elements.length) return;
    var a = layout.elements; var t = a[sel]; a[sel] = a[j]; a[j] = t; sel = j;
  }
  function dup() {
    var e = layout.elements[sel]; if (!e) return;
    change(function () { var c = clone(e); c.x = Math.min(100 - c.w, c.x + 2); c.y = Math.min(100 - c.h, c.y + 2); layout.elements.push(c); sel = layout.elements.length - 1; });
  }
  function remove() {
    if (sel < 0) return;
    change(function () { layout.elements.splice(sel, 1); sel = -1; });
  }
  function add(kind) {
    var e = { kind: kind, x: 30, y: 40, w: 40, h: 12, text: kind === 'text' ? '{clock}' : '', size: kind === 'timer' ? 20 : 6, color: '', bg: kind === 'box' ? '#1E293B' : '', align: 'center', bold: kind === 'timer', image: '' };
    if (kind === 'progress') { e.h = 2; e.y = 70; }
    if (kind === 'image') { e.w = 20; e.h = 20; e.x = 76; e.y = 76; }
    change(function () { if (kind === 'box') { layout.elements.unshift(e); sel = 0; } else { layout.elements.push(e); sel = layout.elements.length - 1; } });
  }

  function refresh(full) {
    render.setLayout(clone(layout));
    drawHandles();
    if (full !== false) drawProps();
    $('name').value = layout.name;
    $('bgc').value = layout.background || '#000000';
    $('font').value = layout.font || '';
    $('open').href = '/layout?name=' + encodeURIComponent(layout.name);
    $('undo').disabled = !history.length; $('redo').disabled = !future.length;
    document.title = (dirty() ? '• ' : '') + 'Screen designer — ' + layout.name;
  }

  // ---- server
  function api(path, body) {
    return fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Stagewatch-Pin': pin }, body: JSON.stringify(body) })
      .then(function (r) {
        return r.json().catch(function () { return {}; }).then(function (j) {
          if (r.status === 401) { $('pinbox').style.display = 'inline-flex'; throw new Error(j.error || 'PIN required'); }
          if (!r.ok) throw new Error(j.error || ('error ' + r.status));
          return j;
        });
      });
  }
  function names() {
    return fetch('/api/layouts', { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (j) {
      var pick = $('pick'); pick.textContent = '';
      pick.appendChild(new Option('Open…', ''));
      (j.layouts || []).forEach(function (n) { pick.appendChild(new Option(n, n)); });
      pick.appendChild(new Option('+ New layout', '+'));
      return j.layouts || [];
    }).catch(function () { return []; });
  }
  function open(n) {
    fetch('/api/layouts/' + encodeURIComponent(n), { cache: 'no-store' }).then(function (r) { return r.ok ? r.json() : null; }).then(function (l) {
      if (!l) return status('No layout called ' + n, true);
      layout = l; history = []; future = []; sel = -1; saved = JSON.stringify(layout);
      refresh(); status('Opened ' + n);
    });
  }
  function save() {
    layout.name = $('name').value.trim();
    api('/api/layouts/save', layout).then(function () {
      saved = JSON.stringify(layout); refresh(false); status('Saved — screens at /layout?name=' + layout.name + ' update now'); names();
    }).catch(function (e) { status(e.message, true); });
  }

  // ---- AI draft: previewed, then kept or discarded by you
  function draft() {
    var req = $('ask').value.trim();
    if (!req) return status('Describe the screen first', true);
    $('aibtn').disabled = true; status('The copilot is designing…');
    var current = draftBackup ? draftBackup : layout;
    api('/api/layouts/ai', { request: req, current: current.elements.length ? current : null }).then(function (j) {
      $('aibtn').disabled = false;
      var notes = $('notes'); notes.textContent = '';
      (j.notes || []).forEach(function (n) { notes.appendChild(SW.el('li', '', n)); });
      if (!j.layout) { status((j.notes && j.notes[0]) || 'No draft', true); return; }
      if (!draftBackup) draftBackup = clone(layout);
      j.layout.name = draftBackup.name;
      layout = j.layout; sel = -1;
      $('draft').style.display = 'block';
      refresh(); status('AI draft — keep it, ask again to refine, or discard');
    }).catch(function (e) { $('aibtn').disabled = false; status(e.message, true); });
  }
  $('keep').onclick = function () { history.push(draftBackup); draftBackup = null; $('draft').style.display = 'none'; refresh(); status('Kept — press Save to put it on screens'); };
  $('discard').onclick = function () { layout = draftBackup; draftBackup = null; $('draft').style.display = 'none'; refresh(); status('Draft discarded'); };

  // ---- wiring
  Object.keys(KINDS).forEach(function (k) { var b = SW.el('button', '', '+ ' + KINDS[k]); b.onclick = function () { add(k); }; $('adds').appendChild(b); });
  FONTS.forEach(function (f) { $('font').appendChild(new Option(f || 'Default font', f)); });
  $('font').onchange = function () { change(function () { layout.font = $('font').value; }); };
  $('bgc').onchange = function () { change(function () { layout.background = $('bgc').value.toUpperCase(); }); };
  $('name').onchange = function () { change(function () { layout.name = $('name').value.trim(); }); };
  $('aspect').onchange = function () { fitStage(); SW.store('sw-designer-aspect', $('aspect').value); };
  $('pick').onchange = function () {
    var v = $('pick').value; $('pick').value = '';
    if (!v) return;
    if (dirty() && !confirmLeave()) return;
    if (v === '+') { layout = starter('screen' + Math.floor(Math.random() * 90 + 10)); history = []; future = []; sel = -1; saved = ''; refresh(); return; }
    open(v);
  };
  $('save').onclick = save;
  $('del').onclick = function () {
    if (!$('del').dataset.sure) { $('del').dataset.sure = '1'; $('del').textContent = 'Really delete?'; setTimeout(function () { delete $('del').dataset.sure; $('del').textContent = 'Delete'; }, 3000); return; }
    api('/api/layouts/delete?name=' + encodeURIComponent(layout.name), {}).then(function () { status('Deleted ' + layout.name); saved = ''; names(); }).catch(function (e) { status(e.message, true); });
  };
  $('undo').onclick = undo; $('redo').onclick = redo;
  $('aibtn').onclick = draft;
  $('pinok').onclick = function () { pin = $('pin').value.trim(); SW.store('sw-pin', pin); $('pinbox').style.display = 'none'; status('Try again'); };
  $('stage').addEventListener('pointerdown', function (ev) { if (ev.target.id === 'handles' || ev.target.id === 'canvas') select(-1); });

  // Leaving with unsaved changes: an in-page warning, never a blocking dialog.
  var leaveArmed = false;
  function confirmLeave() {
    if (leaveArmed) { leaveArmed = false; return true; }
    leaveArmed = true; status('Unsaved changes — choose again to discard them, or Save first', true);
    setTimeout(function () { leaveArmed = false; }, 4000);
    return false;
  }

  document.addEventListener('keydown', function (ev) {
    var typing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
    var ctrl = ev.ctrlKey || ev.metaKey;
    if (ctrl && ev.key.toLowerCase() === 's') { ev.preventDefault(); save(); return; }
    if (typing) return;
    if (ctrl && ev.key.toLowerCase() === 'z') { ev.preventDefault(); ev.shiftKey ? redo() : undo(); return; }
    if (ctrl && ev.key.toLowerCase() === 'y') { ev.preventDefault(); redo(); return; }
    if (ctrl && ev.key.toLowerCase() === 'd') { ev.preventDefault(); dup(); return; }
    if (ev.key === 'Tab') { ev.preventDefault(); if (layout.elements.length) select((sel + (ev.shiftKey ? -1 : 1) + layout.elements.length) % layout.elements.length); return; }
    var e = layout.elements[sel];
    if (!e) return;
    if (ev.key === 'Delete' || ev.key === 'Backspace') { ev.preventDefault(); remove(); return; }
    var step = ev.shiftKey ? 5 : 0.5, d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[ev.key];
    if (d) { ev.preventDefault(); change(function () { e.x = Math.max(0, Math.min(100 - e.w, e.x + d[0])); e.y = Math.max(0, Math.min(100 - e.h, e.y + d[1])); }); }
  });

  window.addEventListener('resize', fitStage);
  var asp = SW.store('sw-designer-aspect'); if (asp) $('aspect').value = asp;
  fitStage();
  refresh();
  names().then(function (list) { var want = SW.q('name') || list[0]; if (want) open(want); });
  SW.connect({ detail: true, onState: function (s) { render.update(s); }, onConfig: function (c) { images = (c.holding || []).map(function (h) { return { id: h.id, name: h.name }; }); if (sel >= 0) drawProps(); } });
  setInterval(render.tick, 250);
})();
