// Role views — one renderer, five jobs, chosen by the URL:
//   /moderator        session clock, cue, Q&A live + queue + "how many fit", cues for the moderator
//   /backstage        now / next / expected start, call-to-stage with "found" / "in position" acknowledgements
//   /dept?name=Sound  huge STANDBY / GO for one department, with acknowledgement
//   /interpreter      speaker, time left, next speaker, pace messages
//   /haptic?for=speaker   phone / watch in the speaker's or chair's pocket: buzz + flash + tap to acknowledge
// Read-only except for acknowledgements, which need no PIN.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var path = location.pathname.replace(/\/$/, '').toLowerCase();
  var role = path === '/greenroom' ? 'backstage' : path === '/department' ? 'dept' : path === '/watch' ? 'haptic' : path.slice(1);
  var dept = SW.q('name') || 'Stage';
  var hapticFor = (SW.q('for') || 'speaker').toLowerCase();
  var by = SW.q('by') || SW.store('sw-name') || '';
  var conn = null;
  var main = $('main');

  $('roleName').textContent = 'STAGEWATCH · ' + ({
    moderator: 'MODERATOR', backstage: 'BACKSTAGE', dept: dept.toUpperCase(), interpreter: 'INTERPRETER', haptic: 'POCKET CUE'
  }[role] || role.toUpperCase());

  function clear() { main.textContent = ''; }
  function ack(id, stage) { if (conn) conn.send('ack', id, stage, by); }

  function timeText(s) { return s.mode === 'countdown' ? (s.speakerText || s.text) : s.text; }

  function signalRow(g, buttons) {
    var row = SW.el('div', 'signal ' + g.state + (g.escalated ? ' escalated' : ''));
    var label = g.kind === 'call' ? 'CALL: ' : g.kind === 'standby' ? 'STANDBY ' : g.kind === 'go' ? 'GO ' : '';
    row.appendChild(SW.el('div', 'what', label + (g.text || '')));
    if (g.state === 'sent' || g.state === 'seen') buttons.forEach(function (b) {
      if (b.when && b.when !== g.state) return;
      var btn = SW.el('button', b.cls || 'primary', b.label);
      btn.onclick = function () { ack(g.id, b.stage); };
      row.appendChild(btn);
    });
    else row.appendChild(SW.el('span', 'ack-ok', '✓' + (g.by ? ' ' + g.by : '')));
    return row;
  }

  function renderModerator(s) {
    clear();
    document.documentElement.style.setProperty('--timer', s.color || '#fff');
    main.appendChild(SW.el('div', 'big num', timeText(s)));
    if (s.cue) main.appendChild(SW.el('div', 'cue', '')).appendChild(SW.el('span', 'cue-badge', s.cue));
    main.appendChild(SW.el('div', 'sub', s.title + (s.finishBy ? '  ·  ' + s.finishBy : '')));
    var mine = (s.signals || []).filter(function (g) { return g.target === 'moderator' || g.target === 'chair'; });
    if (mine.length) {
      main.appendChild(SW.el('h2', '', 'Cues for you'));
      mine.forEach(function (g) { main.appendChild(signalRow(g, [{ label: 'Got it', stage: 'done' }])); });
    }
    main.appendChild(SW.el('h2', '', 'Q&A — about ' + s.qaFit + ' more fit'));
    var qs = s.questions || [];
    if (!qs.length && !s.qaLive) main.appendChild(SW.el('div', 'muted', 'No questions yet. Audience: scan the QR on the agenda screen.'));
    qs.filter(function (q) { return q.state !== 'new'; }).forEach(function (q) {
      var d = SW.el('div', 'q ' + q.state, q.text);
      if (q.name) d.appendChild(SW.el('small', '', q.name + (q.state === 'starred' ? ' ★' : '')));
      main.appendChild(d);
    });
  }

  function renderBackstage(s) {
    clear();
    var t = s.timings || [];
    var cur = s.item >= 0 ? t[s.item] : null;
    var nxt = s.item + 1 < t.length ? t[s.item + 1] : null;
    main.appendChild(SW.el('h2', '', 'On stage now'));
    var now = SW.el('div', 'panel');
    now.appendChild(SW.el('div', '', cur ? cur.title + (cur.speaker ? ' — ' + cur.speaker : '') : (s.title || '—')));
    now.appendChild(SW.el('div', 'big num', timeText(s)));
    main.appendChild(now);
    if (nxt) {
      main.appendChild(SW.el('h2', '', 'Next'));
      var n = SW.el('div', 'panel');
      n.appendChild(SW.el('div', '', nxt.title + (nxt.speaker ? ' — ' + nxt.speaker : '')));
      n.appendChild(SW.el('div', 'sub num', 'Expected on stage ' + nxt.expected + (nxt.delay > 30 ? '  (' + SW.fmt(nxt.delay) + ' late)' : '')));
      main.appendChild(n);
    }
    var calls = (s.signals || []).filter(function (g) { return g.target === 'backstage'; });
    main.appendChild(SW.el('h2', '', 'Calls'));
    if (!calls.length) main.appendChild(SW.el('div', 'muted', 'No calls.'));
    calls.slice().reverse().forEach(function (g) {
      main.appendChild(signalRow(g, [
        { label: 'Found', stage: 'seen', when: 'sent', cls: 'warn' },
        { label: 'In position', stage: 'done', cls: 'primary' }
      ]));
    });
  }

  var lastDeptId = 0;
  function renderDept(s) {
    clear();
    var mine = (s.signals || []).filter(function (g) { return g.target.toLowerCase() === dept.toLowerCase() && g.state !== 'cancelled'; });
    var g = mine[mine.length - 1];
    var box = SW.el('div', '');
    box.id = 'dept';
    var open = g && (g.state === 'sent' || g.state === 'seen');
    var stateText = !g ? 'CLEAR' : g.kind === 'go' ? 'GO' : g.kind === 'standby' ? 'STANDBY' : g.kind.toUpperCase();
    if (open) box.className = g.kind === 'go' ? 'go' : 'standby';
    box.appendChild(SW.el('div', 'state' + (open && g.state === 'sent' ? ' flash-slow' : ''), stateText));
    box.appendChild(SW.el('div', 'what', g && g.text ? g.text : ''));
    if (open) {
      var b = SW.el('button', 'ack-btn ' + (g.kind === 'go' ? 'primary' : 'warn'), g.kind === 'standby' ? 'Standing by' : 'Done');
      b.onclick = function () { ack(g.id, g.kind === 'standby' ? 'seen' : 'done'); };
      if (!(g.kind === 'standby' && g.state === 'seen')) box.appendChild(b);
      else box.appendChild(SW.el('div', 'ack-ok', '✓ standing by'));
      if (g.id !== lastDeptId) { lastDeptId = g.id; SW.buzz(g.kind === 'go' ? [500] : [150, 100, 150]); }
    } else if (g) box.appendChild(SW.el('div', 'ack-ok', '✓ ' + (g.by || 'done')));
    main.appendChild(box);
    main.appendChild(SW.el('div', 'sub', (s.title || '') + '  ·  ' + timeText(s)));
  }

  function renderInterpreter(s) {
    clear();
    document.documentElement.style.setProperty('--timer', s.color || '#fff');
    var t = s.timings || [];
    var cur = s.item >= 0 ? t[s.item] : null;
    var nxt = s.item + 1 < t.length ? t[s.item + 1] : null;
    main.appendChild(SW.el('div', 'sub', cur ? (cur.speaker || cur.title) : s.title));
    main.appendChild(SW.el('div', 'big num', timeText(s)));
    if (s.message) main.appendChild(SW.el('div', 'cue', s.message));
    if (nxt) main.appendChild(SW.el('div', 'sub', 'Next: ' + (nxt.speaker ? nxt.speaker + ' — ' : '') + nxt.title + ' · ' + nxt.expected));
    var mine = (s.signals || []).filter(function (g) { return g.target === 'interpreter'; });
    mine.forEach(function (g) { main.appendChild(signalRow(g, [{ label: 'Got it', stage: 'done' }])); });
  }

  // Pocket cue (phone / watch): distinct vibration per cue, full-screen flash, tap anywhere to acknowledge.
  var hapticOpen = null;
  function patternFor(g) {
    var t = (g.text || '').toLowerCase();
    if (g.kind === 'call') return [200, 100, 200, 100, 200];
    if (t.indexOf('time') >= 0 || t.indexOf('stop') >= 0) return [800, 200, 800, 200, 800];
    if (t.indexOf('wrap') >= 0) return [600, 150, 200];
    if (t.indexOf('+') === 0 || t.indexOf('more time') >= 0) return [200, 150, 600];
    return [250, 120, 250];
  }
  function renderHaptic(s) {
    main.classList.add('hidden');
    show($('haptic'), true);
    document.documentElement.style.setProperty('--timer', s.color || '#fff');
    $('hT').textContent = timeText(s);
    var mine = (s.signals || []).filter(function (g) {
      return g.target === hapticFor && (g.kind === 'nudge' || g.kind === 'message' || g.kind === 'call') && g.state === 'sent';
    });
    var g = mine[mine.length - 1] || null;
    $('haptic').classList.toggle('alert', !!g);
    $('hMsg').textContent = g ? (g.text || 'Look at the monitor') : (s.cue || '');
    if (g && (!hapticOpen || hapticOpen.id !== g.id)) SW.buzz(patternFor(g));
    if (g && g.escalated && hapticOpen && hapticOpen.id === g.id && !hapticOpen.escalated) SW.buzz(patternFor(g));
    hapticOpen = g;
    $('pair').textContent = g ? 'Tap anywhere to acknowledge' : 'Keep this screen on. Cues for ' + hapticFor + ' will buzz and flash here.';
  }
  function show(el, on) { el.classList.toggle('hidden', !on); }
  $('haptic').addEventListener('click', function () { if (hapticOpen) ack(hapticOpen.id, 'done'); });

  // ---- Department notes: this department's notes on each item (its crew link is its permission to read and edit).
  var notes = null, notesVersion = -1, notesItem = -2, notesBusy = false, editing = false, lastState = null;
  var crewKey = SW.q('key') || '';
  function loadNotes() {
    if (notesBusy) return;
    notesBusy = true;
    fetch('/api/deptnotes?dept=' + encodeURIComponent(dept) + (crewKey ? '&key=' + encodeURIComponent(crewKey) : ''), { cache: 'no-store' })
      .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, j: j }; }); })
      .then(function (res) {
        notesBusy = false;
        if (!res.ok) { notes = null; $('notesMsg').textContent = res.j.error || 'Notes unavailable'; $('editNotes').classList.add('hidden'); renderNotes(); return; }
        notes = res.j.items || [];
        $('notesMsg').textContent = '';
        $('editNotes').classList.remove('hidden');
        renderNotes();
      })
      .catch(function () { notesBusy = false; });
  }
  function renderNotes() {
    var box = $('notesNow');
    box.textContent = '';
    var s = lastState;
    if (!notes || !s) return;
    var cur = s.item >= 0 ? notes[s.item] : null;
    var nxt = s.item + 1 < notes.length ? notes[s.item + 1] : (s.item < 0 ? notes[0] : null);
    [['NOW', cur], ['NEXT', nxt]].forEach(function (pair) {
      if (!pair[1]) return;
      var d = SW.el('div', 'now' + (pair[1].note ? '' : ' empty'));
      d.appendChild(SW.el('b', '', pair[0] + ' · ' + (pair[1].i + 1) + '. ' + pair[1].title));
      d.appendChild(document.createTextNode(pair[1].note || 'No note'));
      box.appendChild(d);
    });
    if (editing) renderEditor();
  }
  function renderEditor() {
    var ed = $('noteEditor');
    var focused = document.activeElement && document.activeElement.dataset ? document.activeElement.dataset.i : null;
    if (ed.childNodes.length === notes.length) {
      // Keep what's being typed: only refresh boxes nobody is editing.
      notes.forEach(function (n, i) {
        var ta = ed.childNodes[i].querySelector('textarea');
        if (String(i) !== focused && !ta.dataset.dirty) ta.value = n.note;
        ed.childNodes[i].classList.toggle('live', lastState && lastState.item === i);
      });
      return;
    }
    ed.textContent = '';
    notes.forEach(function (n, i) {
      var row = SW.el('div', 'item' + (lastState && lastState.item === i ? ' live' : ''));
      row.appendChild(SW.el('div', '', (i + 1) + '. ' + n.title));
      var ta = SW.el('textarea');
      ta.value = n.note;
      ta.maxLength = 1000;
      ta.dataset.i = String(i);
      ta.oninput = function () { ta.dataset.dirty = '1'; ok.textContent = ''; };
      var save = SW.el('button', 'small primary', 'Save');
      var ok = SW.el('span', 'saved', '');
      save.onclick = function () {
        if (conn) conn.send('deptnote', String(i + 1), dept, ta.value);
        delete ta.dataset.dirty;
        ok.textContent = '✓ saved';
      };
      row.appendChild(ta);
      row.appendChild(save);
      row.appendChild(ok);
      ed.appendChild(row);
    });
  }
  $('editNotes').onclick = function () {
    editing = !editing;
    $('noteEditor').classList.toggle('hidden', !editing);
    $('editNotes').textContent = editing ? 'Done' : 'Edit our notes';
    if (editing && notes) { $('noteEditor').textContent = ''; renderEditor(); }
  };
  function deptNotes(s) {
    lastState = s;
    $('notes').classList.remove('hidden');
    $('notesTitle').textContent = dept + ' notes';
    if (s.notesVersion !== notesVersion) { notesVersion = s.notesVersion; loadNotes(); }
    else if (s.item !== notesItem) renderNotes();
    notesItem = s.item;
  }

  function render(s) {
    $('clock').textContent = s.timeOfDay;
    if (role === 'dept') deptNotes(s);
    switch (role) {
      case 'moderator': renderModerator(s); break;
      case 'backstage': renderBackstage(s); break;
      case 'dept': renderDept(s); break;
      case 'interpreter': renderInterpreter(s); break;
      case 'haptic': renderHaptic(s); break;
      default: renderModerator(s);
    }
  }

  conn = SW.connect({ detail: role !== 'haptic' && role !== 'dept', onState: render, onStatus: function (on) { show($('offline'), !on); } });
  SW.keepAwake();
})();
