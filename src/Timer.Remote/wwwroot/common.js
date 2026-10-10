// Stagewatch browser client — shared connection logic.
// The app is the authority: we render whatever state it sends and send commands back.
// Reconnects forever with capped backoff so a tablet left on a stand recovers on its own.
(function () {
  'use strict';

  function connect(opts) {
    var ws = null;
    var delay = 500;
    var closedByUs = false;
    var lastConfigVersion = -1;

    function url() {
      var proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
      var parts = [];
      if (opts.control) parts.push('role=control', 'pin=' + encodeURIComponent(opts.pin || ''));
      if (opts.detail) parts.push('detail=1');
      // Crew link key (?key=…): lets this page confirm the cues sent to it.
      var crewKey = new URLSearchParams(location.search).get('key');
      if (crewKey) parts.push('key=' + encodeURIComponent(crewKey));
      // Screens: this browser's own id (kept, so the operator's name for it sticks) and the page it shows (never the PIN).
      parts.push('cid=' + encodeURIComponent(screenId()), 'page=' + encodeURIComponent(pageWithoutPin()));
      // Viewing code (when the operator set one): from the address, or remembered on this device.
      var view = q('view') || store('sw-view-code');
      if (view) parts.push('view=' + encodeURIComponent(view));
      return proto + '//' + location.host + '/ws' + (parts.length ? '?' + parts.join('&') : '');
    }

    // Freshness watchdog. Stagewatch sends a frame on every change and a heartbeat at least every 2 s,
    // so silence means the link is dead even if the socket still looks open. After STALE_MS without a word
    // the screen says so loudly (never a confident stale time) and the socket is recycled.
    var STALE_MS = 6000;
    var lastHeard = Date.now();
    var stale = false;
    function setStale(on) {
      if (on === stale) return;
      stale = on;
      showStale(on);
      if (opts.onStale) opts.onStale(on);
    }
    setInterval(function () {
      if (Date.now() - lastHeard < STALE_MS) return;
      setStale(true);
      if (ws && ws.readyState === 1) { try { ws.close(); } catch (e) {} }   // onclose reconnects
    }, 1000);

    function open() {
      try { ws = new WebSocket(url()); } catch (e) { schedule(); return; }
      ws.onopen = function () { delay = 500; lastHeard = Date.now(); if (opts.onStatus) opts.onStatus(true); };
      ws.onmessage = function (ev) {
        var msg;
        try { msg = JSON.parse(ev.data); } catch (e) { lastHeard = Date.now(); return; }
        // A beat with live:false means the link is fine but Stagewatch itself has stopped updating: don't count it
        // as hearing from the show, so the screen goes stale rather than showing a frozen time as current.
        if (msg.type === 'beat') { if (msg.live !== false) lastHeard = Date.now(); return; }
        lastHeard = Date.now();
        if (msg.type === 'redirect') { goTo(msg.to); return; }
        if (msg.type === 'reload') { location.reload(); return; }
        if (msg.type === 'disconnect') { closedByUs = true; offByOperator(); try { ws.close(); } catch (e) {} return; }
        if (msg.type === 'auth' && msg.view) { closedByUs = true; askViewCode(msg.error, function () { closedByUs = false; delay = 0; schedule(); }); return; }
        if (msg.type === 'identify') { identify(msg.name, msg.id); return; }
        if (msg.type === 'state' && delaySeconds > 0) {
          // ?delay=N: show the show N seconds late (to match a stream's latency). Frames are queued and played out
          // in order; relative times are stamped when shown, so every countdown stays exact — just later.
          setTimeout(function () { deliver(msg); }, delaySeconds * 1000);
          return;
        }
        if (msg.type === 'state') { deliver(msg); return; }
        if (msg.type === 'auth' && opts.onAuth) {
          opts.onAuth(msg);
        } else if (msg.type === 'error') {
          if (opts.onError) opts.onError(msg.error); else toast(msg.error);
        }
      };
      function deliver(msg) {
        setStale(false);
        customCss(msg.configVersion);
        if (msg.configVersion !== lastConfigVersion && opts.onConfig) {
          lastConfigVersion = msg.configVersion;
          fetchConfig();
        }
        stamp();
        if (soundOn) chime(msg);
        opts.onState(msg);
      }
      ws.onclose = function () {
        if (opts.onStatus) opts.onStatus(false);
        if (!closedByUs) schedule();
      };
      ws.onerror = function () { try { ws.close(); } catch (e) {} };
    }

    function schedule() {
      setTimeout(open, delay);
      delay = Math.min(delay * 2, 5000);
    }

    function fetchConfig() {
      // The same pass as the connection: the operator's PIN, a crew link key, or the viewing code.
      var pass = [];
      if (opts.control && opts.pin) pass.push('pin=' + encodeURIComponent(opts.pin));
      var crewKey = q('key'), view = q('view') || store('sw-view-code');
      if (crewKey) pass.push('key=' + encodeURIComponent(crewKey));
      if (view) pass.push('view=' + encodeURIComponent(view));
      fetch('/api/config' + (pass.length ? '?' + pass.join('&') : ''), { cache: 'no-store' })
        .then(function (r) { if (!r.ok) throw new Error('config ' + r.status); return r.json(); })
        .then(function (c) { opts.onConfig(c); })
        .catch(function () { lastConfigVersion = -1; });
    }

    open();

    return {
      send: function (cmd) {
        var args = Array.prototype.slice.call(arguments, 1).map(String);
        if (ws && ws.readyState === 1) ws.send(JSON.stringify({ cmd: cmd, args: args }));
      },
      reconnect: function () {
        closedByUs = true;
        try { ws.close(); } catch (e) {}
        closedByUs = false;
        delay = 0;
        schedule();
      }
    };
  }

  // ---- shared screen chrome: the stale-link warning and a small toast for refused actions
  function injectStyle() {
    if (document.getElementById('sw-chrome-style')) return;
    var st = document.createElement('style');
    st.id = 'sw-chrome-style';
    st.textContent =
      'html.sw-stale body > *:not(#sw-stale):not(#sw-toast) { opacity: .22 !important; filter: grayscale(1) !important; }' +
      '#sw-stale { position: fixed; left: 0; right: 0; top: 0; z-index: 2000; display: none; padding: 1.4vh 3vw; text-align: center;' +
      '  background: #b91c1c; color: #fff; font: 700 max(18px, 3.4vh) "Segoe UI", system-ui, sans-serif; letter-spacing: .02em; }' +
      'html.sw-stale #sw-stale { display: block; }' +
      '#sw-toast { position: fixed; left: 50%; bottom: 4vh; transform: translateX(-50%); z-index: 2001; max-width: 90vw; padding: 10px 16px;' +
      '  border-radius: 10px; background: #111; border: 1px solid #f59e0b; color: #fde68a; font: 600 15px "Segoe UI", system-ui, sans-serif; display: none; }';
    document.head.appendChild(st);
  }
  function showStale(on) {
    injectStyle();
    var b = document.getElementById('sw-stale');
    if (!b) {
      b = document.createElement('div');
      b.id = 'sw-stale';
      b.textContent = 'NOT CONNECTED TO STAGEWATCH — the time shown may be out of date';
      document.body.appendChild(b);
    }
    document.documentElement.classList.toggle('sw-stale', on);
  }
  var toastTimer = 0;
  function toast(text) {
    if (!text) return;
    injectStyle();
    var t = document.getElementById('sw-toast');
    if (!t) { t = document.createElement('div'); t.id = 'sw-toast'; document.body.appendChild(t); }
    t.textContent = text;
    t.style.display = 'block';
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.style.display = 'none'; }, 6000);
  }

  // ---- Per-screen options in the address (no settings needed on the device):
  //   ?mirror=h|v|both   flip the page (teleprompter glass, rear projection)
  //   ?delay=5           show the show 5 s late, to line up with a delayed stream (0-60)
  //   ?view=CODE         the viewing code, when the operator set one (remembered on the device)
  var delaySeconds = Math.max(0, Math.min(60, parseFloat(q('delay')) || 0));
  (function () {
    var m = (q('mirror') || '').toLowerCase();
    var sx = m === 'h' || m === 'both' || m === 'x' ? -1 : 1, sy = m === 'v' || m === 'both' || m === 'y' ? -1 : 1;
    if (sx !== 1 || sy !== 1) {
      var st = document.createElement('style');
      st.textContent = 'html { transform: scale(' + sx + ',' + sy + '); transform-origin: center; }';
      document.head.appendChild(st);
    }
    var code = q('view');
    if (code) store('sw-view-code', code);
  })();

  /** The operator disconnected this screen: say so plainly; reloading the page reconnects. */
  function offByOperator() {
    injectStyle();
    var d = document.getElementById('sw-off');
    if (!d) {
      d = document.createElement('div');
      d.id = 'sw-off';
      d.style.cssText = 'position:fixed;inset:0;z-index:3500;display:flex;flex-direction:column;align-items:center;justify-content:center;' +
        'background:#000;color:#9ca3af;font:600 max(18px,3vh) "Segoe UI",system-ui,sans-serif;text-align:center;padding:5vw';
      d.textContent = 'Disconnected by the Stagewatch operator. Reload this page to reconnect.';
      document.body.appendChild(d);
    }
  }

  /** Ask for the viewing code once, remember it on this device, then reconnect. */
  function askViewCode(error, retry) {
    injectStyle();
    var box = document.getElementById('sw-view');
    if (box) { box.querySelector('p').textContent = error || ''; return; }
    box = document.createElement('form');
    box.id = 'sw-view';
    box.style.cssText = 'position:fixed;inset:0;z-index:3600;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;' +
      'background:#000;color:#f9fafb;font:600 18px "Segoe UI",system-ui,sans-serif;padding:6vw';
    var h = document.createElement('div'); h.textContent = 'Stagewatch — viewing code';
    var p = document.createElement('p'); p.style.cssText = 'margin:0;color:#fcd34d;font-size:15px'; p.textContent = error || '';
    var input = document.createElement('input');
    input.type = 'password'; input.autocomplete = 'off'; input.maxLength = 20;
    input.style.cssText = 'font:inherit;font-size:24px;padding:10px 14px;border-radius:10px;border:1px solid #374151;background:#111;color:#fff;text-align:center;width:min(320px,80vw)';
    var b = document.createElement('button'); b.type = 'submit'; b.textContent = 'Show';
    b.style.cssText = 'font:inherit;padding:10px 22px;border-radius:10px;border:0;background:#22c55e;color:#000';
    box.appendChild(h); box.appendChild(p); box.appendChild(input); box.appendChild(b);
    box.onsubmit = function (e) {
      e.preventDefault();
      store('sw-view-code', input.value.trim());
      box.parentNode.removeChild(box);
      retry();
    };
    document.body.appendChild(box);
    input.focus();
  }

  // ---- Chimes on this screen (?sound=1): the same chime the operator hears, e.g. a tablet at the lectern or an
  // in-ear feed. Built-in tones are synthesised here; library sounds are fetched once by id. Browsers only allow
  // sound after a touch, so the page asks for one tap.
  var soundOn = q('sound') === '1';
  var audio = null, lastChime = -1, soundCache = {};
  function audioCtx() {
    if (!audio) { try { audio = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { audio = null; } }
    return audio;
  }
  function tones(list) {
    var ctx = audioCtx(); if (!ctx) return;
    var t = ctx.currentTime + 0.02;
    list.forEach(function (n) {
      var o = ctx.createOscillator(), g = ctx.createGain();
      o.frequency.value = n[0]; o.type = 'sine';
      g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(0.3, t + 0.01); g.gain.linearRampToValueAtTime(0, t + n[1]);
      o.connect(g); g.connect(ctx.destination); o.start(t); o.stop(t + n[1] + 0.02);
      t += n[1] + (n[2] || 0);
    });
  }
  function playSpec(spec) {
    if (spec === 'builtin:warning') tones([[880, .14, .09], [880, .14]]);
    else if (spec === 'builtin:wrapup') tones([[1046, .11, .07], [1046, .11, .07], [1046, .11]]);
    else if (spec === 'builtin:zero') tones([[784, .5, .05], [523, .9]]);
    else if (spec === 'builtin:start') tones([[660, .12, .03], [990, .16]]);
    else if (spec.indexOf('sound:') === 0) {
      var id = spec.slice(6), ctx = audioCtx();
      if (!ctx || !/^[A-Za-z0-9]{1,32}$/.test(id)) return;
      var go = function (buf) { var src = ctx.createBufferSource(); src.buffer = buf; src.connect(ctx.destination); src.start(); };
      if (soundCache[id]) { go(soundCache[id]); return; }
      fetch('/api/sounds/' + id + '.wav').then(function (r) { return r.arrayBuffer(); })
        .then(function (b) { return ctx.decodeAudioData(b); }).then(function (buf) { soundCache[id] = buf; go(buf); }).catch(function () {});
    }
  }
  function chime(s) {
    if (typeof s.chimeSeq !== 'number') return;
    if (lastChime < 0) { lastChime = s.chimeSeq; askForSound(); return; }     // don't replay an old chime on connect
    if (s.chimeSeq === lastChime) return;
    lastChime = s.chimeSeq;
    if (s.chime) playSpec(s.chime);
  }
  function askForSound() {
    var ctx = audioCtx();
    if (!ctx || ctx.state === 'running' || document.getElementById('sw-sound')) return;
    var b = document.createElement('button');
    b.id = 'sw-sound';
    b.textContent = '🔈 Tap to enable sound';
    b.style.cssText = 'position:fixed;left:50%;bottom:3vh;transform:translateX(-50%);z-index:3100;padding:10px 18px;border-radius:12px;' +
      'border:1px solid #22c55e;background:#052e16;color:#bbf7d0;font:600 16px "Segoe UI",system-ui,sans-serif';
    b.onclick = function () { ctx.resume(); tones([[880, .06]]); b.parentNode.removeChild(b); };
    document.body.appendChild(b);
  }

  // ---- Custom styling: /custom.css (Settings -> Remote) on every page, reloaded when settings change.
  // Each page's <html> gets class sw-page-<name> (sw-page-viewer, sw-page-audience...) so CSS can target one screen.
  (function () {
    var name = (location.pathname.replace(/^\/+/, '').split(/[/.]/)[0] || 'index').toLowerCase();
    if (/^[a-z0-9-]+$/.test(name)) document.documentElement.classList.add('sw-page-' + name);
  })();
  var cssVersion = null;
  function customCss(version) {
    if (version === undefined || version === cssVersion) return;
    cssVersion = version;
    var link = document.getElementById('sw-custom-css');
    if (!link) {
      link = document.createElement('link');
      link.id = 'sw-custom-css';
      link.rel = 'stylesheet';
      document.head.appendChild(link);
    }
    link.href = '/custom.css?v=' + encodeURIComponent(String(version));
  }

  // ---- Screens: identity, remote redirect and "which screen is this?"
  function screenId() {
    var id = store('sw-screen-id');
    if (!id || !/^[A-Za-z0-9_-]{6,32}$/.test(id)) {
      id = '';
      var abc = 'abcdefghijkmnpqrstuvwxyz23456789';
      for (var i = 0; i < 10; i++) id += abc.charAt(Math.floor(Math.random() * abc.length));
      store('sw-screen-id', id);
    }
    return id;
  }
  function pageWithoutPin() {
    var sp = new URLSearchParams(location.search);
    sp.delete('pin');
    var qs = sp.toString();
    return location.pathname + (qs ? '?' + qs : '');
  }
  /** Only ever a page on this Stagewatch: a path, never another site. */
  function goTo(to) {
    if (typeof to !== 'string' || to.length > 400 || to.charAt(0) !== '/' || to.charAt(1) === '/' || to.indexOf('\\') >= 0) return;
    var u;
    try { u = new URL(to, location.origin); } catch (e) { return; }
    if (u.origin !== location.origin) return;
    location.href = u.pathname + u.search;
  }
  var idTimer = 0;
  function identify(name, id) {
    injectStyle();
    var box = document.getElementById('sw-identify');
    if (!box) {
      box = document.createElement('div');
      box.id = 'sw-identify';
      box.style.cssText = 'position:fixed;inset:0;z-index:3000;display:flex;flex-direction:column;align-items:center;justify-content:center;' +
        'background:#0b1220;color:#fff;font-family:"Segoe UI",system-ui,sans-serif;text-align:center;border:1.2vh solid #22d3ee';
      document.body.appendChild(box);
    }
    box.textContent = '';
    var big = document.createElement('div');
    big.style.cssText = 'font-size:12vh;font-weight:800;letter-spacing:.02em;padding:0 4vw';
    big.textContent = name || 'Unnamed screen';
    var small = document.createElement('div');
    small.style.cssText = 'margin-top:3vh;font-size:3.4vh;opacity:.7';
    small.textContent = 'Screen ' + (id || screenId()) + ' · ' + location.pathname;
    box.appendChild(big); box.appendChild(small);
    box.style.display = 'flex';
    clearTimeout(idTimer);
    idTimer = setTimeout(function () { box.style.display = 'none'; }, 6000);
  }

  function store(key, value) {
    try {
      if (value === undefined) return localStorage.getItem(key);
      localStorage.setItem(key, value);
    } catch (e) { return null; }
  }

  function fmt(seconds) {
    var neg = seconds < 0; seconds = Math.abs(seconds);
    var h = Math.floor(seconds / 3600), m = Math.floor(seconds / 60) % 60, s = seconds % 60;
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return (neg ? '-' : '') + (h > 0 ? p(h) + ':' : '') + p(m) + ':' + p(s);
  }

  var urgencyColors = { info: '#E2E8F0', warning: '#F59E0B', critical: '#DC2626' };

  /** Keep the screen awake (tablets on lecterns, phones in pockets). Re-acquired when the page is shown again. */
  function keepAwake() {
    var lock = null;
    function acquire() {
      if (!('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
      navigator.wakeLock.request('screen').then(function (l) { lock = l; }).catch(function () {});
    }
    document.addEventListener('visibilitychange', acquire);
    acquire();
  }

  /** Vibrate where supported (Android); iOS has no vibration API, so callers also flash the screen. */
  function buzz(pattern) {
    try { if (navigator.vibrate) return navigator.vibrate(pattern); } catch (e) {}
    return false;
  }

  function q(name) { return new URLSearchParams(location.search).get(name); }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined && text !== null) e.textContent = text;
    return e;
  }

  /**
   * Holding image: a full-screen still over the page while one is on air (blackout hides it).
   * Images are fetched once by id (they never change) and swapped only when the id does.
   * Add ?holding=0 to a page's address to never show holding images on that screen.
   */
  var holdEl = null, holdId = '';
  function holding(s) {
    if (q('holding') === '0') return false;
    var id = s && !s.blackout && s.holding ? s.holding : '';
    if (!holdEl) {
      holdEl = el('div');
      holdEl.style.cssText = 'position:fixed;inset:0;background:#000;z-index:900;display:none';
      var img = el('img');
      img.alt = '';
      img.style.cssText = 'width:100%;height:100%;object-fit:contain;display:block';
      holdEl.appendChild(img);
      document.body.appendChild(holdEl);
    }
    var pic = holdEl.firstChild;
    pic.style.objectFit = s && s.holdingFill ? 'cover' : 'contain';
    if (id !== holdId) { holdId = id; if (id) pic.src = '/api/holding/' + encodeURIComponent(id); }
    holdEl.style.display = id ? 'block' : 'none';
    return !!id;
  }

  /**
   * Frames carry times relative to the moment they were built ("starts in 754 s", "clock 50400 s"). A screen
   * stamps each frame on arrival with its own monotonic clock and counts on from there — so it ticks smoothly
   * between frames and never depends on the device's wall clock being right.
   */
  var arrivedAt = 0;
  function stamp() { arrivedAt = performance.now(); }
  function since() { return arrivedAt ? (performance.now() - arrivedAt) / 1000 : 0; }

  /** "1:02:03" / "12:30" for a span of seconds (always positive; callers word the sign). */
  function span(seconds) { return fmt(Math.max(0, Math.round(seconds))); }

  /** Coloured label chips for an item ("VT", "GFX"), using the show's label colours. */
  function labelChips(labels, colors) {
    var wrap = el('span', 'sw-labels');
    (labels || []).forEach(function (l) {
      var c = el('span', 'sw-label', l);
      c.style.cssText = 'display:inline-block;margin-left:6px;padding:0 6px;border-radius:4px;font-size:.72em;font-weight:800;color:#fff;vertical-align:middle;background:' +
        ((colors && colors[l]) || '#475569');
      wrap.appendChild(c);
    });
    return wrap;
  }

  window.SW = { labelChips: labelChips, stamp: stamp, since: since, span: span, connect: connect, store: store, fmt: fmt, urgencyColors: urgencyColors, keepAwake: keepAwake, buzz: buzz, q: q, el: el, holding: holding, toast: toast };
})();
