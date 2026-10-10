// Studio clock: a broadcast-style ring of 60 seconds around the time of day, in the show's time zone.
// The time comes from Stagewatch (not this device's clock) and counts on locally between frames.
// ?h=12 for a 12-hour clock · ?timer=0 hides the show timer line · ?date=0 hides the date.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var twelve = SW.q('h') === '12';
  var showTimer = SW.q('timer') !== '0';
  var showDate = SW.q('date') !== '0';
  // ?tz=Europe/Paris: another time zone (from Stagewatch's UTC, so this device's clock still doesn't matter).
  var tz = SW.q('tz');
  var tzParts = null;
  if (tz) {
    try { tzParts = new Intl.DateTimeFormat('en-GB', { timeZone: tz, hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }); }
    catch (e) { tzParts = null; }
  }
  function secondsOfDay(s) {
    if (tzParts && s.utc) {
      var d = new Date(Math.floor(s.utc + SW.since()) * 1000);
      var p = {};
      tzParts.formatToParts(d).forEach(function (x) { p[x.type] = x.value; });
      return (+p.hour) * 3600 + (+p.minute) * 60 + (+p.second);
    }
    return Math.floor(s.clock + SW.since()) % 86400;
  }
  var last = null;

  // 60 dots, larger at the quarter hours; built once.
  var ring = $('ring'), dots = [];
  for (var i = 0; i < 60; i++) {
    var a = (i / 60) * Math.PI * 2 - Math.PI / 2;
    var c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    c.setAttribute('cx', (Math.cos(a) * 90).toFixed(2));
    c.setAttribute('cy', (Math.sin(a) * 90).toFixed(2));
    c.setAttribute('r', i % 5 === 0 ? 3.6 : 2.4);
    c.setAttribute('class', 'dot' + (i % 15 === 0 ? ' q' : ''));
    ring.appendChild(c);
    dots.push(c);
  }

  function p2(n) { return (n < 10 ? '0' : '') + n; }
  var lastSec = -1;
  function tick() {
    if (last && last.clock >= 0) {
      var t = secondsOfDay(last);
      if (t !== lastSec) {
        lastSec = t;
        var h = Math.floor(t / 3600), m = Math.floor(t / 60) % 60, s = t % 60;
        var hh = twelve ? ((h % 12) || 12) : h;
        $('time').textContent = (twelve ? hh : p2(hh)) + ':' + p2(m);
        $('secs').textContent = p2(s) + (twelve ? (h < 12 ? ' am' : ' pm') : '');
        for (var i = 0; i < 60; i++) {
          var cls = 'dot' + (i % 15 === 0 ? ' q' : '') + (i <= s ? ' on' : '') + (i === s ? ' now' : '');
          if (dots[i].getAttribute('class') !== cls) dots[i].setAttribute('class', cls);
        }
      }
    }
    requestAnimationFrame(tick);
  }

  function render(s) {
    last = s;
    SW.holding(s);
    $('date').textContent = showDate ? (tzParts ? tz.replace(/_/g, ' ') : (s.date || '')) : '';
    var timerOn = showTimer && s.mode !== 'tod' && s.state !== 'idle' && !s.blackout;
    $('timer').classList.toggle('hidden', !timerOn);
    $('title').classList.toggle('hidden', !timerOn || !s.title);
    if (timerOn) {
      $('timer').textContent = s.text;
      $('timer').style.color = s.color || '';
      $('title').textContent = s.title || '';
    }
  }

  SW.connect({ onState: render, onStatus: function (on) { $('offline').classList.toggle('hidden', on); } });
  SW.keepAwake();
  requestAnimationFrame(tick);
})();
