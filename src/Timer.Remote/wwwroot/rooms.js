(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  function card(r) {
    var risk = r.hardOutMargin !== null && r.hardOutMargin < 0;
    var late = r.offset !== null && r.offset > 60;
    var c = SW.el('div', 'room phase-' + r.phase + (risk ? ' risk' : late ? ' late' : '') + (r.stale ? ' stale' : ''));
    var h = SW.el('h3', '', r.room);
    h.appendChild(SW.el('span', 'muted', r.self ? 'this room' : r.state));
    c.appendChild(h);
    c.appendChild(SW.el('div', 't', r.text || '--:--'));
    c.appendChild(SW.el('div', 'title', r.title || '—'));
    var m = SW.el('div', 'meta');
    if (r.offset !== null) m.appendChild(SW.el('span', 'pill ' + (r.offset > 60 ? 'late' : r.offset < -60 ? 'early' : ''),
      Math.abs(r.offset) < 30 ? 'on time' : (r.offset > 0 ? '+' : '−') + SW.fmt(Math.abs(r.offset))));
    if (r.hardOut) m.appendChild(SW.el('span', 'pill ' + (risk ? 'late' : ''), r.hardOut));
    if (r.openSignals) m.appendChild(SW.el('span', 'pill warn', r.openSignals + ' cue' + (r.openSignals > 1 ? 's' : '') + ' waiting'));
    c.appendChild(m);
    if (!r.self && r.webPort) {
      var a = SW.el('a', '', 'Open producer view →');
      a.href = 'http://' + r.host + ':' + r.webPort + '/producer';
      a.target = '_blank';
      c.appendChild(a);
    }
    return c;
  }
  function render(s) {
    $('clock').textContent = s.timeOfDay;
    var rooms = [{ room: s.room || 'This room', host: location.hostname, webPort: 0, title: s.title, text: s.text, state: s.state, phase: s.phase,
      offset: s.offset, hardOut: s.hardOut, hardOutMargin: s.hardOutMargin, openSignals: (s.signals || []).filter(function (g) { return g.state === 'sent'; }).length, stale: false, self: true }]
      .concat(s.rooms || []);
    var grid = $('grid');
    grid.textContent = '';
    rooms.forEach(function (r) { grid.appendChild(card(r)); });
    $('empty').classList.toggle('hidden', (s.rooms || []).length > 0);
  }
  SW.connect({ detail: true, onState: render, onStatus: function (on) { $('offline').classList.toggle('hidden', on); } });
})();
