// A designed screen: /layout?name=lobby. Reloads its design whenever the operator saves it in the designer.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var name = (SW.q('name') || '').trim();
  var r = SWLayout.create($('screen'));
  var version = null;

  function missing(text) { $('missing').textContent = text; $('missing').classList.toggle('hidden', !text); }

  function load() {
    if (!name) { missing('Add ?name= and a layout name to this address. Make layouts at /designer.'); return; }
    // The same pass as the screen's own connection: a viewing code or a crew link key.
    var pass = '?view=' + encodeURIComponent(SW.q('view') || SW.store('sw-view-code') || '') + (SW.q('key') ? '&key=' + encodeURIComponent(SW.q('key')) : '');
    fetch('/api/layouts/' + encodeURIComponent(name) + pass, { cache: 'no-store' })
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (l) {
        if (!l) { missing('No layout called “' + name + '”.'); return; }
        missing('');
        r.setLayout(l);
      })
      .catch(function () {});
  }

  SW.connect({
    detail: true,
    onState: function (s) {
      if (s.configVersion !== version) { version = s.configVersion; load(); }
      SW.holding(s);
      r.update(s);
    },
    onStatus: function (on) { $('offline').classList.toggle('hidden', on); }
  });
  SW.keepAwake();
  setInterval(r.tick, 250);   // countdowns between frames
})();
