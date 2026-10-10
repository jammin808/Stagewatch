// Draws a designed screen (a Stagewatch layout) into a box, from live frames. Shared by /layout and the /designer
// preview, so what you design is exactly what the screen shows. Sizes are percent of the box, fonts percent of its height.
(function () {
  'use strict';

  function live(s) { var t = s.timings || []; for (var i = 0; i < t.length; i++) if (t[i].status === 'live') return { item: t[i], index: i }; return null; }
  function next(s) { var t = s.timings || []; for (var i = 0; i < t.length; i++) if (t[i].status === 'upcoming' && t[i].kind !== 'buffer') return t[i]; return null; }

  /** Placeholder values for a frame (counting on locally between frames). */
  function values(s) {
    if (!s) return {};
    var l = live(s), n = next(s), t = s.timings || [];
    var timer = s.mode === 'countdown' ? (s.speakerText !== undefined ? s.speakerText : s.text) : s.text;
    var last = t.length ? t[t.length - 1] : null;
    return {
      time: timer || '',
      title: s.title || (l ? l.item.title : ''),
      speaker: l ? (l.item.speaker || '') : '',
      next: s.next || (n ? n.title : ''),
      nextStart: n ? (n.expected || n.planned || '') : '',
      nextIn: n && typeof n['in'] === 'number' ? SW.span(n['in'] - SW.since()) : '',
      item: l ? (l.index + 1) + ' of ' + t.length : '',
      group: l ? (l.item.group || '') : '',
      end: s.endTime || '',
      showEnd: last ? (last.expectedEnd || '') : '',
      clock: s.timeOfDay || '',
      date: s.date || '',
      room: s.room || '',
      message: s.message || '',
      cue: s.cue || '',
      aux: s.auxLine || '',
      slide: s.slides ? 'Slide ' + s.slides.slide + ' / ' + s.slides.count : ''
    };
  }

  function fill(template, v) {
    var any = false, filled = false;
    var out = String(template || '').replace(/\{([A-Za-z]+)\}/g, function (m, k) {
      if (!(k in v)) return m;
      any = true;
      if (v[k]) filled = true;
      return v[k];
    });
    return { text: out, empty: any && !filled };
  }

  var flashCss = false;
  function ensureCss() {
    if (flashCss) return;
    flashCss = true;
    var st = document.createElement('style');
    st.textContent = '@keyframes swl-flash{50%{opacity:.15}}.swl-flash-slow{animation:swl-flash 1.2s steps(1) infinite}.swl-flash-fast{animation:swl-flash .5s steps(1) infinite}' +
      '.swl-el{position:absolute;box-sizing:border-box;display:flex;align-items:center;overflow:hidden;line-height:1.05;white-space:pre-line;font-variant-numeric:tabular-nums}' +
      '.swl-hidden{visibility:hidden}';
    document.head.appendChild(st);
  }

  /** Creates a renderer in `box`. Returns { setLayout(layout), update(frame), tick() }. */
  function create(box) {
    ensureCss();
    var layout = null, frame = null, nodes = [], lastKey = [];
    if (getComputedStyle(box).position === 'static') box.style.position = 'relative';
    box.style.overflow = 'hidden';

    function build() {
      box.querySelectorAll('.swl-el').forEach(function (n) { n.parentNode.removeChild(n); });
      nodes = []; lastKey = [];
      if (!layout) return;
      box.style.background = layout.background || '#000';
      box.style.fontFamily = layout.font ? '"' + layout.font + '", "Segoe UI", system-ui, sans-serif' : '"Segoe UI", system-ui, sans-serif';
      layout.elements.forEach(function (e) {
        var d = document.createElement('div');
        d.className = 'swl-el';
        d.style.left = e.x + '%'; d.style.top = e.y + '%'; d.style.width = e.w + '%'; d.style.height = e.h + '%';
        d.style.justifyContent = e.align === 'left' ? 'flex-start' : e.align === 'right' ? 'flex-end' : 'center';
        d.style.textAlign = e.align || 'center';
        d.style.fontWeight = e.bold ? '800' : '500';
        if (e.bg && e.kind !== 'progress') d.style.background = e.bg;
        if (e.kind === 'progress') {
          d.style.background = e.bg || '#1F2937';
          d.style.borderRadius = '999px';
          var f = document.createElement('div');
          f.style.cssText = 'position:absolute;left:0;top:0;bottom:0;border-radius:999px;width:0';
          d.appendChild(f);
        }
        if (e.kind === 'image' && e.image) {
          d.style.background = 'url("/api/holding/' + e.image + '") center/contain no-repeat' + (e.bg ? ' ' + e.bg : '');
        }
        box.appendChild(d);
        nodes.push(d);
      });
      size();
      draw();
    }

    function size() {
      var h = box.clientHeight || 1;
      if (!layout) return;
      layout.elements.forEach(function (e, i) { nodes[i].style.fontSize = (e.size / 100 * h) + 'px'; nodes[i].dataset.base = String(e.size / 100 * h); });
      lastKey = [];
    }

    /** Shrink text that doesn't fit its box (a long title, an hour-long countdown). */
    function fit(n) {
      var base = parseFloat(n.dataset.base || '0'), px = base;
      n.style.fontSize = px + 'px';
      for (var k = 0; k < 14 && (n.scrollWidth > n.clientWidth + 1 || n.scrollHeight > n.clientHeight + 1) && px > 6; k++) { px *= 0.88; n.style.fontSize = px + 'px'; }
    }

    function draw() {
      if (!layout) return;
      var s = frame || {}, v = values(frame);
      var black = !!s.blackout;
      layout.elements.forEach(function (e, i) {
        var n = nodes[i], key, hidden = black, text = null, color = e.color || '#FFFFFF', flash = 'off';
        if (e.kind === 'timer') {
          text = v.time || ''; color = e.color || s.color || '#FFFFFF';
          flash = s.flashTimer ? (s.flash === 'off' ? 'slow' : s.flash) : 'off';
        } else if (e.kind === 'text') {
          var r = fill(e.text, v); text = r.text; hidden = hidden || r.empty;
        } else if (e.kind === 'message') {
          text = s.message ? (e.text ? fill(e.text, v).text + ' ' : '') + s.message : '';
          color = e.color || (SW.urgencyColors && SW.urgencyColors[s.urgency]) || '#E2E8F0';
          hidden = hidden || !s.message; flash = s.message ? (s.flash || 'off') : 'off';
        } else if (e.kind === 'labels') {
          var l = live(s), labels = l && l.item.labels ? l.item.labels : [];
          key = 'L' + labels.join(',') + hidden;
          if (key !== lastKey[i]) {
            lastKey[i] = key; n.textContent = '';
            n.appendChild(SW.labelChips(labels, s.labelColors));
            n.classList.toggle('swl-hidden', hidden || !labels.length);
          }
          return;
        } else if (e.kind === 'progress') {
          var p = s.mode === 'countdown' && s.total > 0 ? Math.max(0, Math.min(1, s.seconds / s.total)) : 0;
          var fillEl = n.firstChild;
          fillEl.style.width = (p * 100).toFixed(2) + '%';
          fillEl.style.background = e.color || s.color || '#22C55E';
          n.classList.toggle('swl-hidden', hidden || !(s.mode === 'countdown' && s.total > 0));
          return;
        } else {
          n.classList.toggle('swl-hidden', hidden);
          return;
        }
        key = text + '|' + color + '|' + flash + '|' + hidden;
        if (key === lastKey[i]) return;
        lastKey[i] = key;
        if (n.textContent !== text) { n.textContent = text; fit(n); }
        n.style.color = color;
        n.classList.toggle('swl-hidden', hidden);
        n.classList.toggle('swl-flash-slow', flash === 'slow');
        n.classList.toggle('swl-flash-fast', flash === 'fast');
      });
    }

    if (window.ResizeObserver) new ResizeObserver(function () { size(); draw(); layout && nodes.forEach(fit); }).observe(box);

    return {
      setLayout: function (l) { layout = l; build(); },
      update: function (s) { frame = s; draw(); },
      tick: draw,
      values: function () { return values(frame); }
    };
  }

  window.SWLayout = { create: create, values: values, fill: fill };
})();
