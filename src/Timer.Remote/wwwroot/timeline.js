// Timeline: the running order as blocks on a time axis, with a live now-line and groups above.
// Blocks use expected times (delays included). ?span=3h shows a window around now (20% past, 80% ahead);
// without it the whole show fits the screen. Positions are anchored to the show clock, so the layout is rebuilt
// only when the plan changes; the now-line moves on its own between frames.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var spanParam = SW.q('span');
  var spanSec = (function () {
    if (!spanParam) return 0;
    var m = /^(\d+(?:\.\d+)?)\s*(h|m)?$/i.exec(spanParam.trim());
    if (!m) return 0;
    var v = parseFloat(m[1]);
    return (m[2] || 'h').toLowerCase() === 'm' ? v * 60 : v * 3600;
  })();
  var last = null, lastKey = '', layout = null;

  function clockText(sec, withSeconds) {
    sec = ((Math.floor(sec) % 86400) + 86400) % 86400;
    var h = Math.floor(sec / 3600), m = Math.floor(sec / 60) % 60, s = sec % 60;
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return p(h) + ':' + p(m) + (withSeconds ? ':' + p(s) : '');
  }

  /** Absolute show-clock seconds for every item and group, from this frame. */
  function absolute(s) {
    var base = s.clock >= 0 ? s.clock : 0;
    var items = (s.timings || []).filter(function (r) { return r['in'] !== null && r.endIn !== null; })
      .map(function (r) { return { r: r, a: base + r['in'], b: base + r.endIn }; });
    var groups = (s.groups || []).filter(function (g) { return g['in'] !== null && g.endIn !== null; })
      .map(function (g) { return { g: g, a: base + g['in'], b: base + g.endIn }; });
    return { base: base, items: items, groups: groups };
  }

  function build(s, abs) {
    var lane = $('lane');
    lane.textContent = '';
    if (!abs.items.length) {
      var e = SW.el('div', '', 'The running order appears here once the show has a start time.');
      e.id = 'empty';
      lane.appendChild(e);
      return null;
    }
    var from, to;
    if (spanSec > 0) { from = abs.base - spanSec * 0.2; to = abs.base + spanSec * 0.8; }
    else {
      from = Math.min.apply(null, abs.items.map(function (x) { return x.a; }));
      to = Math.max.apply(null, abs.items.map(function (x) { return x.b; }));
    }
    if (to - from < 60) to = from + 60;
    var W = to - from;
    function x(sec) { return ((sec - from) / W) * 100; }

    var step = W > 6 * 3600 ? 3600 : W > 2 * 3600 ? 1800 : W > 3600 ? 900 : 300;
    for (var a = Math.ceil(from / step) * step; a <= to; a += step) {
      var line = SW.el('div', 'tickline'); line.style.left = x(a) + '%'; lane.appendChild(line);
      var lab = SW.el('div', 'tick', clockText(a)); lab.style.left = x(a) + '%'; lane.appendChild(lab);
    }

    abs.groups.forEach(function (o) {
      var left = Math.max(0, x(o.a)), right = Math.min(100, x(o.b));
      if (right <= 0 || left >= 100) return;
      var d = SW.el('div', 'grp' + (/^over/.test(o.g.targetText || '') ? ' over' : ''));
      d.style.left = left + '%'; d.style.width = (right - left) + '%';
      d.appendChild(document.createTextNode(o.g.name));
      if (o.g.targetText) d.appendChild(SW.el('small', '', o.g.targetText));
      lane.appendChild(d);
    });

    abs.items.forEach(function (o) {
      var r = o.r;
      var left = Math.max(0, x(o.a)), right = Math.min(100, x(o.b));
      if (right <= 0 || left >= 100) return;
      var d = SW.el('div', 'it ' + r.status + (r.kind === 'break' ? ' break' : '') + (r.conflict ? ' conflict' : ''));
      d.style.left = left + '%'; d.style.width = Math.max(0.3, right - left) + '%';
      d.title = r.title;
      var tt = SW.el('div', 't', r.title);
      if (r.labels && r.labels.length) tt.appendChild(SW.labelChips(r.labels, s.labelColors));
      d.appendChild(tt);
      if (r.speaker) d.appendChild(SW.el('div', 's', r.speaker));
      d.appendChild(SW.el('div', 'tm', (r.status === 'done' ? (r.actual || r.planned) : r.expected) + '–' + r.expectedEnd));
      lane.appendChild(d);
    });

    var nl = SW.el('div'); nl.id = 'nowline';
    nl.appendChild(SW.el('span'));
    lane.appendChild(nl);
    return { from: from, W: W, line: nl };
  }

  function tick() {
    if (last && layout) {
      var now = (last.clock >= 0 ? last.clock : 0) + SW.since();
      var pos = ((now - layout.from) / layout.W) * 100;
      layout.line.style.display = pos < 0 || pos > 100 ? 'none' : '';
      layout.line.style.left = Math.max(0, Math.min(100, pos)) + '%';
      layout.line.firstChild.textContent = clockText(now, true);
    }
    requestAnimationFrame(tick);
  }

  function render(s) {
    last = s;
    SW.holding(s);
    $('room').textContent = s.room ? s.room + ' — timeline' : 'Timeline';
    $('now').textContent = s.title ? 'Now: ' + s.title : '';
    var st = $('status');
    st.textContent = s.offset === null ? '' : Math.abs(s.offset) < 60 ? 'On time' : (s.offset > 0 ? 'Running ' + SW.span(s.offset) + ' late' : SW.span(-s.offset) + ' ahead');
    st.className = s.offset === null ? '' : s.offset >= 60 ? 'pill late' : s.offset <= -60 ? 'pill early' : 'pill';
    var abs = absolute(s);
    // Rebuild when the plan moves by 15 s or more, a status changes, or (with a window) every 30 s as now advances.
    var key = JSON.stringify(s.labelColors || {}) + JSON.stringify(abs.items.map(function (o) { return [o.r.title, o.r.status, o.r.conflict, o.r.labels, Math.round(o.a / 15), Math.round(o.b / 15)]; }))
      + JSON.stringify(abs.groups.map(function (o) { return [o.g.name, o.g.targetText, Math.round(o.a / 15), Math.round(o.b / 15)]; }))
      + (spanSec > 0 ? '|' + Math.floor(abs.base / 30) : '');
    if (key !== lastKey) { lastKey = key; layout = build(s, abs); }
  }

  SW.connect({ detail: true, onState: render, onStatus: function (on) { $('offline').classList.toggle('hidden', on); } });
  SW.keepAwake();
  requestAnimationFrame(tick);
})();
