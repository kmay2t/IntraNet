/* =============================================================================
   hoja-fecha.js — la hoja de calendario compartida de la intranet

   Es la misma hoja que se construyó para la Bitácora (un solo cuadro que abre
   una rejilla con cuatro modos: Día · Mes · Año · Rango), puesta en un archivo
   suelto para las pantallas que consultan por fechas:
     /admin/reportes/   /gestion/historico/   /patologia/consulta/
     /dicom/            /dicom/enviar/
   La Bitácora conserva su propia copia dentro de su página: allí la hoja sí sabe
   qué días tienen movimientos (consulta /api/bitacora/periodos) y apaga los que
   no; aquí no hay historial que consultar, así que todos los días son válidos.

   Uso en cualquier página con un par de campos <input type="date">:
     <link rel="stylesheet" href="/css/hoja-fecha.css?v=20261104a" />
     <script src="/js/hoja-fecha.js?v=20261104a"></script>
     HojaFecha.instalar({ desde: 'fDesde', hasta: 'fHasta' });

   Y en cualquier página que filtre además por hora, con un par de campos que guarden
   'HH:MM' (el de cierre en 'HH:59', la hora entera incluida):
     HojaFecha.instalarHora({ desde: 'fHoraDesde', hasta: 'fHoraHasta' });
   Sale la misma caja de la Bitácora —reloj + «Todas las horas» + hoja de 24 horas con
   «Una hora · Rango» y los atajos Todo el día / Mañana / Tarde—. `cfg.conteos` es opcional:
   si devuelve {'07': 3, …} cada hora dice en su título cuántas entradas caen en ella.

   El campo nativo NO se borra: se esconde y sigue siendo el dueño del valor, en
   yyyy-mm-dd, los dos extremos incluidos. La hoja escribe ahí y dispara
   `change`, así que el filtrado que cada página ya tenía sigue funcionando sin
   tocar su código. Si la página escribe el valor por su cuenta (botones «Hoy»,
   «Últimos 7 días» o «Limpiar»), el cuadro se actualiza solo: se vigila el
   propio `value` del campo y el `reset` de su formulario.
   ========================================================================== */
(function () {
  'use strict';

  var MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
    'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
  var DIAS_SEM = ['l', 'm', 'x', 'j', 'v', 's', 'd'];
  var MODOS = [['dia', 'Día'], ['mes', 'Mes'], ['anio', 'Año'], ['rango', 'Rango']];
  var ICONO_CAL = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
    + 'stroke-width="2" aria-hidden="true"><rect x="3" y="4" width="18" height="17" rx="2"/>'
    + '<line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/>'
    + '<line x1="3" y1="10" x2="21" y2="10"/></svg>';
  var ICONO_BAJA = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
    + 'stroke-width="2" aria-hidden="true"><polyline points="6 9 12 15 18 9"/></svg>';

  // ---------- utilidades sueltas ----------
  function p(n) { return (n < 10 ? '0' : '') + n; }
  function hoyito() { var d = new Date(); return { a: d.getFullYear(), m: d.getMonth(), d: d.getDate() }; }
  function ultimoDia(a, m) { return new Date(a, m + 1, 0).getDate(); }
  function claveDia(a, m, d) { return a + '-' + p(m + 1) + '-' + p(d); }
  function esFecha(s) { return /^\d{4}-\d{2}-\d{2}$/.test(String(s == null ? '' : s).trim()); }
  // '2026-11-03' → '03/11/2026', como se lee la fecha en todas las tablas.
  function diaLegible(k) { var t = String(k || '').split('-'); return t.length === 3 ? t[2] + '/' + t[1] + '/' + t[0] : ''; }
  // Un enlace o una página pueden traer '03/11/2026'; el campo lo quiere '2026-11-03'.
  function normalizar(s) {
    s = String(s == null ? '' : s).trim();
    if (esFecha(s)) return s;
    var t = s.split('/');
    if (t.length === 3 && /^\d{4}$/.test(t[2]) && /^\d{1,2}$/.test(t[1]) && /^\d{1,2}$/.test(t[0])) {
      return t[2] + '-' + p(Number(t[1])) + '-' + p(Number(t[0]));
    }
    return '';
  }
  function n(tag, clase, texto) {
    var e = document.createElement(tag);
    if (clase) e.className = clase;
    if (texto != null) e.textContent = texto;
    return e;
  }
  function boton(parente, texto, accion, titulo) {
    var b = n('button');
    b.type = 'button';
    b.textContent = texto;
    if (titulo) b.title = titulo;
    b.addEventListener('click', accion);
    parente.appendChild(b);
    return b;
  }
  function marcarChip(contenedor, activo, atributo) {
    [].forEach.call(contenedor.querySelectorAll('button'), function (b) {
      b.className = b.dataset[atributo] === activo ? 'hf-sel' : '';
    });
  }
  function resolver(x) { return typeof x === 'string' ? document.getElementById(x) : x; }
  function saltarEvento(el, tipo) {
    var ev;
    try { ev = new Event(tipo, { bubbles: true }); }
    catch (e) {
      ev = document.createEvent('Event');
      ev.initEvent(tipo, true, true);
    }
    el.dispatchEvent(ev);
  }

  // El `set` nativo de .value, para escribir sin que la vigilancia se llame a sí misma.
  var SET_NATIVO = (function () {
    try {
      var d = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
      return d && d.set ? d : null;
    } catch (e) { return null; }
  })();

  var INSTANCIAS = [];
  var _abierta = null;          // la instancia con la hoja a la vista
  var _escuchando = false;      // si ya están puestos los manejadores globales

  // ---------- abrir, cerrar y colocar (una sola vez para toda la página) ----------
  function posicionar(inst) {
    if (inst.hoja.hidden) return;
    var r = inst.caja.getBoundingClientRect();
    var ancho = inst.hoja.offsetWidth, alto = inst.hoja.offsetHeight;
    var y;
    if (window.innerHeight - r.bottom >= alto + 12) y = r.bottom + 8;
    else if (r.top >= alto + 12) y = r.top - alto - 8;
    else y = Math.max(8, window.innerHeight - alto - 8);
    var x = Math.max(8, Math.min(r.left, window.innerWidth - ancho - 8));
    inst.hoja.style.left = Math.round(x) + 'px';
    inst.hoja.style.top = Math.round(y) + 'px';
  }

  function reposicionar() {
    INSTANCIAS.forEach(function (i) { if (!i.hoja.hidden) posicionar(i); });
  }

  // El primer clic de fuera solo cierra la hoja; la página hace lo que tenga que hacer
  // con el segundo (clic en otro campo, en la tabla, etc.).
  function fueraDeHoja(e) {
    var inst = _abierta;
    if (!inst) return;
    if (inst.hoja.contains(e.target) || inst.campo.contains(e.target)) return;
    inst.cerrarHoja();
  }

  function porTecla(e) {
    if (e.key === 'Escape' && _abierta) { _abierta.cerrarHoja(); }
  }

  function escuchar() {
    if (_escuchando) return;
    _escuchando = true;
    document.addEventListener('pointerdown', fueraDeHoja, true);
    document.addEventListener('keydown', porTecla, true);
    window.addEventListener('resize', reposicionar);
    window.addEventListener('scroll', reposicionar, true);
  }

  function dejarDeEscuchar() {
    if (!_escuchando || _abierta) return;
    _escuchando = false;
    document.removeEventListener('pointerdown', fueraDeHoja, true);
    document.removeEventListener('keydown', porTecla, true);
    window.removeEventListener('resize', reposicionar);
    window.removeEventListener('scroll', reposicionar, true);
  }

  // ---------- la instalación ----------
  function instalar(cfg) {
    cfg = cfg || {};
    var desde = resolver(cfg.desde);
    var hasta = resolver(cfg.hasta);
    if (!desde) return null;

    var ya = null;
    INSTANCIAS.forEach(function (i) { if (i.desde === desde) ya = i; });
    if (ya) return ya;

    var tituloCampo = cfg.titulo || 'Periodo';
    var cualquier = cfg.cualquier || 'Cualquier día';

    // Los dos campos nativos se quedan donde están (las páginas los buscan por id y
    // los leen con .value), pero se tapan: a partir de aquí la manera de escribirles
    // es la hoja.
    var anclaD = desde.closest ? (desde.closest('label') || desde.parentNode) : desde.parentNode;
    var anclaH = hasta ? (hasta.closest ? (hasta.closest('label') || hasta.parentNode) : hasta.parentNode) : null;
    if (anclaD === anclaH) {
      // Si las dos fechas viven en el mismo envoltorio, lo que se tapa son los campos y no
      // el envoltorio: taparlo dejaría el hueco, tapar los campos deja el cuadro solo.
      desde.classList.add('hf-origen');
      if (hasta) hasta.classList.add('hf-origen');
    } else {
      [anclaD, anclaH].forEach(function (a) {
        if (a) { a.classList.add('hf-origen'); a.setAttribute('aria-hidden', 'true'); }
      });
    }

    var campo = n('div', 'hf-campo');
    campo.innerHTML =
      '<span class="hf-titulo">' + tituloCampo + '</span>' +
      '<div class="hf-fila">' +
        '<button type="button" class="hf-caja" aria-haspopup="dialog" aria-expanded="false">'
        + ICONO_CAL + '<span class="hf-txt hf-vacio">' + cualquier + '</span>' + ICONO_BAJA + '</button>' +
        '<button type="button" class="hf-quitar" aria-label="Quitar el filtro" title="Quitar el filtro" hidden>×</button>' +
      '</div>';
    if (anclaD && anclaD.parentNode) anclaD.parentNode.insertBefore(campo, anclaD.nextSibling);
    else desde.parentNode.insertBefore(campo, desde.nextSibling);

    var caja = campo.querySelector('.hf-caja');
    var txt = campo.querySelector('.hf-txt');
    var quitar = campo.querySelector('.hf-quitar');

    var hoja = n('div', 'hf-hoja');
    hoja.setAttribute('role', 'dialog');
    hoja.setAttribute('aria-label', 'Hoja para elegir el periodo');
    hoja.hidden = true;
    hoja.innerHTML =
      '<div class="hf-modos"></div>' +
      '<div class="hf-cab">' +
        '<button type="button" class="hf-flecha hf-atras" aria-label="Anterior" title="Anterior">‹</button>' +
        '<span class="hf-titulo-mes">—</span>' +
        '<button type="button" class="hf-flecha hf-adelante" aria-label="Siguiente" title="Siguiente">›</button>' +
      '</div>' +
      '<div class="hf-dow"></div>' +
      '<div class="hf-dias"></div>' +
      '<div class="hf-pies"><span class="hf-ct"></span><span class="hf-botones"></span></div>';
    document.body.appendChild(hoja);

    var modos = hoja.querySelector('.hf-modos');
    var cab = hoja.querySelector('.hf-cab');
    var tit = hoja.querySelector('.hf-titulo-mes');
    var atras = hoja.querySelector('.hf-atras');
    var adelante = hoja.querySelector('.hf-adelante');
    var dow = hoja.querySelector('.hf-dow');
    var rejilla = hoja.querySelector('.hf-dias');
    var nota = hoja.querySelector('.hf-ct');
    var botones = hoja.querySelector('.hf-botones');
    DIAS_SEM.forEach(function (x) { dow.appendChild(n('span', '', x)); });
    MODOS.forEach(function (m) {
      var b = n('button');
      b.type = 'button';
      b.textContent = m[1];
      b.dataset.modo = m[0];
      b.title = { dia: 'Un solo día', mes: 'Todo un mes', anio: 'Todo un año', rango: 'Del día inicial al día final' }[m[0]];
      modos.appendChild(b);
    });

    var est = { modo: 'dia', mes: null, bloque: null, pendiente: '' };

    function valor() {
      return { d: normalizar(desde.value), h: normalizar(hasta ? hasta.value : '') };
    }

    // De qué modo viene lo escrito, para que el rótulo diga «noviembre de 2026» y no
    // «01/11/2026 – 30/11/2026» cuando la hoja no fue la que lo puso.
    function modoDe(d, h) {
      if (!d) return '';
      if (!esFecha(h) || h === d) return 'dia';
      var t = d.split('-'), u = h.split('-');
      if (t[0] === u[0] && t[1] === u[1] && t[2] === '01' && u[2] === p(ultimoDia(Number(t[0]), Number(t[1]) - 1))) return 'mes';
      if (t[0] === u[0] && d === t[0] + '-01-01' && h === t[0] + '-12-31') return 'anio';
      return 'rango';
    }

    function etiqueta() {
      var v = valor();
      if (!v.d) return cualquier;
      var modo = modoDe(v.d, v.h) || est.modo;
      if (modo === 'mes') return MESES[Number(v.d.split('-')[1]) - 1] + ' de ' + v.d.slice(0, 4);
      if (modo === 'anio') return v.d.slice(0, 4);
      if (modo === 'rango') return diaLegible(v.d) + ' – ' + diaLegible(v.h);
      return diaLegible(v.d);
    }

    // ---------- pintar ----------
    function pintar() {
      var v = valor();
      var texto = etiqueta();
      txt.textContent = texto;
      txt.className = 'hf-txt ' + (v.d ? 'hf-puesto' : 'hf-vacio');
      quitar.hidden = !v.d;
      caja.title = v.d
        ? tituloCampo + ': ' + texto + '. Pulse para cambiarlo'
        : 'Elegir el periodo: un día, un mes, un año o un rango de fechas';
      if (!hoja.hidden) pintarHoja();
    }

    function mirar() {
      if (est.mes) return est.mes;
      var v = valor();
      if (v.d) { var t = v.d.split('-'); return { a: Number(t[0]), m: Number(t[1]) - 1 }; }
      return hoyito();
    }

    function pintarHoja() {
      marcarChip(modos, est.modo, 'modo');
      if (est.modo === 'anio') { cab.className = 'hf-cab'; dow.className = 'hf-dow hf-oculto'; rejilla.className = 'hf-dias cuatro'; pintarAnios(); }
      else if (est.modo === 'mes') { cab.className = 'hf-cab'; dow.className = 'hf-dow hf-oculto'; rejilla.className = 'hf-dias tres'; pintarMeses(); }
      else { cab.className = 'hf-cab'; dow.className = 'hf-dow'; rejilla.className = 'hf-dias'; pintarDias(); }
      pintarBotones();
    }

    // Rejilla del mes, lunes primero. Todos los días se pueden pulsar: aquí no se
    // consulta ningún historial, así que no hay días «sin datos» que apagar.
    function pintarDias() {
      var v = valor();
      var mir = mirar();
      var h = hoyito();
      var enRango = est.modo === 'rango';
      var d = v.d, z = v.h || v.d;
      est.mes = mir;
      tit.textContent = MESES[mir.m] + ' de ' + mir.a;
      rejilla.innerHTML = '';
      var desplaz = (new Date(mir.a, mir.m, 1).getDay() + 6) % 7;
      for (var i = 0; i < desplaz; i++) rejilla.appendChild(n('span'));
      for (var dd = 1; dd <= ultimoDia(mir.a, mir.m); dd++) {
        var k = claveDia(mir.a, mir.m, dd);
        var b = rejilla.appendChild(n('button'));
        b.type = 'button';
        b.dataset.dia = k;
        b.textContent = String(dd);
        var cls = '';
        if (mir.a === h.a && mir.m === h.m && dd === h.d) cls += ' hf-hoy';
        if (enRango) {
          if (k === est.pendiente) cls += ' hf-pendiente';
          if (d && (k === d || k === z)) cls += ' hf-sel';
          else if (d && z && k > d && k < z) cls += ' hf-enrango';
        } else if (d && k === d) cls += ' hf-sel';
        b.className = cls;
        b.title = 'Pulse ' + diaLegible(k);
      }
      nota.textContent = enRango
        ? (est.pendiente ? 'Elija el día final (después del ' + diaLegible(est.pendiente) + ')'
          : (d ? 'Del ' + diaLegible(d) + ' al ' + diaLegible(z) : 'Pulse el día inicial y luego el final'))
        : (d ? 'Periodo: ' + etiqueta() : 'Sin periodo: consulta todos los días');
      atras.title = 'Mes anterior';
      adelante.title = 'Mes siguiente';
    }

    // Los doce meses del año que se ve; las flechas cambian de año.
    function pintarMeses() {
      var v = valor();
      var a = mirar().a;
      var d = v.d, z = v.h || v.d;
      est.mes = { a: a, m: mirar().m };
      tit.textContent = String(a);
      rejilla.innerHTML = '';
      for (var m = 0; m < 12; m++) {
        var pref = a + '-' + p(m + 1);
        var ini = pref + '-01';
        var fin = pref + '-' + p(ultimoDia(a, m));
        var b = rejilla.appendChild(n('button'));
        b.type = 'button';
        b.dataset.mes = pref;
        b.textContent = MESES[m];
        var cls = '';
        if (modoDe(d, z) === 'rango' && d && !(fin < d || ini > z)) cls += ' hf-enrango';
        else if (d && d.slice(0, 7) === pref) cls += ' hf-sel';
        b.className = cls;
        b.title = 'Todo ' + MESES[m] + ' de ' + a;
      }
      nota.textContent = d ? 'Periodo: ' + etiqueta() : 'Elija un mes completo';
      atras.title = 'Año anterior';
      adelante.title = 'Año siguiente';
    }

    // Bloque de doce años; sin historial no se sabe cuáles existen, así que las flechas
    // mueven el bloque de doce en doce (y al abrir la hoja el bloque se acomoda sobre el
    // año que esté elegido, para que se vea siempre el del periodo en curso).
    function pintarAnios() {
      var v = valor();
      var a = est.bloque || (v.d ? Number(v.d.slice(0, 4)) : hoyito().a);
      if (!est.bloque) est.bloque = Math.floor(a / 12) * 12;
      tit.textContent = est.bloque + ' – ' + (est.bloque + 11);
      rejilla.innerHTML = '';
      var h = hoyito();
      for (var y = est.bloque; y < est.bloque + 12; y++) {
        var b = rejilla.appendChild(n('button'));
        b.type = 'button';
        b.dataset.anio = String(y);
        b.textContent = String(y);
        b.className = (v.d && v.d.slice(0, 4) === String(y) ? 'hf-sel' : '') + (y === h.a ? ' hf-hoy' : '');
        b.title = 'Todo ' + y;
      }
      nota.textContent = v.d ? 'Periodo: ' + etiqueta() : 'Elija un año completo';
      atras.title = 'Doce años antes';
      adelante.title = 'Doce años después';
    }

    // El pie cambia con el modo: los atajos que tienen sentido en cada uno.
    function pintarBotones() {
      botones.innerHTML = '';
      if (est.modo === 'anio') {
        boton(botones, 'Este año', function () {
          var hh = hoyito();
          aplicar('anio', hh.a + '-01-01', hh.a + '-12-31');
        });
        boton(botones, 'Cualquier año', function () { aplicar('', '', ''); });
        return;
      }
      if (est.modo === 'mes') {
        boton(botones, 'Este mes', function () {
          var hh = hoyito();
          aplicar('mes', claveDia(hh.a, hh.m, 1), claveDia(hh.a, hh.m, ultimoDia(hh.a, hh.m)));
        });
        boton(botones, 'Mes pasado', function () {
          var hh = hoyito();
          var m = hh.m - 1, a = hh.a;
          if (m < 0) { m = 11; a--; }
          aplicar('mes', claveDia(a, m, 1), claveDia(a, m, ultimoDia(a, m)));
        });
        boton(botones, 'Cualquier mes', function () { aplicar('', '', ''); });
        return;
      }
      if (est.modo === 'rango') {
        boton(botones, '7 días', function () { tras(6); }, 'De hoy hacia atrás siete días');
        boton(botones, '30 días', function () { tras(29); }, 'De hoy hacia atrás treinta días');
        boton(botones, '90 días', function () { tras(89); }, 'De hoy hacia atrás noventa días');
        boton(botones, 'Cualquier día', function () { aplicar('', '', ''); });
        return;
      }
      boton(botones, 'Hoy', function () {
        var hh = hoyito();
        aplicar('dia', claveDia(hh.a, hh.m, hh.d), claveDia(hh.a, hh.m, hh.d));
      });
      boton(botones, 'Ayer', function () {
        var dd = new Date();
        dd.setDate(dd.getDate() - 1);
        aplicar('dia', claveDia(dd.getFullYear(), dd.getMonth(), dd.getDate()),
          claveDia(dd.getFullYear(), dd.getMonth(), dd.getDate()));
      });
      boton(botones, 'Cualquier día', function () { aplicar('', '', ''); });
    }

    function tras(dias) {
      var fin = new Date();
      var ini = new Date();
      ini.setDate(ini.getDate() - dias);
      aplicar('rango', claveDia(ini.getFullYear(), ini.getMonth(), ini.getDate()),
        claveDia(fin.getFullYear(), fin.getMonth(), fin.getDate()));
    }

    // ---------- escribir en los campos de la página ----------
    function escribir(input, val) {
      if (!input) return;
      if (SET_NATIVO) SET_NATIVO.set.call(input, val);
      else input.value = val;
    }

    function aplicar(modo, d, h) {
      est.modo = modo || 'dia';
      est.pendiente = '';
      escribir(desde, d || '');
      if (hasta) escribir(hasta, d ? (h || d) : '');
      cerrarHoja();
      pintar();
      // Con disparar `change` en el campo inicial basta: las cinco páginas que usan esta
      // hoja leen SIEMPRE los dos valores dentro del mismo manejador (reportes y
      // histórico repintan con `repintar`, patología vuelve a consultar con `buscar(1)`
      // y las dos de DICOM leen los campos al pulsar Buscar). Avisar también al campo
      // final haría dos consultas idénticas por cada periodo.
      saltarEvento(desde, 'change');
      if (typeof cfg.alAplicar === 'function') {
        try { cfg.alAplicar(); } catch (e) { /* que no falle la hoja por el callback */ }
      }
    }

    // ---------- abrir y cerrar ----------
    function abrirHoja() {
      var v = valor();
      est.pendiente = '';
      if (v.d) {
        var t = v.d.split('-');
        est.mes = { a: Number(t[0]), m: Number(t[1]) - 1 };
        est.bloque = Math.floor(Number(t[0]) / 12) * 12;
      } else {
        if (!est.mes) est.mes = hoyito();
        est.bloque = Math.floor(est.mes.a / 12) * 12;
      }
      est.modo = modoDe(v.d, v.h) || est.modo || 'dia';
      hoja.hidden = false;
      if (_abierta && _abierta !== inst) _abierta.cerrarHoja();
      _abierta = inst;
      escuchar();
      caja.setAttribute('aria-expanded', 'true');
      pintarHoja();
      posicionar(inst);
    }

    function cerrarHoja() {
      if (!hoja.hidden) hoja.hidden = true;
      caja.setAttribute('aria-expanded', 'false');
      if (_abierta === inst) _abierta = null;
      dejarDeEscuchar();
    }

    // ---------- los clics ----------
    caja.addEventListener('click', function () {
      if (hoja.hidden) abrirHoja(); else cerrarHoja();
    });
    quitar.addEventListener('click', function () { aplicar('', '', ''); });
    modos.addEventListener('click', function (e) {
      var b = e.target.closest ? e.target.closest('button') : null;
      if (!b || !b.dataset.modo) return;
      est.modo = b.dataset.modo;
      est.pendiente = '';
      var v = valor();
      if (v.d) {
        var t = v.d.split('-');
        est.mes = { a: Number(t[0]), m: Number(t[1]) - 1 };
      }
      pintarHoja();
      posicionar(inst);
    });
    atras.addEventListener('click', function () { saltar(-1); });
    adelante.addEventListener('click', function () { saltar(1); });

    // ‹ › : mes arriba/abajo en Día y Rango, año arriba/abajo en Mes, bloque de doce
    // años en Año.
    function saltar(k) {
      if (est.modo === 'anio') {
        est.bloque += 12 * k;
        est.mes = { a: est.bloque + 5, m: 0 };
      } else if (est.modo === 'mes') {
        est.mes = { a: mirar().a + k, m: 0 };
      } else {
        var mir = mirar();
        var m = mir.m + k, a = mir.a;
        if (m < 0) { m = 11; a--; } else if (m > 11) { m = 0; a++; }
        est.mes = { a: a, m: m };
      }
      pintarHoja();
      posicionar(inst);
    }

    rejilla.addEventListener('click', function (e) {
      var b = e.target.closest ? e.target.closest('button') : null;
      if (!b || b.disabled) return;
      if (b.dataset.dia) {
        var k = b.dataset.dia;
        if (est.modo !== 'rango') { aplicar('dia', k, k); return; }
        if (!est.pendiente) { est.pendiente = k; pintarHoja(); return; }
        var a = est.pendiente, z = k;
        if (z < a) { var t = a; a = z; z = t; }
        est.pendiente = '';
        aplicar('rango', a, z);
        return;
      }
      if (b.dataset.mes) {
        var pref = b.dataset.mes;
        aplicar('mes', pref + '-01', pref + '-' + p(ultimoDia(Number(pref.slice(0, 4)), Number(pref.slice(5, 7)) - 1)));
        return;
      }
      if (b.dataset.anio) {
        aplicar('anio', b.dataset.anio + '-01-01', b.dataset.anio + '-12-31');
      }
    });

    // Que el cuadro se entere cuando la página escribe en el campo por su cuenta
    // («Hoy», «Últimos 7 días», «Limpiar filtros») o cuando el formulario se reinicia.
    [desde, hasta].forEach(function (input) {
      if (!input) return;
      input.addEventListener('change', pintar);
      input.addEventListener('input', pintar);
      if (SET_NATIVO) {
        try {
          Object.defineProperty(input, 'value', {
            configurable: true,
            get: function () { return SET_NATIVO.get.call(this); },
            set: function (v) { SET_NATIVO.set.call(this, v); pintar(); }
          });
        } catch (e) { /* sin el interceptor, el cambio y el reset siguen cubriendo lo normal */ }
      }
    });
    if (desde.form && desde.form.addEventListener) {
      desde.form.addEventListener('reset', function () { setTimeout(pintar, 0); });
    }

    var inst = { desde: desde, hasta: hasta, campo: campo, caja: caja, hoja: hoja, pintar: pintar, cerrarHoja: cerrarHoja };
    INSTANCIAS.push(inst);
    pintar();
    return inst;
  }

  // Devuelve el id/elemento a su estado visible: sirve por si alguna página escribe
  // valores sin que el interceptor alcance a verlo (p. ej. tras reconstruir el DOM).
  function sincronizar(x) {
    var el = resolver(x);
    INSTANCIAS.forEach(function (i) { if (!el || i.desde === el || i.hasta === el) i.pintar(); });
  }

  // ---------- la hoja de horas (Una hora · Rango) ----------
  // Es la misma caja y la misma hoja que la Bitácora usa para filtrar por hora, en versión
  // compartida: aquí no hay historial que consultar, así que las 24 horas se pueden siempre
  // y `cfg.conteos` es opcional (si devuelve {'07': 3, …} cada hora explica en su título
  // cuántas entradas caen en ella). Escribe 'HH:MM' en los dos campos —el final en 'HH:59',
  // la hora entera incluida— y dispara `change` en el primero, que es lo que las páginas
  // ya saben escuchar.
  var ICONO_RELOJ = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
    + 'stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="9"/>'
    + '<polyline points="12 7 12 12 15.5 14"/></svg>';
  var MODOS_HORA = [['una', 'Una hora'], ['rango', 'Rango']];
  var ATAJOS_HORA = [['Todo el día', 0, 23, 'De las 00:00 a las 23:59'],
    ['Mañana', 6, 12, 'De las 06:00 a las 12:59'],
    ['Tarde', 13, 19, 'De las 13:00 a las 19:59']];

  function instalarHora(cfg) {
    cfg = cfg || {};
    var desde = resolver(cfg.desde);
    var hasta = resolver(cfg.hasta);
    if (!desde) return null;

    var ya = null;
    INSTANCIAS.forEach(function (i) { if (i.desde === desde) ya = i; });
    if (ya) return ya;

    var tituloCampo = cfg.titulo || 'Hora';
    var cualquier = cfg.cualquier || 'Todas las horas';

    function horaDe(s) {
      var t = /^(\d{1,2}):(\d{2})$/.exec(String(s == null ? '' : s).trim());
      if (!t) return null;
      var h = Number(t[1]);
      return h > 23 ? null : h;
    }
    function valor() { return { d: horaDe(desde.value), h: hasta ? horaDe(hasta.value) : null }; }
    function modoDe(v) {
      if (v.d === null) return '';
      return (v.h === null || v.h === v.d) ? 'una' : 'rango';
    }
    function etiqueta() {
      var v = valor();
      if (v.d === null) return cualquier;
      var fin = v.h === null ? v.d : v.h;
      return p(v.d) + ':00 – ' + p(fin) + ':59';
    }
    function fijaValor(input, val) {
      if (!input) return;
      if (SET_NATIVO) SET_NATIVO.set.call(input, val);
      else input.value = val;
    }
    function conteos() {
      if (typeof cfg.conteos !== 'function') return null;
      try { return cfg.conteos() || null; } catch (e) { return null; }
    }

    // El par de campos se queda donde está (la página los lee con .value) pero se tapa:
    // a partir de aquí la manera de escribirles es la hoja.
    var ancla = desde.closest ? (desde.closest('label') || desde.parentNode) : desde.parentNode;
    if (ancla) { ancla.classList.add('hf-origen'); ancla.setAttribute('aria-hidden', 'true'); }
    var campo = n('div', 'hf-campo');
    campo.innerHTML =
      '<span class="hf-titulo">' + tituloCampo + '</span>' +
      '<div class="hf-fila">' +
        '<button type="button" class="hf-caja" aria-haspopup="dialog" aria-expanded="false">'
        + ICONO_RELOJ + '<span class="hf-txt hf-vacio">' + cualquier + '</span>' + ICONO_BAJA + '</button>' +
        '<button type="button" class="hf-quitar" aria-label="Quitar el filtro de hora" '
        + 'title="Quitar el filtro de hora" hidden>×</button>' +
      '</div>';
    var padre = (ancla && ancla.parentNode) ? ancla.parentNode : desde.parentNode;
    var siguiente = (ancla && ancla.nextSibling) ? ancla.nextSibling : desde.nextSibling;
    if (padre) padre.insertBefore(campo, siguiente);

    var caja = campo.querySelector('.hf-caja');
    var txt = campo.querySelector('.hf-txt');
    var quitar = campo.querySelector('.hf-quitar');

    var hoja = n('div', 'hf-hoja');
    hoja.setAttribute('role', 'dialog');
    hoja.setAttribute('aria-label', 'Hoja para elegir la hora');
    hoja.hidden = true;
    hoja.innerHTML =
      '<div class="hf-modos"></div>' +
      '<div class="hf-dias cuatro"></div>' +
      '<div class="hf-pies"><span class="hf-ct"></span><span class="hf-botones"></span></div>';
    document.body.appendChild(hoja);

    var modos = hoja.querySelector('.hf-modos');
    var rejilla = hoja.querySelector('.hf-dias');
    var nota = hoja.querySelector('.hf-ct');
    var botones = hoja.querySelector('.hf-botones');
    MODOS_HORA.forEach(function (m) {
      var b = n('button');
      b.type = 'button';
      b.textContent = m[1];
      b.dataset.modo = m[0];
      b.title = m[0] === 'una' ? 'Solo esa hora, de HH:00 a HH:59'
        : 'De la hora inicial a la final, las dos incluidas';
      modos.appendChild(b);
    });

    var est = { modo: 'una', pendiente: null };

    function pintar() {
      var v = valor();
      var texto = etiqueta();
      txt.textContent = texto;
      txt.className = 'hf-txt ' + (v.d === null ? 'hf-vacio' : 'hf-puesto');
      quitar.hidden = v.d === null;
      caja.title = v.d === null
        ? 'Elegir una hora concreta o un rango de horas'
        : tituloCampo + ': ' + texto + '. Pulse para cambiarla';
      if (!hoja.hidden) pintarHoja();
    }

    function pintarHoja() {
      var v = valor();
      var enRango = est.modo === 'rango';
      var por = conteos();
      var conDatos = 0;
      marcarChip(modos, est.modo, 'modo');
      rejilla.innerHTML = '';
      for (var hh = 0; hh < 24; hh++) {
        var k = p(hh);
        var n0 = por ? (Number(por[k]) || 0) : 0;
        if (n0) conDatos++;
        var b = n('button');
        b.type = 'button';
        b.dataset.hora = String(hh);
        b.textContent = k + ':00';
        var cls = '';
        if (enRango) {
          if (est.pendiente === hh) cls += ' hf-pendiente';
          if (v.d !== null && hh === v.d) cls += ' hf-sel';
          else if (v.d !== null && v.h !== null && hh === v.h) cls += ' hf-sel';
          else if (v.d !== null && v.h !== null && hh > v.d && hh < v.h) cls += ' hf-enrango';
        } else if (v.d !== null && hh === v.d) cls = ' hf-sel';
        b.className = cls.replace(/^\s+/, '');
        b.title = por
          ? (n0 ? n0 + ' entrada(s) en la hora de las ' + k + ':00'
            : 'Sin entradas en la hora de las ' + k + ':00')
          : 'De las ' + k + ':00 a las ' + k + ':59';
        rejilla.appendChild(b);
      }
      nota.textContent = enRango
        ? (est.pendiente === null ? 'Elija la hora inicial'
          : 'Elija la hora final (después de las ' + p(est.pendiente) + ':00)')
        : (por ? (conDatos ? conDatos + ' hora(s) con entradas en lo recibido'
          : 'Ninguna hora de lo recibido coincide con la lista') : 'Elija la hora');
      botones.innerHTML = '';
      if (enRango) {
        ATAJOS_HORA.forEach(function (a) {
          boton(botones, a[0], function () { aplicar('rango', a[1], a[2]); }, a[3]);
        });
      }
      boton(botones, 'Cualquier hora', function () { aplicar('', null, null); });
    }

    function aplicar(modo, d, h) {
      est.modo = modo || 'una';
      est.pendiente = null;
      if (d === null || typeof d === 'undefined') {
        fijaValor(desde, '');
        fijaValor(hasta, '');
      } else {
        var fin = (typeof h === 'number') ? h : d;
        fijaValor(desde, p(d) + ':00');
        fijaValor(hasta, p(fin) + ':59');
      }
      cerrarHoja();
      pintar();
      // Como en la hoja de fechas: basta con avisar al campo inicial, que es el que las
      // páginas escuchan; los dos valores se leen juntos dentro del mismo manejador.
      saltarEvento(desde, 'change');
      if (typeof cfg.alAplicar === 'function') {
        try { cfg.alAplicar(); } catch (e) { /* que no falle la hoja por el callback */ }
      }
    }

    function abrirHoja() {
      var v = valor();
      est.pendiente = null;
      est.modo = modoDe(v) || est.modo || 'una';
      hoja.hidden = false;
      if (_abierta && _abierta !== inst) _abierta.cerrarHoja();
      _abierta = inst;
      escuchar();
      caja.setAttribute('aria-expanded', 'true');
      pintarHoja();
      posicionar(inst);
    }

    function cerrarHoja() {
      if (!hoja.hidden) hoja.hidden = true;
      caja.setAttribute('aria-expanded', 'false');
      if (_abierta === inst) _abierta = null;
      dejarDeEscuchar();
    }

    caja.addEventListener('click', function () {
      if (hoja.hidden) abrirHoja(); else cerrarHoja();
    });
    quitar.addEventListener('click', function () { aplicar('', null, null); });
    modos.addEventListener('click', function (e) {
      var b = e.target.closest ? e.target.closest('button') : null;
      if (!b || !b.dataset.modo) return;
      est.modo = b.dataset.modo;
      est.pendiente = null;
      pintarHoja();
      posicionar(inst);
    });
    rejilla.addEventListener('click', function (e) {
      var b = e.target.closest ? e.target.closest('button') : null;
      if (!b || b.disabled) return;
      var hh = Number(b.dataset.hora);
      if (est.modo !== 'rango') { aplicar('una', hh, hh); return; }
      if (est.pendiente === null) { est.pendiente = hh; pintarHoja(); posicionar(inst); return; }
      var a = est.pendiente, z = hh;
      if (z < a) { var t = a; a = z; z = t; }
      est.pendiente = null;
      aplicar('rango', a, z);
    });

    [desde, hasta].forEach(function (input) {
      if (!input) return;
      input.addEventListener('change', pintar);
      input.addEventListener('input', pintar);
      if (SET_NATIVO) {
        try {
          Object.defineProperty(input, 'value', {
            configurable: true,
            get: function () { return SET_NATIVO.get.call(this); },
            set: function (v) { SET_NATIVO.set.call(this, v); pintar(); }
          });
        } catch (e) { /* sin el interceptor, el cambio y el reset siguen cubriendo lo normal */ }
      }
    });
    if (desde.form && desde.form.addEventListener) {
      desde.form.addEventListener('reset', function () { setTimeout(pintar, 0); });
    }

    var inst = { desde: desde, hasta: hasta, campo: campo, caja: caja, hoja: hoja, pintar: pintar, cerrarHoja: cerrarHoja };
    INSTANCIAS.push(inst);
    pintar();
    return inst;
  }

  window.HojaFecha = {
    instalar: instalar, instalarHora: instalarHora, sincronizar: sincronizar, instancias: INSTANCIAS
  };
})();
