// Countdown to anything in the running order, for backstage, green rooms and foyers.
//   ?to=next            the next item to start (default)
//   ?to=3               item 3 (as numbered in the running order)
//   ?to=end             the end of the show
//   ?to=group:Morning   the end of a group (add &at=start for its start)
//   &label=Doors%20open  your own words above the time
// Times come from Stagewatch's live schedule (expected, so delays are included) and count on locally between frames.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var to = (SW.q('to') || 'next').trim();
  var atStart = SW.q('at') === 'start';
  var ownLabel = SW.q('label');
  var last = null;

  /** What we're counting to: { label, seconds (from the frame), title, when } or null. */
  function target(s) {
    var t = s.timings || [];
    if (!t.length) return null;
    if (to === 'end') {
      var lastItem = t[t.length - 1];
      return lastItem.endIn === null ? null : { label: 'Show ends in', seconds: lastItem.endIn, title: s.room || 'The show', when: 'Expected end ' + lastItem.expectedEnd, done: 'Show ended' };
    }
    if (to.toLowerCase().indexOf('group:') === 0) {
      var name = to.slice(6).toLowerCase();
      var g = (s.groups || []).filter(function (x) { return x.name.toLowerCase() === name; })[0];
      if (!g) return { label: '', seconds: null, title: 'No group called “' + to.slice(6) + '”', when: '' };
      return atStart
        ? { label: g.name + ' starts in', seconds: g['in'], title: g.name, when: 'Planned ' + g.plannedStart, done: g.name + ' has started' }
        : { label: g.name + ' ends in', seconds: g.endIn, title: g.name, when: 'Expected end ' + g.expectedEnd + (g.targetText ? '  ·  ' + g.targetText : ''), done: g.name + ' has ended' };
    }
    var item = null;
    if (to === 'next') {
      for (var i = 0; i < t.length; i++) if (t[i].status === 'upcoming' && t[i].kind !== 'buffer') { item = t[i]; break; }
      if (!item) return { label: '', seconds: null, title: 'Nothing else today', when: '' };
    } else {
      var n = parseInt(to, 10);
      item = isNaN(n) ? null : t[n - 1];
      if (!item) return { label: '', seconds: null, title: 'No item ' + to, when: '' };
    }
    return {
      label: 'Starts in', seconds: item['in'],
      title: item.title + (item.speaker ? ' — ' + item.speaker : ''),
      when: (item.fixed ? 'At ' : 'Planned ') + item.planned + (item.expected && item.expected !== item.planned ? '  ·  expected ' + item.expected : ''),
      done: item.status === 'done' ? 'Finished' : 'Now'
    };
  }

  var lastText = '';
  function tick() {
    if (last) {
      var x = target(last);
      var label = '', big = '--:--', cls = '', what = '', when = '';
      if (x) {
        what = x.title; when = x.when; label = ownLabel || x.label;
        if (x.seconds !== null && x.seconds !== undefined) {
          var left = x.seconds - SW.since();
          if (left > 0.5) big = SW.span(left);
          else { big = x.done || 'Now'; cls = 'now'; label = ownLabel || ''; }
        } else big = '';
      }
      var key = label + '|' + big + '|' + cls + '|' + what + '|' + when;
      if (key !== lastText) {
        lastText = key;
        $('label').textContent = label;
        $('big').textContent = big;
        $('big').className = cls;
        $('what').textContent = what;
        $('when').textContent = when;
      }
    }
    requestAnimationFrame(tick);
  }

  SW.connect({ detail: true, onState: function (s) { last = s; SW.holding(s); }, onStatus: function (on) { $('offline').classList.toggle('hidden', on); } });
  SW.keepAwake();
  requestAnimationFrame(tick);
})();
