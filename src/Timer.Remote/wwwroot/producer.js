// Showcaller / producer page: the whole show on one screen. Like every remote it holds no state —
// it renders the detail frames Stagewatch pushes and sends commands back in the one shared grammar.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var conn = null, state = null, config = null, pinUsed = '', holdShown = null;

  function flashError(text) {
    $('err').textContent = text;
    clearTimeout(flashError.t);
    flashError.t = setTimeout(function () { $('err').textContent = ''; }, 5000);
  }

  function send() { if (conn) conn.send.apply(null, arguments); }

  function start(pin) {
    pinUsed = pin;
    $('pinbox').classList.add('hidden');
    conn = SW.connect({
      control: true, detail: true, pin: pin,
      onState: render,
      onConfig: function (c) { config = c; renderDepts(); renderHolding(); if (pinUsed !== null) loadCrewLinks(); },
      onAuth: function (a) {
        if (a.ok) { $('app').classList.remove('hidden'); afterAuth(); netAuth(true); return; }
        netAuth(false);
        $('app').classList.add('hidden');
        $('pinbox').classList.remove('hidden');
        $('pinerr').textContent = a.error || 'Wrong PIN';
        SW.store('sw-pin', '');
      },
      onError: flashError,
      onReceipt: deskReceipt,
      onStatus: function (on) { netLinked = on; $('conn').textContent = on ? 'connected' : 'reconnecting…'; $('conn').style.color = on ? '' : 'var(--amber)'; }
    });
  }

  // Reports and crew links are PIN-protected: carry the PIN this page logged in with.
  function afterAuth() {
    var q = pinUsed ? '?pin=' + encodeURIComponent(pinUsed) : '';
    $('reportLink').href = '/report' + q;
    $('csvLink').href = '/api/report.csv' + q;
    $('networkLink').href = '/api/report/network.csv' + q;
    loadCrewLinks();
  }

  function loadCrewLinks() {
    fetch('/api/crew-links', { headers: { 'X-Stagewatch-Pin': pinUsed }, cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .then(function (j) {
        var box = $('crewLinks');
        box.textContent = '';
        (j.links || []).forEach(function (l) {
          var url = location.origin + l.path;
          var row = SW.el('div', 'crew');
          row.appendChild(SW.el('span', 'what', l.label));
          var open = SW.el('a', 'pill', 'Open'); open.href = l.path; open.target = '_blank';
          var copy = SW.el('button', 'small', 'Copy');
          copy.onclick = function () {
            if (navigator.clipboard) navigator.clipboard.writeText(url).then(function () { copy.textContent = 'Copied'; }, function () {});
          };
          var qr = SW.el('button', 'small', 'QR');
          qr.onclick = function () {
            var holder = $('crewQr');
            holder.textContent = '';
            var img = SW.el('img'); img.alt = 'QR code for ' + l.label; img.src = '/api/qr?text=' + encodeURIComponent(url);
            holder.appendChild(img);
            holder.appendChild(SW.el('div', 'muted', l.label));
            holder.classList.remove('hidden');
          };
          row.appendChild(open); row.appendChild(copy); row.appendChild(qr);
          box.appendChild(row);
        });
        if (!j.required) box.appendChild(SW.el('div', 'muted', 'Crew keys are off (Settings → Remote): any screen can confirm cues.'));
      })
      .catch(function () {});
  }

  function pill(text, cls) { return SW.el('span', 'pill ' + (cls || ''), text); }
  function signed(s) { return Math.abs(s) < 30 ? 'on time' : (s > 0 ? '+' : '−') + SW.fmt(Math.abs(s)); }

  function render(s) {
    state = s;
    if (holdShown !== (s.holding || '')) renderHolding();
    document.documentElement.style.setProperty('--timer', s.color || '#fff');
    $('room').textContent = s.room || 'This room';
    $('clock').textContent = s.timeOfDay;
    $('title').textContent = (s.preview ? 'PREVIEW · ' : '') + (s.title || '—');
    $('timer').textContent = s.text;
    $('cue').textContent = '';
    if (s.cue) $('cue').appendChild(SW.el('span', 'cue-badge', s.cue));
    var hasBar = s.mode === 'countdown' && s.total > 0;
    $('bar').style.visibility = hasBar ? 'visible' : 'hidden';
    if (hasBar) $('fill').style.width = (Math.max(0, Math.min(1, s.seconds / s.total)) * 100).toFixed(2) + '%';

    var st = $('status');
    st.textContent = '';
    if (s.offset !== null) st.appendChild(pill(signed(s.offset) + (s.offset > 30 ? ' behind' : s.offset < -30 ? ' ahead' : ''), s.offset > 60 ? 'late' : s.offset < -60 ? 'early' : ''));
    if (s.hardOut) st.appendChild(pill(s.hardOut, s.hardOutMargin !== null && s.hardOutMargin < 0 ? 'late' : ''));
    if (s.finishBy) st.appendChild(pill(s.finishBy));
    if (s.stretching) st.appendChild(pill('ELASTIC', 'warn'));
    if (s.slides) st.appendChild(pill('Slide ' + s.slides.slide + '/' + s.slides.count + (s.slides.pace !== 'unknown' ? ' · ' + (s.slides.pace === 'behind' ? SW.fmt(s.slides.ghost) + ' behind rehearsal' : s.slides.pace === 'ahead' ? SW.fmt(-s.slides.ghost) + ' ahead' : 'on pace') : ''), s.slides.pace === 'behind' ? 'warn' : ''));
    if (s.speech) st.appendChild(pill('Script ' + s.speech.percent + '% · ' + s.speech.wpm + ' wpm'));
    if (s.playout) st.appendChild(pill('VT ' + SW.fmt(s.playout.remaining), 'warn'));
    if (s.clockOffsetMs !== null && Math.abs(s.clockOffsetMs) > 1000) st.appendChild(pill('PC clock off by ' + (Math.abs(s.clockOffsetMs) / 1000).toFixed(1) + ' s', 'late'));
    $('forecast').textContent = s.forecast || s.schedule || '';

    var counting = s.state === 'running' || (s.state === 'expired' && s.seconds < 0);
    $('toggle').textContent = counting ? 'Pause' : 'Start';
    $('toggle').className = counting ? 'warn' : 'primary';

    $('ack').textContent = s.messageAck === 'sent' ? '⏳ Waiting for the speaker to acknowledge “' + s.message + '”'
      : s.messageAck ? '✓ Speaker acknowledged “' + s.message + '”' + (s.messageAckBy ? ' (' + s.messageAckBy + ')' : '') : (s.message ? 'On screen: “' + s.message + '”' : '');

    renderSignals(s);
    renderStandby(s);
    renderQuestions(s);
    renderAdvice(s);
    renderDesk(s);
    renderNetwork(s);
    renderRows(s);
    $('delayInfo').textContent = s.delay > 0 ? '· delay ' + SW.fmt(s.delay) + ' before next' : '';
  }

  // Holding images: tap to put one on every screen, tap again (or Off) to take it down.
  function renderHolding() {
    var box = $('holdList');
    var on = state && state.holding || '';
    holdShown = on;
    box.textContent = '';
    var list = config && config.holding || [];
    list.forEach(function (h) {
      var b = SW.el('button', 'hold' + (h.id === on ? ' on' : ''));
      var img = SW.el('img');
      img.alt = '';
      img.loading = 'lazy';
      img.src = '/api/holding/' + encodeURIComponent(h.id);
      b.appendChild(img);
      b.appendChild(SW.el('span', '', (h.id === on ? '● ' : '') + h.name));
      b.onclick = function () { send('holding', h.id === on ? 'off' : h.id); };
      box.appendChild(b);
    });
    if (!list.length) box.appendChild(SW.el('div', 'muted', 'No holding images yet — upload one, or add them on the Holding tab in Stagewatch.'));
    $('holdOn').textContent = on ? '· on air: ' + (state.holdingName || '') : '';
  }

  $('holdUpload').onclick = function () { $('holdFile').click(); };
  $('holdFile').onchange = function () {
    var files = Array.prototype.slice.call($('holdFile').files || []);
    $('holdFile').value = '';
    var msg = $('holdMsg');
    files.reduce(function (p, f) {
      return p.then(function () {
        if (f.size > 25 * 1024 * 1024) { msg.textContent = f.name + ' is larger than 25 MB'; return; }
        msg.textContent = 'Uploading ' + f.name + '…';
        var name = f.name.replace(/\.[^.]+$/, '');
        return fetch('/api/holding/upload?name=' + encodeURIComponent(name), {
          method: 'POST', body: f, headers: { 'X-Stagewatch-Pin': pinUsed, 'Content-Type': f.type || 'application/octet-stream' }
        }).then(function (r) { return r.json(); }).then(function (j) {
          msg.textContent = j.ok ? 'Uploaded ' + f.name : (j.error || 'Upload failed');
        }).catch(function () { msg.textContent = 'Upload failed — check the connection'; });
      });
    }, Promise.resolve());
  };

  function renderDepts() {
    var box = $('depts');
    box.textContent = '';
    (config && config.departments || []).forEach(function (d) {
      var sb = SW.el('button', '', 'SB ' + d);
      sb.onclick = function () { send('signal', 'standby', d); };
      var go = SW.el('button', 'primary', 'GO ' + d);
      go.onclick = function () { send('signal', 'go', d); };
      box.appendChild(sb);
      box.appendChild(go);
    });
  }

  // ALL GO: every department standing by gets its GO at once (a cue that spans departments).
  $('allGo').onclick = function () { send('signal', 'go', 'all'); };
  function renderStandby(s) {
    var standing = (s.signals || []).filter(function (g) { return g.kind === 'standby' && g.state !== 'cancelled'; });
    var btn = $('allGo');
    btn.disabled = standing.length === 0;
    btn.textContent = standing.length ? 'ALL GO (' + standing.length + ')' : 'ALL GO';
    $('standing').textContent = standing.length
      ? 'Standing by: ' + standing.map(function (g) { return g.target + (g.text ? ' ' + g.text : ''); }).join(' · ')
      : '';
  }

  function renderSignals(s) {
    var box = $('signals');
    box.textContent = '';
    (s.signals || []).slice().reverse().forEach(function (g) {
      var row = SW.el('div', 'sig');
      var stateCls = g.escalated ? 'late' : g.state === 'sent' ? 'warn' : g.state === 'done' ? 'early' : '';
      row.appendChild(pill(g.state === 'sent' ? (g.escalated ? 'NO ACK' : 'SENT') : g.state.toUpperCase(), stateCls));
      row.appendChild(SW.el('span', 'what', g.kind + ' → ' + g.target + (g.text ? ': ' + g.text : '') + (g.by ? ' (' + g.by + ')' : '')));
      if (g.state === 'sent' || g.state === 'seen') {
        var again = SW.el('button', 'small', 'Resend');
        again.onclick = function () { send('signal', 'resend', g.id); };
        var x = SW.el('button', 'small', '✕');
        x.onclick = function () { send('signal', 'clear', g.id); };
        row.appendChild(again);
        row.appendChild(x);
      }
      box.appendChild(row);
    });
  }

  function renderQuestions(s) {
    $('qaSummary').textContent = s.qaPending + ' new · ' + s.qaQueued + ' queued · about ' + s.qaFit + ' fit in the time left';
    var box = $('questions');
    box.textContent = '';
    (s.questions || []).forEach(function (q) {
      var row = SW.el('div', 'q ' + q.state);
      row.appendChild(SW.el('span', 'txt', (q.state === 'starred' ? '★ ' : '') + q.text + (q.name ? ' — ' + q.name : '')));
      function b(label, action, cls) {
        var x = SW.el('button', 'small ' + (cls || ''), label);
        x.onclick = function () { send('q', q.id, action); };
        row.appendChild(x);
      }
      if (q.state === 'new') { b('Approve', 'approve', 'primary'); b('★', 'star'); }
      if (q.state === 'approved') b('★', 'star');
      if (q.state !== 'live') b('Live', 'live');
      else b('Answered', 'answered', 'primary');
      b('✕', 'dismiss');
      box.appendChild(row);
    });
  }

  function renderAdvice(s) {
    var box = $('advice');
    box.textContent = '';
    var list = s.advice || [];
    if (!list.length) { box.appendChild(SW.el('div', 'muted', 'No suggestions — the show is tracking.')); return; }
    list.forEach(function (a) {
      var row = SW.el('div', 'advice ' + a.level);
      row.appendChild(SW.el('span', 'txt', a.text));
      if (a.command) {
        var b = SW.el('button', 'small primary', a.label || 'Apply');
        b.onclick = function () { runLine(a.command); b.disabled = true; };
        row.appendChild(b);
      }
      box.appendChild(row);
    });
  }

  // The room's verdict ('ready for doors'). The chip reads the frame's network: ready while no row of Stagewatch's preflight is
  // red; amber is counted and never blocks; each state is a shape and words, never colour alone. Why — a reason per node — is
  // read from GET /api/preflight each time the frame's verdict changes: its red rows stand under the chip while the room is not
  // ready, its red and amber rows when the chip is pressed. A frame with no network (a host that keeps no preflight) draws no
  // chip: a missing verdict is never drawn as ready. The route is read only with a PIN this Stagewatch took and while the link
  // is up: a refused PIN is never sent again (each send would count against this device), and an answer of another version
  // than this page reads is not drawn.
  var NET_ROWS = 8;
  var netDrawn = null, netAsked = '', netOpen = false, netRead = null, netFailed = false, netAuthed = false, netLinked = false;

  // The PIN was taken, or refused: what was read with it is forgotten either way, and the next frame asks again once it is taken.
  function netAuth(ok) {
    netAuthed = ok;
    netAsked = ''; netRead = null; netFailed = false; netDrawn = null;
    if (ok && state) renderNetwork(state);
  }

  function loadPreflight(key) {
    if (!netAuthed) return;
    fetch('/api/preflight', { headers: { 'X-Stagewatch-Pin': pinUsed || '' }, cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error('not read'); return r.json(); })
      .then(function (p) {
        if (!p || p.v !== 1 || !Array.isArray(p.rows)) throw new Error('not a preflight this page reads');
        if (netAsked === key) { netRead = p; netFailed = false; }
      })
      .catch(function () { if (netAsked === key) { netRead = null; netFailed = true; } })
      .then(function () { if (netAsked === key && state) { netDrawn = null; renderNetwork(state); } });
  }

  function renderNetwork(s) {
    var n = s.network, chip = $('netReady'), box = $('netBox');
    chip.classList.toggle('hidden', !n);
    if (!n) { box.classList.add('hidden'); netDrawn = null; netAsked = ''; netRead = null; return; }
    var verdict = JSON.stringify(n);
    if (netAuthed && verdict !== netAsked) { netAsked = verdict; loadPreflight(verdict); }
    var key = verdict + '|' + netOpen + '|' + netFailed + '|' + (netRead ? netRead.at : '');
    if (key === netDrawn) return;
    netDrawn = key;
    var ready = n.ready && !n.stuck;
    chip.textContent = n.stuck ? '✕ No verdict — Stagewatch is stuck'
      : !n.ready ? '✕ Not ready for doors · ' + n.red + ' red' + (n.amber ? ' · ' + n.amber + ' to look at' : '')
      : n.amber ? '△ Ready for doors · ' + n.amber + ' to look at' : '✓ Ready for doors';
    chip.className = 'pill ' + (!ready ? 'late' : n.amber ? 'warn' : 'ok');
    // The rows drawn are of the verdict the chip says: one read for another verdict (the room changed since) is not drawn.
    var read = netRead && !!netRead.ready === !!n.ready && netRead.red === n.red && netRead.amber === n.amber ? netRead : null;
    var show = !ready || netOpen;
    box.classList.toggle('hidden', !show);
    if (!show) return;
    var rows = $('netRows');
    rows.textContent = '';
    var listed = read ? read.rows.filter(function (r) { return r.light === 'red' || (netOpen && r.light === 'amber'); }) : [];
    listed.slice(0, NET_ROWS).forEach(function (r) {
      var line = SW.el('div', 'netrow ' + r.light, (r.light === 'red' ? '✕ ' : '△ ') + (r.name || r.id) + ' — ' + r.words
        + (r.configured ? ' (' + r.configured + ')' : '') + (r.fix ? ' · ' + r.fix : ''));
      rows.appendChild(line);
    });
    $('netNote').textContent = !read ? (netFailed ? 'The reasons could not be read here — see Stagewatch → Producer → Show network.' : 'Reading the reasons…')
      : listed.length > NET_ROWS ? '+' + (listed.length - NET_ROWS) + ' more — see Stagewatch → Producer → Show network'
      : listed.length === 0 ? (netOpen ? 'Every row is green.' : '') : (read.words || '');
  }

  $('netReady').onclick = function () { netOpen = !netOpen; if (state) renderNetwork(state); };
  // A row's words age ("not heard for 14 s") while the verdict's counts stand: while rows are shown they are read again.
  setInterval(function () {
    if (netAuthed && netLinked && state && state.network && netAsked && !$('netBox').classList.contains('hidden')) loadPreflight(netAsked);
  }, 5000);

  // The approval desk (Show Bus §11): what other show apps and their AIs propose waits here for a person (this Stagewatch's own
  // copilot's proposals wait at its desk). The frame's desk block names how many wait and the oldest; a decision names that
  // proposal by its id, so a press decides the one this page showed and never one that arrived since. A high proposal is approved
  // by a second press, which sends the word high with its id.
  var HIGH_ARM_MS = 5000;
  // A press counts only on what has stood still this long. Whatever is newly drawn — another proposal, the armed Approve, a long
  // line read whole — and whatever was just pressed takes no press until then, so the second click of a double-click, or a press
  // aimed at the proposal before, neither confirms a high proposal it only armed nor decides one nobody read.
  var DESK_DWELL_MS = 800;
  var deskDrawn = null, deskArmed = '', deskArmTimer = null;
  var deskShown = '', deskHeld = false, deskHoldTimer = null;
  var deskWhole = { id: '', line: null, failed: false };

  function holdDesk() {
    deskHeld = true;
    clearTimeout(deskHoldTimer);
    deskHoldTimer = setTimeout(function () { deskHeld = false; if (state) renderDesk(state); }, DESK_DWELL_MS);
  }

  // The frame carries a line's first characters and says when there are more (cut). Nobody approves a part: the whole line is
  // read from the desk by the id shown, and until it stands on the page — or when it can't be read — Approve takes no press.
  function wholeLine(n) {
    if (!n.cut) return n.line;
    if (deskWhole.id !== n.id) {
      var want = deskWhole = { id: n.id, line: null, failed: false };
      fetch('/api/proposals/' + encodeURIComponent(n.id), { headers: { 'X-Stagewatch-Pin': pinUsed || '' }, cache: 'no-store' })
        .then(function (r) { if (!r.ok) throw new Error('not read'); return r.json(); })
        .then(function (p) {
          if (p.id !== n.id || typeof p.line !== 'string' || p.line.lastIndexOf(n.line, 0) !== 0) throw new Error('not the line shown');
          want.line = p.line;
        })
        .catch(function () { want.failed = true; })
        .then(function () { if (deskWhole === want && state) { holdDesk(); renderDesk(state); } });
    }
    return deskWhole.line;
  }

  function renderDesk(s) {
    var d = s.desk;
    $('deskBox').classList.toggle('hidden', !d);
    if (!d) { deskDrawn = null; deskShown = ''; return; }
    var n = d.next;
    if (deskArmed && (!n || n.id !== deskArmed)) disarmDesk();
    var id = n ? n.id : '';
    if (id !== deskShown) { deskShown = id; if (n) holdDesk(); }
    var whole = n ? wholeLine(n) : '';
    var unread = whole === null;
    // Drawn again only when it reads differently: a frame a second must not replace a button between its press and release.
    var key = JSON.stringify(d) + '|' + deskArmed + '|' + deskHeld + '|' + (unread ? (deskWhole.failed ? 'unread' : 'reading') : whole.length);
    if (key === deskDrawn) return;
    deskDrawn = key;
    $('deskCount').textContent = d.pending ? '· ' + d.pending + ' waiting' : '';
    var box = $('desk');
    box.textContent = '';
    if (!n) box.appendChild(SW.el('div', 'muted', 'Nothing from other apps waits for approval.'));
    else {
      var item = SW.el('div', 'deskitem ' + n.risk);
      item.id = 'deskNext';
      item.dataset.id = n.id;
      var head = SW.el('div', 'row');
      head.appendChild(pill(n.risk.toUpperCase(), n.risk === 'high' ? 'late' : n.risk === 'review' ? 'warn' : ''));
      var who = SW.el('span', 'who grow', n.from + ' → ' + n.target + ' · ' + n.id.split('-')[0]);
      who.title = (n.fromId || '') + ' → ' + (n.targetId || '');   // the node ids the names stand for
      head.appendChild(who);
      item.appendChild(head);
      var line = SW.el('div', 'line', unread ? n.line + '…' : whole);
      line.id = 'deskLine';
      item.appendChild(line);
      var why = !n.collects ? n.target + ' is not collecting — approve once it asks again'
        : unread && deskWhole.failed ? 'This line is longer than shown and could not be read here — read it at the desk'
        : unread ? 'Reading the whole line…' : '';
      var acts = SW.el('div', 'row');
      var armed = deskArmed === n.id;
      var approve = SW.el('button', 'small ' + (armed ? 'warn' : 'primary'), armed ? 'Approve HIGH — press again' : 'Approve');
      approve.id = 'deskApprove';
      approve.disabled = !n.collects || unread || deskHeld;
      if (!n.collects) approve.title = n.target + ' is not asking this desk for its approved lines: it would not collect this';
      else if (unread) approve.title = 'The line is longer than shown: it is approved only once the whole of it stands here';
      approve.onclick = function () {
        if (deskHeld) return;
        if (n.risk === 'high' && !armed) { armDesk(n.id); return; }
        disarmDesk();
        holdDesk();
        if (n.risk === 'high') send('desk', 'approve', n.id, 'high'); else send('desk', 'approve', n.id);
        if (state) renderDesk(state);
      };
      var reject = SW.el('button', 'small', 'Reject');
      reject.id = 'deskReject';
      reject.disabled = deskHeld;
      reject.onclick = function () {
        if (deskHeld) return;
        disarmDesk();
        holdDesk();
        send('desk', 'reject', n.id);
        if (state) renderDesk(state);
      };
      acts.appendChild(approve);
      acts.appendChild(reject);
      if (why) { var note = SW.el('span', 'muted', why); note.id = 'deskWhy'; acts.appendChild(note); }
      item.appendChild(acts);
      box.appendChild(item);
      if (d.pending > 1) box.appendChild(SW.el('div', 'muted', '+ ' + (d.pending - 1) + ' more behind it, oldest first'));
    }
    $('deskApps').textContent = (d.apps || []).length
      ? 'Apps: ' + d.apps.map(function (a) { return a.name + (a.collects ? ' ✓ collecting' : ' — not collecting') + (a.verified ? '' : ', verbs unverified'); }).join(' · ')
      : '';
  }

  // The first press on a high proposal: it arms Approve, which then takes no press for the dwell and falls back after HIGH_ARM_MS.
  function armDesk(id) {
    deskArmed = id;
    holdDesk();
    clearTimeout(deskArmTimer);
    deskArmTimer = setTimeout(function () { deskArmed = ''; if (state) renderDesk(state); }, HIGH_ARM_MS);
    if (state) renderDesk(state);
  }
  function disarmDesk() { deskArmed = ''; clearTimeout(deskArmTimer); }

  // What a desk line did, in the desk's own words; a refusal also flashes like any other.
  function deskReceipt(r) {
    if (!r.ok) flashError(r.error || 'refused');
    $('deskMsg').textContent = r.ok ? (r.effect || '') : (r.error || '');
  }

  function renderRows(s) {
    var body = $('rows');
    body.textContent = '';
    var groupsByFirst = {};
    (s.groups || []).forEach(function (g) { groupsByFirst[g.first] = g; });
    (s.timings || []).forEach(function (t) {
      var g = groupsByFirst[t.i];
      if (g) {
        // A group header: name, planned length against its target, and when it's expected to end.
        var gr = SW.el('tr', 'group');
        var cell = SW.el('td', '');
        cell.colSpan = 7;
        cell.appendChild(SW.el('b', '', g.name));
        var bits = [SW.fmt(g.planned) + (g.target ? ' / ' + SW.fmt(g.target) + ' target' : '')];
        if (g.expectedEnd) bits.push('ends ' + g.expectedEnd);
        if (g.targetText && g.targetText !== 'on target') bits.push(g.targetText);
        cell.appendChild(SW.el('span', /^over/.test(g.targetText || '') ? 'who over' : 'who', '  ' + bits.join(' · ')));
        gr.appendChild(cell);
        body.appendChild(gr);
      }
      var tr = SW.el('tr', t.status);
      tr.appendChild(SW.el('td', 'n', String(t.i + 1)));
      var name = SW.el('td', '');
      name.appendChild(document.createTextNode(t.title + (t.fixed ? ' 📌' : '') + (t.hardOut ? ' ⏹ ' + t.hardOut : '')));
      if (t.labels && t.labels.length) name.appendChild(SW.labelChips(t.labels, s.labelColors));
      if (t.speaker || t.kind !== 'talk' || t.adjust) name.appendChild(SW.el('span', 'who', [t.speaker, t.kind, SW.fmt(t.seconds), t.adjust ? (t.adjust > 0 ? '+' : '−') + SW.fmt(Math.abs(t.adjust)) : ''].filter(Boolean).join(' · ')));
      tr.appendChild(name);
      tr.appendChild(SW.el('td', 'n', t.planned));
      tr.appendChild(SW.el('td', 'n' + (t.conflict ? ' conflict' : ''), t.status === 'done' ? '' : t.expected));
      tr.appendChild(SW.el('td', 'n', t.actual ? t.actual + (t.actualEnd ? '–' + t.actualEnd : '') : ''));
      tr.appendChild(SW.el('td', 'n', t.status === 'done' ? '' : (Math.abs(t.delay) < 30 ? '' : (t.delay > 0 ? '+' : '−') + SW.fmt(Math.abs(t.delay)))));
      var act = SW.el('td', 'act');
      if (t.status !== 'done' && t.status !== 'live') {
        var go = SW.el('button', 'small', 'Load');
        go.onclick = function () { send('goto', t.i + 1); };
        act.appendChild(go);
      }
      if (t.status !== 'done') {
        var cut = SW.el('button', 'small', '−1m');
        cut.onclick = function () { send('trim', t.i + 1, -60); };
        var add = SW.el('button', 'small', '+1m');
        add.onclick = function () { send('trim', t.i + 1, 60); };
        act.appendChild(cut);
        act.appendChild(add);
      }
      tr.appendChild(act);
      body.appendChild(tr);
    });
  }

  /** A full command line (assistant suggestions): split into verb + args, respecting quotes. */
  function runLine(line) {
    var parts = [], cur = '', quoted = false;
    for (var i = 0; i < line.length; i++) {
      var ch = line[i];
      if (ch === '"') { quoted = !quoted; continue; }
      if (!quoted && /\s/.test(ch)) { if (cur) { parts.push(cur); cur = ''; } continue; }
      cur += ch;
    }
    if (cur) parts.push(cur);
    if (parts.length) send.apply(null, parts);
  }

  document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-cmd]');
    if (b) {
      var args = [b.dataset.cmd];
      if (b.dataset.arg !== undefined) args.push(b.dataset.arg);
      if (b.dataset.arg2 !== undefined) args.push(b.dataset.arg2);
      send.apply(null, args);
    }
    var s = e.target.closest('[data-sig]');
    if (s) {
      var p = s.dataset.sig.split(' ');
      send('signal', p[0], p[1], s.dataset.text);
    }
    var d = e.target.closest('[data-delay]');
    if (d && state) send('delay', (state.delay || 0) + Number(d.dataset.delay));
  });

  $('toggle').onclick = function () { send('toggle'); };
  $('sendAck').onclick = function () {
    var text = $('msg').value.trim();
    if (!text) return;
    send('signal', 'message', 'speaker', text);
    $('msg').value = '';
  };
  $('msg').addEventListener('keydown', function (e) { if (e.key === 'Enter') $('sendAck').click(); });
  $('callNext').onclick = function () {
    if (!state) return;
    var t = state.timings || [];
    var n = t[state.item + 1];
    if (!n) { flashError('No next item'); return; }
    send('signal', 'call', 'backstage', (n.speaker || n.title) + ' to stage');
  };
  $('addNote').onclick = function () {
    var text = $('note').value.trim();
    if (!text) return;
    send('note', text);
    $('note').value = '';
  };

  $('pinok').onclick = function () {
    var pin = $('pin').value.trim();
    SW.store('sw-pin', pin);
    $('pinerr').textContent = '';
    if (conn) { location.reload(); return; }
    start(pin);
  };
  $('pin').addEventListener('keydown', function (e) { if (e.key === 'Enter') $('pinok').click(); });

  var fromUrl = SW.q('pin');
  if (fromUrl) SW.store('sw-pin', fromUrl);
  start(fromUrl || SW.store('sw-pin') || '');
  SW.keepAwake();
})();
