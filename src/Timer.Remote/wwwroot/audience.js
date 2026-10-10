(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var qrDone = false;
  function render(s) {
    SW.holding(s);
    if (!qrDone) {
      var img = new Image();
      img.alt = 'QR code: ask a question';
      img.src = '/api/qr?text=' + encodeURIComponent(location.origin + '/ask');
      $('qr').appendChild(img);
      qrDone = true;
    }
    $('room').textContent = s.room ? s.room + ' — agenda' : 'Agenda';
    $('now').textContent = s.timeOfDay + (s.title ? '  ·  Now: ' + s.title : '');
    var t = s.timings || [];
    var cur = s.item >= 0 ? t[s.item] : null;
    var isBreak = cur && cur.kind === 'break' && s.mode === 'countdown' && s.state !== 'idle';
    $('breakBox').classList.toggle('hidden', !isBreak);
    if (isBreak) $('break').textContent = s.seconds > 0 ? SW.fmt(s.seconds) : 'Now';
    var ol = $('agenda');
    ol.textContent = '';
    t.forEach(function (r) {
      if (r.kind === 'buffer') return;
      var li = SW.el('li', r.status === 'done' ? 'done' : r.status === 'live' ? 'live' : '');
      li.appendChild(SW.el('span', 't', r.status === 'done' ? (r.actual || r.planned) : r.expected));
      li.appendChild(SW.el('span', '', r.title + (r.speaker ? ' — ' + r.speaker : '')));
      ol.appendChild(li);
    });
    $('live').classList.toggle('hidden', !s.qaLive);
    $('live').textContent = s.qaLive ? 'Now answering: “' + s.qaLive + '”' : '';
  }
  SW.connect({ detail: true, onState: render, onStatus: function (on) { $('offline').classList.toggle('hidden', on); } });
  SW.keepAwake();
})();
