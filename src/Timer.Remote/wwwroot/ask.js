(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  $('text').addEventListener('input', function () { $('count').textContent = $('text').value.length + ' / 280'; });
  fetch('/api/state', { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (s) {
    if (s.title) $('session').textContent = 'Session: ' + s.title + '. The moderator will pick questions to put to the speaker.';
  }).catch(function () {});
  $('send').onclick = function () {
    var text = $('text').value.trim();
    if (!text) { $('msg').className = 'bad'; $('msg').textContent = 'Type a question first.'; return; }
    $('send').disabled = true;
    var url = '/api/question/' + encodeURIComponent(text) + ($('name').value.trim() ? '/' + encodeURIComponent($('name').value.trim()) : '');
    fetch(url, { method: 'POST' }).then(function (r) {
      return r.json().then(function (j) { return { ok: r.ok, j: j }; });
    }).then(function (res) {
      if (res.ok) { $('msg').className = 'ok'; $('msg').textContent = 'Thanks — your question has been sent.'; $('text').value = ''; $('count').textContent = '0 / 280'; }
      else { $('msg').className = 'bad'; $('msg').textContent = res.j.error || 'Not sent — try again.'; }
    }).catch(function () { $('msg').className = 'bad'; $('msg').textContent = 'No connection — try again.'; })
      .then(function () { setTimeout(function () { $('send').disabled = false; }, 10000); });
  };
})();
