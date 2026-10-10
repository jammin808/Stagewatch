// Confidence-monitor (speaker) view. Pure renderer: every frame from the app replaces the last.
// The only thing it ever sends back is an acknowledgement ("I've seen your cue").
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var stage = $('stage'), timer = $('timer');
  var lastFit = '';
  var conn = null;
  var target = SW.q('for') || 'speaker';
  var nudgeId = 0;

  function show(el, on) { el.classList.toggle('hidden', !on); }

  function setFlash(el, rate) {
    el.classList.toggle('flash-slow', rate === 'slow');
    el.classList.toggle('flash-fast', rate === 'fast');
  }

  function fitTimer(text, extras) {
    // Size by character count so "00:00" and "00:00:00" both fill the width without reflow jitter,
    // and give up height for each extra line shown so nothing is pushed off screen.
    var key = text.length + '/' + extras;
    if (key === lastFit) return;
    lastFit = key;
    var vw = Math.min(92 / (Math.max(text.length, 4) * 0.58), 34);
    var vh = Math.max(20, 50 - extras * 6.5);
    timer.style.fontSize = 'min(' + vw.toFixed(2) + 'vw, ' + vh.toFixed(1) + 'vh)';
  }

  // ---- holographic style: ?style=holo|flat on the address, otherwise what Stagewatch is set to.
  // The 3D renderer is loaded only when first needed, and any failure drops back to this flat view for good.
  var holo = null, holoFailed = false, holoLoading = false;
  function wantHolo(s) {
    var forced = SW.q('style');
    if (forced === 'flat' || forced === 'standard') return false;
    return !holoFailed && (forced === 'holo' || s.style === 'holo');
  }
  function syncHolo(s) {
    var want = wantHolo(s);
    if (want && !holo && !holoLoading) {
      holoLoading = true;
      var go = function () {
        holoLoading = false;
        if (!window.StagewatchHolo || !StagewatchHolo.supported()) { holoFailed = true; return; }
        $('holo').classList.remove('hidden');
        holo = StagewatchHolo.start($('holo'), Object.assign(urlOptions(), {
          onFail: function () { holoFailed = true; holo = null; document.body.classList.remove('holo'); $('holo').classList.add('hidden'); }
        }));
        if (holo.failed) { holo = null; return; }
        document.body.classList.add('holo');
        if (lastState) holo.update(lastState);
      };
      if (window.StagewatchHolo) go();
      else {
        var tag = document.createElement('script');
        tag.src = '/holo.bundle.js';
        tag.onload = go;
        tag.onerror = function () { holoLoading = false; holoFailed = true; };
        document.head.appendChild(tag);
      }
    } else if (!want && holo) {
      holo.stop();
      holo = null;
      document.body.classList.remove('holo');
      $('holo').classList.add('hidden');
    }
    if (holo) holo.update(s);
  }
  var lastState = null;

  // Per-screen overrides on the address: ?scene=holo|egg, ?anim=0|1, ?particles=0|1, ?floor=0|1.
  // Anything not given follows the show's settings.
  function urlOptions() {
    var q = new URLSearchParams(location.search), o = {};
    if (q.get('scene') === 'egg' || q.get('scene') === 'holo') o.scene = q.get('scene');
    if (q.get('anim') !== null) o.animate = q.get('anim') !== '0';
    if (q.get('particles') !== null) o.particles = q.get('particles') !== '0';
    if (q.get('floor') !== null) o.floor = q.get('floor') !== '0';
    return o;
  }

  function render(s) {
    lastState = s;
    SW.holding(s);
    syncHolo(s);
    stage.classList.toggle('blackout', s.blackout);
    show($('watermark'), !!s.watermark);
    $('watermark').textContent = s.watermark || '';
    document.documentElement.style.setProperty('--timer', s.color || '#FFFFFF');
    document.documentElement.style.setProperty('--msg', SW.urgencyColors[s.urgency] || '#E2E8F0');
    show($('preview'), !!s.preview);

    // Speaker text: calm ("12 MIN"), honest (never a stretched clock) or empty for progress-only.
    var text = s.mode === 'countdown' ? (s.speakerText !== undefined ? s.speakerText : s.text) : s.text;
    var progressOnly = s.mode === 'countdown' && text === '';
    show(timer, s.show.timer && !progressOnly);
    if (timer.textContent !== text) timer.textContent = text;
    setFlash(timer, s.flashTimer ? (s.flash === 'off' ? 'slow' : s.flash) : 'off');

    // A cue that doesn't rely on colour: label badge plus a coloured frame around the screen.
    var cueOn = (s.show.cue || progressOnly) && !!s.cue;
    show($('cue'), cueOn);
    $('cue').textContent = s.cue || '';
    stage.classList.toggle('cueing', s.phase !== 'normal' && s.mode === 'countdown' && s.state !== 'idle');

    show($('title'), s.show.title && !!s.title);
    $('title').textContent = s.title;

    var hasBar = (s.show.progress || progressOnly) && s.mode === 'countdown' && s.total > 0;
    show($('bar'), hasBar);
    $('bar').classList.toggle('big', progressOnly);
    if (hasBar) $('fill').style.width = (Math.max(0, Math.min(1, s.seconds / s.total)) * 100).toFixed(2) + '%';

    show($('finishby'), s.show.finishBy && !!s.finishBy);
    $('finishby').textContent = s.finishBy || '';
    show($('endtime'), s.show.endTime && !!s.endTime && !s.show.finishBy);
    $('endtime').textContent = s.endTime;
    // An extra timer the operator put on stage (VT, changeover...): one line under the clock.
    show($('aux'), !!s.auxLine);
    if ($('aux').textContent !== (s.auxLine || '')) $('aux').textContent = s.auxLine || '';

    // Stable layout: the message area keeps its space even when empty, so the timer never jumps.
    var hasMsg = s.show.message && !!s.message;
    $('message').classList.toggle('hidden', !hasMsg && !(s.stable && s.show.message));
    $('message').classList.toggle('reserve', !hasMsg);
    $('message').textContent = hasMsg ? s.message : ' ';
    setFlash($('message'), hasMsg ? s.flash : 'off');
    renderAck(s, hasMsg);

    var sl = s.slides;
    show($('slides'), s.show.slides && !!sl);
    if (sl) $('slides').textContent = 'Slide ' + sl.slide + ' / ' + sl.count + (sl.next ? '  ·  Next: ' + sl.next : '');

    var vt = s.playout;
    show($('vt'), s.show.playout && !!vt);
    if (vt) $('vt').textContent = 'VT ' + SW.fmt(vt.remaining) + (vt.running ? '' : ' (paused)');

    show($('next'), s.show.next && !!s.next);
    $('next').textContent = s.next ? 'Next: ' + s.next : '';

    show($('schedule'), s.show.schedule && !!s.schedule);
    $('schedule').textContent = s.schedule;

    show($('tod'), s.show.timeOfDay && s.mode !== 'tod');
    $('tod').textContent = s.timeOfDay;

    renderNudge(s);

    var extras = ['title', 'cue', 'bar', 'finishby', 'endtime', 'slides', 'vt', 'next', 'schedule', 'tod']
      .filter(function (id) { return !$(id).classList.contains('hidden'); }).length
      + ($('message').classList.contains('hidden') ? 0 : 2.5) + ($('ackbox').classList.contains('hidden') ? 0 : 1);
    fitTimer(text || '00:00', extras);
  }

  function renderAck(s, hasMsg) {
    var box = $('ackbox');
    if (!hasMsg || !s.messageAck) { show(box, false); return; }
    show(box, true);
    if (s.messageAck === 'sent') {
      if (!box.querySelector('button') || box.dataset.id !== String(s.messageId)) {
        box.textContent = '';
        var b = SW.el('button', '', 'Got it ✓');
        b.onclick = function (e) { e.stopPropagation(); if (conn) conn.send('ack', s.messageId, 'done'); };
        box.appendChild(b);
        box.dataset.id = String(s.messageId);
      }
    } else {
      box.dataset.id = '';
      box.innerHTML = '';
      box.appendChild(SW.el('span', 'ok', '✓ acknowledged'));
    }
  }

  // Discreet nudges for this screen's person (wrap-up, look at monitor…): full-screen, tap to acknowledge.
  function renderNudge(s) {
    var mine = (s.signals || []).filter(function (g) { return g.kind === 'nudge' && g.target === target && g.state === 'sent'; });
    var g = mine[mine.length - 1];
    show($('nudge'), !!g);
    if (!g) { nudgeId = 0; return; }
    $('nudgeText').textContent = g.text || 'Please look at the monitor';
    if (g.id !== nudgeId) { nudgeId = g.id; SW.buzz([300, 120, 300]); }
    $('nudge').onclick = function (e) { e.stopPropagation(); if (conn) conn.send('ack', g.id, 'done'); };
  }

  conn = SW.connect({
    onState: render,
    onStatus: function (online) { show($('offline'), !online); }
  });
  SW.keepAwake();

  document.addEventListener('click', function () {
    var el = document.documentElement;
    var req = el.requestFullscreen || el.webkitRequestFullscreen;
    if (req && !document.fullscreenElement) { try { req.call(el); } catch (e) {} }
    $('hint').classList.add('hidden');
  });
  setTimeout(function () { $('hint').classList.add('hidden'); }, 6000);
})();
