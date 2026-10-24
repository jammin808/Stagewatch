// A window Stagewatch controls (/w/<token>). Stagewatch says what the window shows; the window shows exactly that and
// says back what it shows. Built like a game's presentation loop:
//  • one link to Stagewatch; every page inside takes its frames from here (SWShell), stamped with when they arrived;
//  • two frames: the next page loads behind the one on screen and is swapped in only once it has drawn its first frame
//    (and loaded) — never a blank, half-built or error page on the screen; a page that won't load is retried while the
//    old one stays up, and the reason goes back to the operator;
//  • the holding page, black and the link warning are drawn here, so they show with no page and no network;
//  • a dropped link never leaves a confident stale clock: the window shows its holding page or the NOT CONNECTED
//    warning, as the operator chose, and reconnects on its own with a jittered backoff (a room of windows doesn't
//    reconnect in one burst when Stagewatch restarts).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var html = document.documentElement;
  var token = (location.pathname.split('/')[2] || '').replace(/[^a-z0-9]/g, '');
  var frames = [$('fa'), $('fb')];
  var holdEl = $('hold'), holdImg = holdEl.querySelector('img'), holdWords = holdEl.querySelector('.words'), holdIdle = holdEl.querySelector('.idle');

  var STALE_MS = 6000, LOAD_MS = 8000, SETTLE_MS = 3000;
  var want = null;              // the window as Stagewatch last said it (content, hold, rev…), with when it arrived (at)
  var front = -1;               // the frame on screen (-1: none)
  var frontContent = '';        // the page in it
  var loading = null;           // the page on its way: { i, content, ready, loaded, timer }
  var latest = null, latestAt = 0;
  var subs = [];                // pages listening: { win, fn }
  var error = '', retries = 0, retryTimer = 0, swapMs = 0;
  var ws = null, delay = 500, lastHeard = Date.now(), lost = false, gone = false;

  function nowAbs() { return performance.timeOrigin ? performance.timeOrigin + performance.now() : Date.now(); }
  function isPage(content) { return !!content && content !== 'holding' && content !== 'black'; }

  // ---------------------------------------------------------------- what the pages inside see
  function call(fn, msg, at) { try { fn(msg, at); } catch (e) { /* one page's fault never stops another page or the window */ } }
  window.SWShell = {
    token: token,
    /** A page asks for frames; it gets the newest at once (so it draws before it is shown), then each one. */
    subscribe: function (win, fn) {
      subs = subs.filter(function (s) { return s.win !== win; });   // a frame holds one page: its old one is gone
      subs.push({ win: win, fn: fn });
      if (latest) call(fn, latest, latestAt);
    },
    /** A page has drawn its first frame. */
    ready: function (win) {
      var L = loading;
      if (!L || frames[L.i].contentWindow !== win) return;
      L.ready = true;
      if (L.loaded) swapIn(L);
    },
    /** What a page may send (cue acknowledgements, audience questions): the server holds a window to public verbs. */
    send: function (obj) {
      if (ws && ws.readyState === 1 && obj && typeof obj.cmd === 'string') ws.send(JSON.stringify({ cmd: obj.cmd, args: obj.args || [] }));
    },
    /** For people and tests: what is on screen. */
    state: function () {
      return { content: shownContent(), page: frontContent, hold: holdShown(), link: !lost, rev: want ? want.rev : 0, error: error, loading: loading ? loading.content : '', swapMs: swapMs };
    }
  };

  // ---------------------------------------------------------------- the two frames
  /** Frame i off screen and its page deaf (it is about to be given another page). */
  function hide(i) {
    var f = frames[i], w = f.contentWindow;
    subs = subs.filter(function (s) { return s.win !== w; });
    f.onload = null;
    f.style.transition = 'none'; f.style.opacity = '0'; f.style.visibility = 'hidden'; f.style.zIndex = '1';
  }
  /** Frame i off screen and emptied, so a page nobody sees uses no CPU, GPU or socket. */
  function retire(i) {
    hide(i);
    try { frames[i].contentWindow.location.replace('about:blank'); } catch (e) { /* already blank */ }
  }

  function stopLoading() {
    if (!loading) return;
    clearTimeout(loading.timer);
    var i = loading.i;
    loading = null;
    if (i !== front) retire(i);
  }

  function load(page) {
    stopLoading();
    clearTimeout(retryTimer);
    var i = front === 0 ? 1 : 0;
    hide(i);
    var L = loading = { i: i, content: page, ready: false, loaded: false, timer: 0 };
    frames[i].onload = function () {
      if (loading !== L) return;
      var ok = false;
      try { ok = !!(frames[i].contentWindow && frames[i].contentWindow.SW); } catch (e) { ok = false; }
      if (!ok) { failed(L, "isn't a Stagewatch page"); return; }
      L.loaded = true;
      if (L.ready) { swapIn(L); return; }
      // A page that never asks for frames (a form) is shown once it has had time to settle.
      clearTimeout(L.timer);
      L.timer = setTimeout(function () { swapIn(L); }, SETTLE_MS);
    };
    // A page that drew its frame but is still fetching an image is shown at the deadline; one that drew nothing is not.
    L.timer = setTimeout(function () { if (L.ready) swapIn(L); else failed(L, "didn't load in " + LOAD_MS / 1000 + ' s'); }, LOAD_MS);
    frames[i].src = page;
  }

  /** Two painted frames before the reveal, with a timer behind them for a browser that throttles animation frames. */
  function afterPaint(fn) {
    var done = false;
    function go() { if (!done) { done = true; fn(); } }
    requestAnimationFrame(function () { requestAnimationFrame(go); });
    setTimeout(go, 150);
  }

  function swapIn(L) {
    if (loading !== L) return;
    clearTimeout(L.timer);
    loading = null;
    var i = L.i, old = front, f = frames[i];
    var fade = Math.max(0, Math.min(5000, (want && want.fadeMs) | 0));
    front = i;
    frontContent = L.content;
    error = ''; retries = 0;
    swapMs = Math.max(0, Math.round(nowAbs() - (want && want.at ? want.at : nowAbs())));
    afterPaint(function () {
      if (front !== i) return;
      f.style.transition = 'none';
      f.style.zIndex = '3'; f.style.visibility = 'visible';
      void f.offsetWidth;                                     // the fade starts from what is on screen now
      f.style.transition = fade ? 'opacity ' + fade + 'ms linear' : 'none';
      f.style.opacity = '1';
      paint(); report();
      setTimeout(function () {
        if (front === i) f.style.zIndex = '2';
        if (old >= 0 && old !== front && !(loading && loading.i === old)) retire(old);
      }, fade + 50);
    });
  }

  function failed(L, why) {
    if (loading !== L) return;
    clearTimeout(L.timer);
    loading = null;
    retire(L.i);
    error = why;
    paint(); report();
    // Try again while Stagewatch still wants this page, backing off to 30 s; whatever is on screen stays up.
    retries++;
    clearTimeout(retryTimer);
    retryTimer = setTimeout(function () {
      if (want && want.content === L.content && frontContent !== L.content && !loading) load(L.content);
    }, Math.min(30000, 2000 * Math.pow(2, retries - 1)));
  }

  /** Bring the screen to what Stagewatch last said. */
  function apply() {
    if (!want) { paint(); return; }
    if (!isPage(want.content)) {
      // The holding page and black are the window's own: no page behind them.
      stopLoading();
      clearTimeout(retryTimer);
      if (front >= 0) { retire(front); front = -1; frontContent = ''; }
      error = '';
      paint(); report();
      return;
    }
    if (front >= 0 && frontContent === want.content) { stopLoading(); error = ''; paint(); report(); return; }
    if (loading && loading.content === want.content) { paint(); return; }
    load(want.content);
    paint();
  }

  // ---------------------------------------------------------------- the window's own layers
  function holdShown() {
    if (!want) return true;                                   // nothing said yet: the holding page
    if (want.hold || want.content === 'holding') return true;
    if (lost && want.onLoss !== 'warn') return true;          // never a confident stale clock
    return isPage(want.content) && front < 0;                 // the first page hasn't drawn yet, or wouldn't load
  }
  function shownContent() {
    if (!want) return 'holding';
    if (!isPage(want.content)) return want.content;
    return front >= 0 ? frontContent : 'holding';
  }

  var imageId = '', imageWanted = '';
  function showImage(id, fill) {
    holdImg.style.objectFit = fill ? 'cover' : 'contain';
    imageWanted = id;
    if (!id) { imageId = ''; holdImg.style.display = 'none'; return; }
    if (id === imageId) { holdImg.style.display = 'block'; return; }
    // Decoded before it is shown: never half an image on a screen.
    var im = new Image();
    im.src = '/api/holding/' + encodeURIComponent(id);
    (im.decode ? im.decode() : Promise.resolve()).then(function () {
      if (imageWanted !== id) return;
      holdImg.src = im.src; imageId = id; holdImg.style.display = 'block';
    }).catch(function () { /* a missing image leaves the holding page black */ });
  }

  function paint() {
    var hold = holdShown();
    var blackout = !!(latest && latest.blackout && !lost);
    $('black').style.display = blackout || (want && want.content === 'black' && !hold) ? 'block' : 'none';
    holdEl.style.display = hold ? 'flex' : 'none';
    if (hold) {
      var words = want ? want.holdText || '' : '';
      var image = words ? '' : (want && want.holdImage) || (latest && !latest.blackout && latest.holding) || '';
      holdWords.textContent = words;
      showImage(image, !!(latest && latest.holdingFill));
      // An unused window says its name, faintly, so it can be found; a window put on hold shows nothing it wasn't given.
      var idle = !words && !image && (!want || (want.content === 'holding' && !want.hold));
      holdIdle.textContent = idle ? (want ? want.name : 'Stagewatch') : '';
    }
    html.classList.toggle('lost-warn', lost && !!want && want.onLoss === 'warn');
    html.setAttribute('data-hold', hold ? '1' : '0');
    html.setAttribute('data-link', lost ? 'lost' : 'ok');
    html.setAttribute('data-showing', shownContent());
    html.setAttribute('data-error', error);
    html.setAttribute('data-rev', String(want ? want.rev : 0));
  }

  /** Say what is on screen, for the revision it was sent (Stagewatch shows green only when they match). */
  function report() {
    if (!want || !ws || ws.readyState !== 1) return;
    ws.send(JSON.stringify({ type: 'shown', rev: want.rev, content: shownContent(), hold: holdShown(), ms: swapMs, error: error }));
  }

  // ---------------------------------------------------------------- the link
  function heard() {
    lastHeard = Date.now();
    if (lost) { lost = false; paint(); }
  }
  setInterval(function () {
    if (gone || lost || Date.now() - lastHeard < STALE_MS) return;
    lost = true;
    paint();
    if (ws) { try { ws.close(); } catch (e) { /* onclose reconnects */ } }
  }, 500);

  function connect() {
    if (gone) return;
    var proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    try { ws = new WebSocket(proto + '//' + location.host + '/ws?win=' + encodeURIComponent(token) + '&detail=1'); } catch (e) { later(); return; }
    ws.onopen = function () { delay = 500; };
    ws.onmessage = function (ev) {
      var msg;
      try { msg = JSON.parse(ev.data); } catch (e) { return; }
      // A beat with live:false: the link is up but the show has stopped updating — not news from the show.
      if (msg.type === 'beat') { if (msg.live !== false) heard(); return; }
      heard();
      if (msg.type === 'state') { latest = msg; latestAt = nowAbs(); subs.slice().forEach(function (s) { call(s.fn, msg, latestAt); }); paint(); return; }
      if (msg.type === 'window') {
        if (msg.revoked) { revoked(); return; }
        msg.at = nowAbs();
        want = msg;
        apply();
        return;
      }
      if (msg.type === 'identify') { identify(msg.name); return; }
      if (msg.type === 'reload') { location.reload(); }
    };
    ws.onclose = function () { ws = null; if (!gone) later(); };
    ws.onerror = function () { try { ws.close(); } catch (e) { /* closing */ } };
  }
  function later() {
    var d = delay;
    delay = Math.min(delay * 2, 5000);
    setTimeout(connect, d / 2 + Math.random() * d / 2);
  }

  function revoked() {
    gone = true;
    stopLoading();
    clearTimeout(retryTimer);
    for (var i = 0; i < frames.length; i++) retire(i);
    front = -1; frontContent = '';
    $('gone').style.display = 'flex';
    html.setAttribute('data-link', 'gone');
    if (ws) { try { ws.close(); } catch (e) { /* closing */ } }
  }

  var identTimer = 0;
  function identify(name) {
    var box = $('ident');
    box.querySelector('.big').textContent = name || 'Window';
    box.querySelector('.small').textContent = 'Stagewatch window · ' + (want ? want.id : '');
    box.style.display = 'flex';
    clearTimeout(identTimer);
    identTimer = setTimeout(function () { box.style.display = 'none'; }, 6000);
  }

  // Full screen from the screen itself (double-click, or F); kept awake while shown.
  function fullscreen() {
    var d = document;
    if (d.fullscreenElement) { if (d.exitFullscreen) d.exitFullscreen(); }
    else if (html.requestFullscreen) html.requestFullscreen().catch(function () { /* the browser said no */ });
  }
  document.addEventListener('dblclick', fullscreen);
  document.addEventListener('keydown', function (e) { if (e.key === 'f' || e.key === 'F') fullscreen(); });
  function awake() {
    if (!('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
    navigator.wakeLock.request('screen').catch(function () { /* not allowed here */ });
  }
  document.addEventListener('visibilitychange', awake);
  awake();

  paint();
  if (token.length === 26) connect(); else revoked();
})();
