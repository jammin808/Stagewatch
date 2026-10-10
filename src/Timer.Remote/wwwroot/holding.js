// A holding screen: shows the holding image while one is on air, black otherwise.
// Add ?idle=blank to hide the faint name when nothing is showing.
(function () {
  'use strict';
  var idle = document.getElementById('idle');
  if (SW.q('idle') === 'blank') idle.textContent = '';
  SW.connect({
    onState: function (s) { idle.style.visibility = SW.holding(s) ? 'hidden' : 'visible'; },
    onStatus: function (on) { document.getElementById('offline').classList.toggle('hidden', on); }
  });
  SW.keepAwake();
})();
