// Remote control. Commands go to the app; the screen only ever shows what the app reports back.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var conn = null;
  var state = null;
  var config = null;

  function show(el, on) { el.classList.toggle('hidden', !on); }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
  }

  function flashError(text) {
    $('err').textContent = text;
    clearTimeout(flashError.t);
    flashError.t = setTimeout(function () { $('err').textContent = ''; }, 4000);
  }

  function start(pin) {
    show($('pinbox'), false);
    conn = SW.connect({
      control: true,
      pin: pin,
      onState: render,
      onConfig: function (c) { config = c; renderConfig(); },
      onAuth: function (a) {
        if (a.ok) { show($('app'), true); return; }
        show($('app'), false);
        show($('pinbox'), true);
        $('pinerr').textContent = a.error || 'Wrong PIN';
        SW.store('sw-pin', '');
      },
      onError: flashError,
      onStatus: function (online) {
        $('conn').textContent = online ? 'connected' : 'reconnecting…';
        $('conn').className = online ? '' : 'bad';
      }
    });
  }

  function render(s) {
    state = s;
    document.documentElement.style.setProperty('--timer', s.color || '#FFFFFF');
    $('timer').textContent = s.text;
    $('title').textContent = s.title || (s.mode === 'countup' ? 'Count up' : s.mode === 'tod' ? 'Time of day' : '');
    var hasBar = s.mode === 'countdown' && s.total > 0;
    show($('bar'), hasBar);
    if (hasBar) $('fill').style.width = (Math.max(0, Math.min(1, s.seconds / s.total)) * 100).toFixed(2) + '%';
    $('endtime').textContent = s.endTime;
    $('schedule').textContent = s.schedule;
    $('next').textContent = s.next ? 'Next: ' + s.next : '';
    $('msgnow').textContent = s.message;
    $('msgnow').style.color = SW.urgencyColors[s.urgency] || '';

    var counting = s.state === 'running' || (s.state === 'expired' && s.seconds < 0);
    $('toggle').textContent = counting ? 'Pause' : 'Start';
    $('blackout').classList.toggle('on', s.blackout);
    $('blackout').textContent = s.blackout ? 'Blackout ON' : 'Blackout';
    document.querySelectorAll('[data-mode]').forEach(function (b) { b.classList.toggle('on', b.dataset.mode === s.mode); });

    document.querySelectorAll('#rundown .item').forEach(function (b, i) {
      b.classList.toggle('current', i === s.item);
      b.classList.toggle('done', s.item >= 0 && i < s.item);
    });
  }

  function renderConfig() {
    var bank = $('bank'); bank.textContent = '';
    (config.messages || []).forEach(function (m) {
      var b = el('button', '', m.label);
      b.onclick = function () { conn.send('message', m.text, m.urgency, m.flash); };
      bank.appendChild(b);
    });

    var presets = $('presets'); presets.textContent = '';
    (config.presets || []).forEach(function (p) {
      var b = el('button', '', p.label);
      b.appendChild(el('small', '', p.seconds < 0 ? 'count up' : SW.fmt(p.seconds)));
      b.onclick = function () { conn.send('preset', p.slot); };
      b.ondblclick = function () { conn.send('preset', p.slot, 'start'); };
      presets.appendChild(b);
    });

    var rd = $('rundown'); rd.textContent = '';
    (config.rundown || []).forEach(function (r, i) {
      var b = el('button', 'item');
      b.appendChild(el('span', 'n', String(i + 1)));
      var t = el('span', '', r.title);
      if (r.speaker) t.appendChild(el('small', '', r.speaker));
      b.appendChild(t);
      b.appendChild(el('span', 'n', SW.fmt(r.seconds)));
      b.onclick = function () { conn.send('goto', i + 1); };
      rd.appendChild(b);
    });
    show($('rundownWrap'), (config.rundown || []).length > 0);
    if (state) render(state);
  }

  document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-cmd]');
    if (b && conn) {
      if (b.dataset.arg !== undefined) conn.send(b.dataset.cmd, b.dataset.arg);
      else conn.send(b.dataset.cmd);
    }
    var m = e.target.closest('[data-mode]');
    if (m && conn) conn.send('mode', m.dataset.mode);
  });

  $('toggle').onclick = function () { if (conn) conn.send('toggle'); };
  $('blackout').onclick = function () { if (conn) conn.send('blackout', 'toggle'); };
  $('send').onclick = function () {
    var text = $('msg').value.trim();
    if (!text) return;
    conn.send('message', text, $('urgency').value, $('flash').value);
  };
  $('msg').addEventListener('keydown', function (e) { if (e.key === 'Enter') $('send').click(); });

  $('pinok').onclick = function () {
    var pin = $('pin').value.trim();
    SW.store('sw-pin', pin);
    $('pinerr').textContent = '';
    if (conn) { location.reload(); return; }
    start(pin);
  };
  $('pin').addEventListener('keydown', function (e) { if (e.key === 'Enter') $('pinok').click(); });

  // PIN from the link (?pin=1234) or remembered on this device; otherwise try without one.
  var fromUrl = new URLSearchParams(location.search).get('pin');
  if (fromUrl) SW.store('sw-pin', fromUrl);
  start(fromUrl || SW.store('sw-pin') || '');
})();
