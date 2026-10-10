// The holographic display page. Two hosts, one renderer:
//  • a browser screen: frames arrive over the WebSocket (SW.connect);
//  • Stagewatch's output window (WebView2): frames arrive as web messages, and we answer with a heartbeat each
//    second so the app can fall back to its normal display the moment rendering stops.
(function () {
  'use strict';
  var embedded = !!(window.chrome && window.chrome.webview);
  var post = function (m) { if (embedded) { try { window.chrome.webview.postMessage(m); } catch (e) {} } };
  var msg = document.getElementById('msg');

  function failed(reason) {
    post({ type: 'fail', reason: reason });
    if (!embedded) {
      msg.classList.remove('hidden');
      msg.innerHTML = '';
      msg.appendChild(document.createTextNode('The holographic display needs WebGL (' + reason + '). '));
      var a = document.createElement('a');
      a.href = '/viewer' + location.search.replace(/([?&])style=holo&?/, '$1');
      a.textContent = 'Open the standard display';
      msg.appendChild(a);
    }
  }

  if (!window.StagewatchHolo || !StagewatchHolo.supported()) { failed('WebGL is not available on this device'); return; }


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

  var view = StagewatchHolo.start(document.getElementById('holo'), Object.assign(urlOptions(), {
    onFail: failed,
    onBeat: function (b) { post({ type: 'beat', fps: b.fps, tier: b.tier }); }
  }));
  if (view.failed) return;

  if (embedded) {
    window.chrome.webview.addEventListener('message', function (e) {
      var m = e.data;
      if (!m) return;
      if (m.type === 'state') view.update(m);
      else if (m.type === 'config') view.configure(m);
    });
    post({ type: 'ready' });
  } else {
    SW.connect({
      onState: function (s) {
        view.update(s);
        SW.holding(s);
      },
      onStatus: function (on) { document.getElementById('offline').classList.toggle('hidden', on); }
    });
    SW.keepAwake();
    document.addEventListener('click', function () {
      var el = document.documentElement, req = el.requestFullscreen || el.webkitRequestFullscreen;
      if (req && !document.fullscreenElement) { try { req.call(el); } catch (e) {} }
    });
  }
})();
