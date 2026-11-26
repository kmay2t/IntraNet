// Scripts globales de la intranet (se cargan en todas las páginas desde _Layout).
// Cada bloque consulta primero su elemento: las páginas que no lo tienen simplemente lo omiten.

// Regla de búsqueda compartida (/js/filtro.js): compara sin acentos ni mayúsculas.
var Busca = window.Filtro || { norm: function (x) { return String(x == null ? '' : x).toLowerCase(); } };

// reloj de la cabecera y saludo del hero
(function () {
  var c = document.getElementById('clock'), s = document.getElementById('saludo');
  if (!c && !s) return;
  function tick() {
    var d = new Date();
    var texto = d.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long' }) + ' · ' +
      d.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
    if (c) c.textContent = texto;
    if (!s) return;
    var h = d.getHours();
    var g = h < 12 ? 'Buenos días' : h < 19 ? 'Buenas tardes' : 'Buenas noches';
    s.innerHTML = g + ', <em>equipo</em>';
  }
  tick(); setInterval(tick, 30000);
})();

// búsqueda de accesos (hero) con atajo de teclado "/"
(function () {
  var q = document.getElementById('q');
  var apps = [].slice.call(document.querySelectorAll('.app'));
  var empty = document.getElementById('empty');
  if (!q || !apps.length) return;
  q.addEventListener('input', function () {
    var t = Busca.norm(q.value.trim()), n = 0;
    apps.forEach(function (a) {
      var hay = Busca.norm(a.textContent + ' ' + a.dataset.k);
      var show = !t || hay.indexOf(t) > -1;
      a.hidden = !show; if (show) n++;
    });
    // Oculta los encabezados de grupo que se quedaron sin tarjetas visibles.
    [].slice.call(document.querySelectorAll('.grupo')).forEach(function (g) {
      var visibles = g.querySelectorAll('.app:not([hidden])').length;
      g.hidden = visibles === 0;
    });
    if (empty) empty.hidden = n > 0;
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === '/' && document.activeElement !== q) { e.preventDefault(); q.focus(); }
  });
})();

// malla de circuitos del fondo (canvas)
(function () {
  var cv = document.getElementById('mesh');
  if (!cv) return;
  var ctx = cv.getContext('2d');
  if (!ctx) return;
  function rnd(seed) { return function () { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; }; }
  function draw() {
    var styles = getComputedStyle(document.documentElement);
    function color(name) { return styles.getPropertyValue(name).trim() || 'transparent'; }
    var line = color('--mesh-line'), goldLine = color('--mesh-gold-line');
    var dot = color('--mesh-dot'), goldDot = color('--mesh-gold-dot');
    var dpr = Math.min(window.devicePixelRatio || 1, 2), w = innerWidth, h = innerHeight;
    cv.width = w * dpr; cv.height = dpr * h; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    // Fondo transparente: deja ver la marca de agua (emblema) que pinta el CSS del body.
    ctx.clearRect(0, 0, w, h);
    var g = ctx.createRadialGradient(w * .85, 0, 0, w * .85, 0, Math.max(w, h) * .8);
    g.addColorStop(0, color('--mesh-glow')); g.addColorStop(1, color('--mesh-fade'));
    ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
    var r = rnd(42), step = 48, cols = Math.ceil(w / step) + 1, rows = Math.ceil(h / step) + 1;
    ctx.lineWidth = 1; ctx.lineCap = 'square';
    for (var i = 0; i < 70; i++) {
      var x = Math.floor(r() * cols) * step, y = Math.floor(r() * rows) * step, gold = r() < .14;
      ctx.strokeStyle = gold ? goldLine : line;
      ctx.beginPath(); ctx.moveTo(x, y);
      var dir = Math.floor(r() * 4), len = 3 + Math.floor(r() * 5);
      var dx = [1, 0, -1, 0], dy = [0, 1, 0, -1];
      for (var k = 0; k < len; k++) {
        var seg = 1 + Math.floor(r() * 3);
        x += dx[dir] * step * seg; y += dy[dir] * step * seg; ctx.lineTo(x, y);
        if (r() < .5) { var d2 = (dir + (r() < .5 ? 1 : 3)) % 4; var bx = dx[d2] + dx[dir], by = dy[d2] + dy[dir]; x += bx * step * .5; y += by * step * .5; ctx.lineTo(x, y); dir = d2; }
      }
      ctx.stroke();
      ctx.fillStyle = gold ? goldDot : dot;
      ctx.beginPath(); ctx.arc(x, y, gold ? 3 : 2.5, 0, 6.283); ctx.fill();
    }
  }
  draw(); var t; addEventListener('resize', function () { clearTimeout(t); t = setTimeout(draw, 120); });
  addEventListener('meditechnosys:themechange', draw);
})();

// estado de sistemas desde /api/status (panel lateral)
(function () {
  var box = document.getElementById('status');
  if (!box) return;
  function render(items) {
    box.innerHTML = '';
    items.forEach(function (i) {
      var d = document.createElement('div'), n = document.createElement('span'), c = document.createElement('code');
      n.textContent = i.name; c.textContent = i.label;
      if (i.state !== 'ok') c.className = 'warn';
      d.appendChild(n); d.appendChild(c); box.appendChild(d);
    });
  }
  function load() {
    fetch('/api/status', { headers: { Accept: 'application/json' } })
      .then(function (r) { if (!r.ok) throw 0; return r.json(); })
      .then(render).catch(function () {});
  }
  load(); setInterval(load, 60000);
})();
