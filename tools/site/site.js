// Draws each figure's scene over its SVG, and gives a sampled live figure its control. Without a
// script, or when a scene cannot be fetched (a page opened from disk), the SVG stays.
(function () {
  if (typeof MareyView === 'undefined') return;

  // The canvas draws the themes' text in the faces the PNG backend carries, declared in
  // site.css; a canvas does not ask for a face, so they are loaded before anything is drawn.
  var faces = ['400 12px Inter', '500 12px Inter', '600 12px Inter', '700 12px Inter',
               '400 12px "Liberation Serif"', 'italic 400 12px "Liberation Serif"', '700 12px "Liberation Serif"'];
  var ready = document.fonts && document.fonts.load
    ? Promise.all(faces.map(function (f) { return document.fonts.load(f).catch(function () {}); }))
    : Promise.resolve();

  function draw(cv, scene) {
    var why = MareyView.show(cv, scene, Number(cv.dataset.w), Number(cv.dataset.h));
    if (why) { console.warn('Marey: ' + why); return; }
    cv.hidden = false;
    var poster = cv.parentNode.querySelector('.poster');
    if (poster) poster.hidden = true;
  }

  function fetched(url, as) {
    return fetch(url).then(function (r) { return r.ok ? r[as]() : Promise.reject(r.status); });
  }

  function live(cv, d) {
    var box = document.createElement('div');
    box.className = 'control';
    var label = document.createElement('label');
    label.textContent = d.label;
    var input = document.createElement('input');
    input.type = 'range';
    input.min = '0';
    input.max = String(d.scenes.length - 1);
    input.step = '1';
    input.value = String(d.start);
    var shown = document.createElement('output');
    label.appendChild(input);
    box.appendChild(label);
    box.appendChild(shown);
    var stage = cv.parentNode;
    stage.parentNode.insertBefore(box, stage);
    function at(i) { draw(cv, d.scenes[i]); shown.textContent = d.shown[i]; }
    input.addEventListener('input', function () { at(Number(input.value)); });
    at(d.start);
  }

  ready.then(function () {
    document.querySelectorAll('canvas[data-scene]').forEach(function (cv) {
      fetched(cv.dataset.scene, 'text').then(function (s) { draw(cv, s.trim()); }).catch(function () {});
    });
    document.querySelectorAll('canvas[data-live]').forEach(function (cv) {
      fetched(cv.dataset.live, 'json').then(function (d) { live(cv, d); }).catch(function () {});
    });
  });
})();
