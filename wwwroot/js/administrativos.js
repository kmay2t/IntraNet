/* Pantalla "Administrativos": actas administrativas y credenciales ligadas a usuarios.
   - Actas: CRUD con folio consecutivo e impresión con membrete y firmas.
   - Credenciales: CRUD con foto recortada en el navegador a tamaño infantil (2.5 × 3 cm),
     render del formato oficial (frente/reverso) en canvas e impresión a 54 × 86 mm.
   Todo se autoriza además en el servidor (permisos actas.administrar / credenciales.administrar). */
(function () {
  'use strict';
  var G = window.Gestion;
  var puedeActas = false, puedeCred = false;
  var ACTAS = [], CRED = [], USUARIOS = [];

  // Superficie mínima (funciones puras) para previsualizar/verificar el formato impreso del acta.
  window.Administrativos = { cuerpoActa: cuerpoActa, estilosActa: estilosActa, htmlActa: htmlActa };

  var $ = function (id) { return document.getElementById(id); };

  // ======================================================================
  //  Pestañas
  // ======================================================================
  function irTab(cual) {
    var actas = cual === 'actas';
    $('tabActas').classList.toggle('on', actas);
    $('tabCred').classList.toggle('on', !actas);
    $('secActas').hidden = !actas || !puedeActas;
    $('secCred').hidden = actas || !puedeCred;
  }

  // ======================================================================
  //  Utilidades de dibujo
  // ======================================================================
  function redondeado(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }
  // Dibuja la imagen cubriendo el rectángulo (recorta lo que sobra, centrado).
  function cubrir(ctx, img, x, y, w, h) {
    var scale = Math.max(w / img.width, h / img.height);
    var dw = img.width * scale, dh = img.height * scale;
    ctx.drawImage(img, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
  }
  function envolver(ctx, texto, maxW) {
    var palabras = String(texto || '').split(/\s+/).filter(Boolean);
    var lineas = [], linea = '';
    palabras.forEach(function (p) {
      var prueba = linea ? linea + ' ' + p : p;
      if (ctx.measureText(prueba).width > maxW && linea) { lineas.push(linea); linea = p; }
      else linea = prueba;
    });
    if (linea) lineas.push(linea);
    return lineas;
  }
  function cargarImagen(src) {
    return new Promise(function (res, rej) {
      var img = new Image();
      img.onload = function () { res(img); };
      img.onerror = function () { rej(new Error('No se pudo cargar la imagen ' + src)); };
      img.src = src;
    });
  }

  // ======================================================================
  //  Recortador de foto tamaño infantil (relación 5:6 = 2.5 × 3 cm)
  // ======================================================================
  function Recortador(canvas) {
    var st = { img: null, zoom: 1, cx: 0.5, cy: 0.5 };
    canvas.width = 450; canvas.height = 540;
    var ctx = canvas.getContext('2d');

    function limites() {
      var scale = Math.max(canvas.width / st.img.width, canvas.height / st.img.height) * st.zoom;
      var dw = st.img.width * scale, dh = st.img.height * scale;
      var cxMin = canvas.width / (2 * dw), cxMax = 1 - cxMin;
      var cyMin = canvas.height / (2 * dh), cyMax = 1 - cyMin;
      st.cx = Math.min(Math.max(st.cx, cxMin), Math.max(cxMin, cxMax));
      st.cy = Math.min(Math.max(st.cy, cyMin), Math.max(cyMin, cyMax));
      return { dw: dw, dh: dh };
    }
    function dibujar() {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      if (!st.img) {
        ctx.fillStyle = '#9aa4ae';
        ctx.font = '600 22px Arial';
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText('Sin foto', canvas.width / 2, canvas.height / 2);
        return;
      }
      var d = limites();
      ctx.drawImage(st.img, canvas.width / 2 - st.cx * d.dw, canvas.height / 2 - st.cy * d.dh, d.dw, d.dh);
    }
    function salida() {
      var cv = document.createElement('canvas');
      cv.width = 600; cv.height = 720;
      var c2 = cv.getContext('2d');
      c2.fillStyle = '#ffffff'; c2.fillRect(0, 0, 600, 720);
      if (st.img) {
        var scale = Math.max(600 / st.img.width, 720 / st.img.height) * st.zoom;
        var dw = st.img.width * scale, dh = st.img.height * scale;
        var cxMin = 600 / (2 * dw), cyMin = 720 / (2 * dh);
        var cx = Math.min(Math.max(st.cx, cxMin), 1 - cxMin);
        var cy = Math.min(Math.max(st.cy, cyMin), 1 - cyMin);
        c2.drawImage(st.img, 300 - cx * dw, 360 - cy * dh, dw, dh);
      }
      return cv;
    }

    var arrastre = null;
    canvas.addEventListener('pointerdown', function (e) {
      if (!st.img) return;
      arrastre = { x: e.clientX, y: e.clientY };
      canvas.setPointerCapture(e.pointerId);
    });
    canvas.addEventListener('pointermove', function (e) {
      if (!arrastre || !st.img) return;
      var scale = Math.max(canvas.width / st.img.width, canvas.height / st.img.height) * st.zoom;
      var dw = st.img.width * scale, dh = st.img.height * scale;
      var rect = canvas.getBoundingClientRect();
      var kx = canvas.width / rect.width, ky = canvas.height / rect.height;
      st.cx -= (e.clientX - arrastre.x) * kx / dw;
      st.cy -= (e.clientY - arrastre.y) * ky / dh;
      arrastre = { x: e.clientX, y: e.clientY };
      dibujar();
    });
    canvas.addEventListener('pointerup', function () { arrastre = null; });

    return {
      setImagen: function (img) { st.img = img; st.zoom = 1; st.cx = 0.5; st.cy = 0.5; dibujar(); },
      setZoom: function (z) { st.zoom = z; dibujar(); },
      dibujar: dibujar,
      salida: salida,
      tiene: function () { return !!st.img; }
    };
  }

  // ======================================================================
  //  Render de la credencial sobre el formato oficial
  // ======================================================================
  var tpl = { frente: null, reverso: null, prom: null };
  function plantillas() {
    if (!tpl.prom) {
      tpl.prom = Promise.all([
        cargarImagen('/img/credencial/frente.png'),
        cargarImagen('/img/credencial/reverso.png')
      ]).then(function (ims) { tpl.frente = ims[0]; tpl.reverso = ims[1]; return tpl; });
    }
    return tpl.prom;
  }

  function renderFrente(cred, foto) {
    var cv = document.createElement('canvas');
    cv.width = tpl.frente.width; cv.height = tpl.frente.height;
    var x = cv.getContext('2d');
    var W = cv.width, H = cv.height, cx = W / 2;
    x.drawImage(tpl.frente, 0, 0);

    // Foto tamaño infantil dentro del marco redondeado del formato.
    if (foto) {
      var fx = 0.2853 * W, fy = 0.0500 * H, fw = 0.4311 * W, fh = 0.3219 * H;
      x.save();
      redondeado(x, fx, fy, fw, fh, 0.040 * W);
      x.clip();
      cubrir(x, foto, fx, fy, fw, fh);
      x.restore();
    }

    x.textAlign = 'center'; x.textBaseline = 'middle';
    // Nombre (negritas negras, se reduce si no cabe).
    var fn = 0.0354 * H;
    x.font = '700 ' + fn + 'px Arial, Helvetica, sans-serif';
    x.fillStyle = '#101010';
    var nombre = String(cred.nombre || '').toUpperCase();
    while (x.measureText(nombre).width > 0.74 * W && fn > 0.018 * H) {
      fn -= 2; x.font = '700 ' + fn + 'px Arial, Helvetica, sans-serif';
    }
    x.fillText(nombre, cx, 0.4615 * H);

    // Puesto (itálica gris, hasta dos líneas).
    var fp = 0.0305 * H;
    x.font = 'italic 600 ' + fp + 'px Arial, Helvetica, sans-serif';
    x.fillStyle = '#4d4d4d';
    var lineas = envolver(x, String(cred.puesto || '').toUpperCase(), 0.60 * W).slice(0, 2);
    if (lineas.length === 1) x.fillText(lineas[0], cx, 0.5780 * H);
    else if (lineas.length === 2) { x.fillText(lineas[0], cx, 0.5625 * H); x.fillText(lineas[1], cx, 0.6000 * H); }

    // NSS y cédula: valor en blanco dentro de las barras azul marino del formato.
    x.fillStyle = '#ffffff';
    x.font = '700 ' + (0.0195 * H) + 'px Arial, Helvetica, sans-serif';
    if (cred.nss) x.fillText(String(cred.nss), cx, 0.7235 * H);
    if (cred.cedula) x.fillText(String(cred.cedula), cx, 0.8140 * H);

    // Vigencia.
    x.fillStyle = '#1b3a85';
    x.font = 'italic 700 ' + (0.0245 * H) + 'px Arial, Helvetica, sans-serif';
    x.fillText('VIG: ' + (cred.vigencia || ''), cx, 0.9020 * H);
    return cv;
  }

  function renderReverso() {
    var cv = document.createElement('canvas');
    cv.width = tpl.reverso.width; cv.height = tpl.reverso.height;
    cv.getContext('2d').drawImage(tpl.reverso, 0, 0);
    return cv;
  }

  // Lienzo único con el frente a la izquierda y el reverso a la derecha, exactamente como salen en
  // la hoja impresa: sirve para descargar UNA sola imagen con ambas caras y para que, al doblar la
  // hoja por en medio, el reverso quede detrás del frente leyendo bien.
  function lienzoAmbas(cred, foto) {
    var f = renderFrente(cred, foto), r = renderReverso();
    var gap = Math.round(f.width * 0.05);
    var cv = document.createElement('canvas');
    cv.width = f.width * 2 + gap * 3;
    cv.height = f.height;
    var x = cv.getContext('2d');
    x.fillStyle = '#ffffff';
    x.fillRect(0, 0, cv.width, cv.height);
    x.drawImage(f, gap, 0);
    x.drawImage(r, gap * 2 + f.width, 0);
    return cv;
  }

  function fotoDe(cred) {
    return fetch('/api/credenciales/' + cred.id + '/foto', { credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error('Sin foto disponible.'); return r.blob(); })
      .then(function (b) { return cargarImagen(URL.createObjectURL(b)); });
  }

  function ventanaImprimir(html) {
    var w = window.open('', '_blank');
    if (!w) { window.alert('Permite las ventanas emergentes para poder imprimir.'); return; }
    w.document.open(); w.document.write(html); w.document.close();
  }

  // HTML de la hoja de impresión: UNA sola cara de papel con el frente a la izquierda y el reverso
  // a la derecha, separados por la línea de doblez. Al doblar por esa línea el reverso queda detrás
  // del frente y se lee bien. Expuesta (hojaCred) para vista previa y pruebas, como las actas.
  function htmlCred(cred, frente, reverso) {
    return '<!DOCTYPE html><html><head><title>Credencial · ' + G.esc(cred.nombre) + '</title><style>' +
      '@page { size: A4; margin: 12mm; } body { font-family: Arial, sans-serif; } ' +
      '.hoja { display: flex; align-items: flex-start; page-break-inside: avoid; break-inside: avoid; } ' +
      '.cara { width: 54mm; height: 86mm; } ' +
      '.cara img { width: 54mm; height: 86mm; display: block; } ' +
      '.pliegue { width: 0; height: 86mm; border-left: .4mm dashed #8a8a8a; margin: 0 5mm; } ' +
      '.nota { font-size: 9pt; color: #555; margin: 0 0 6mm; }' +
      '</style></head><body>' +
      '<p class="nota">Credencial de ' + G.esc(cred.nombre) + ' · ambas caras en una sola hoja, a tamaño real ' +
      '(54 × 86 mm por cara). Recorta y dobla por la línea punteada: el reverso queda detrás del frente.</p>' +
      '<div class="hoja">' +
      '<div class="cara"><img src="' + frente + '" alt="Frente"></div>' +
      '<div class="pliegue"></div>' +
      '<div class="cara"><img src="' + reverso + '" alt="Reverso"></div>' +
      '</div>' +
      '<scr' + 'ipt>window.onload = function () { setTimeout(function () { window.print(); }, 400); };</scr' + 'ipt>' +
      '</body></html>';
  }

  function hojaCred(cred, foto) {
    return htmlCred(cred, renderFrente(cred, foto).toDataURL('image/png'), renderReverso().toDataURL('image/png'));
  }

  function imprimirCred(cred) {
    return plantillas().then(function () { return fotoDe(cred); }).then(function (foto) {
      ventanaImprimir(hojaCred(cred, foto));
    });
  }

  function descargarPng(cred) {
    return plantillas().then(function () { return fotoDe(cred); }).then(function (foto) {
      // Una sola imagen con las dos caras (frente a la izquierda, reverso a la derecha).
      var cv = lienzoAmbas(cred, foto);
      cv.toBlob(function (b) {
        var a = document.createElement('a');
        a.href = URL.createObjectURL(b);
        a.download = 'credencial-' + String(cred.nombre || 'sin-nombre').toLowerCase().replace(/[^a-z0-9]+/g, '-') + '-ambas-caras.png';
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
      }, 'image/png');
    });
  }

  // Expuesto para vista previa y verificación del formato (misma idea que window.Administrativos
  // de las actas).
  window.Administrativos = window.Administrativos || {};
  window.Administrativos.plantillas = plantillas;
  window.Administrativos.hojaCred = hojaCred;
  window.Administrativos.lienzoAmbas = lienzoAmbas;

  // ======================================================================
  //  Actas administrativas
  // ======================================================================
  function cargarActas() {
    return G.api('/api/actas').then(function (l) { ACTAS = l || []; pintarActas(); });
  }

  function pintarActas() {
    var tb = $('cuerpoActas');
    $('cuentaActas').textContent = ACTAS.length ? ACTAS.length + ' acta(s) registradas' : '';
    if (!ACTAS.length) {
      tb.innerHTML = '<tr><td colspan="8" class="empty">Sin actas administrativas todavía.</td></tr>';
      return;
    }
    tb.innerHTML = ACTAS.map(function (a) {
      var etiqueta = a.tipo === 'faltas' ? 'Faltas injustificadas' : 'Mala conducta';
      var clase = 'badge-chip ' + (a.tipo === 'faltas' ? 'acta-faltas' : 'acta-conducta');
      return '<tr>' +
        '<td class="mono">' + G.esc(a.folio) + '</td>' +
        '<td><span class="' + clase + '">' + etiqueta + '</span></td>' +
        '<td>' + G.fmtFecha(a.fechaHechos) + '</td>' +
        '<td><strong>' + G.esc(a.nombre) + '</strong></td>' +
        '<td>' + G.esc(a.puesto || '—') + '</td>' +
        '<td class="mono">' + G.esc(a.usuario || '—') + '</td>' +
        '<td>' + G.esc(a.creadoPor || '—') + '</td>' +
        '<td><div class="row-actions">' +
        '<button class="btn-secondary btn-mini" data-imprimir="' + a.id + '">Imprimir</button>' +
        '<button class="btn-secondary btn-mini" data-editar="' + a.id + '">Editar</button>' +
        '<button class="btn-danger btn-mini" data-eliminar="' + a.id + '">Eliminar</button>' +
        '</div></td></tr>';
    }).join('');
  }

  // Ficha de persona de un usuario de la lista asignable (nombre completo y cargo).
  function datosUsuario(usuario) {
    return USUARIOS.filter(function (x) { return x.usuario === usuario; })[0] || null;
  }
  // Etiqueta del selector: "usuario — Nombre (rol)", para reconocer a la persona de un vistazo.
  function etiquetaUsuario(u) {
    return u.usuario + (u.nombre ? ' — ' + u.nombre : '') + ' (' + G.nombreRol(u.rol) + ')';
  }
  // Al elegir un usuario ligado, autocompleta su nombre y su puesto con los datos de su ficha.
  // Solo rellena los datos que el usuario tenga guardados; lo demás se puede escribir a mano.
  function autocompletarPersona(usuario, api, campoNombre, campoPuesto) {
    var u = datosUsuario(usuario);
    if (!u) return;
    if (u.nombre) api.set(campoNombre, u.nombre);
    if (u.cargo) api.set(campoPuesto, u.cargo);
  }

  function dialogoActa(a) {
    var ops = [{ v: '', t: '(sin usuario ligado)' }].concat(USUARIOS.map(function (u) {
      return { v: u.usuario, t: etiquetaUsuario(u) };
    }));
    var tipoOps = [
      { v: 'conducta', t: 'Mala conducta' },
      { v: 'faltas', t: 'Faltas injustificadas' }
    ];
    G.formDialog({
      titulo: a ? 'Editar acta ' + a.folio : 'Nueva acta administrativa',
      intro: a ? 'El folio no cambia: ' + a.folio
        : 'Elige el tipo de acta; el folio se asigna solo al guardar (ACTA-' + new Date().getFullYear() + '-…).',
      ancho: true,
      campos: [
        { name: 'tipo', label: 'Tipo de acta', tipo: 'select', opciones: tipoOps, required: true, value: a ? a.tipo : 'conducta' },
        { name: 'nombre', label: 'Nombre de la persona (involucrado)', required: true, maxlength: 120, value: a ? a.nombre : '' },
        { name: 'puesto', label: 'Puesto / cargo', maxlength: 80, value: a ? a.puesto : '' },
        { name: 'usuario', label: 'Usuario de intranet ligado', tipo: 'select', opciones: ops, value: a ? a.usuario : '',
          onCambio: function (v, api) { autocompletarPersona(v, api, 'nombre', 'puesto'); } },
        { name: 'lugar', label: 'Ciudad / lugar donde se levanta', maxlength: 120, value: a ? a.lugar : 'Ciudad de México' },
        { name: 'fechaHechos', label: 'Fecha de los hechos', tipo: 'date', required: true, value: a ? a.fechaHechos.slice(0, 10) : G.hoy() },
        { name: 'hora', label: 'Hora (opcional)', maxlength: 20, placeholder: '10:26', value: a ? a.hora : '' },
        { name: 'hechos', label: 'Relación de hechos', tipo: 'textarea', required: true, maxlength: 4000, value: a ? a.hechos : '',
          placeholder: 'Mala conducta: describe las faltas cometidas. Faltas injustificadas: las inasistencias (desde cuándo, cuántos días).' },
        { name: 'acuerdos', label: 'Acuerdos / sanción prevista', tipo: 'textarea', maxlength: 2000, value: a ? a.acuerdos : '',
          placeholder: 'Acuerdos, compromisos o sanción aplicable.' },
        { name: 'testigos', label: 'Testigos (nombres, uno por línea)', tipo: 'textarea', maxlength: 300, value: a ? a.testigos : '' }
      ],
      textoSubmit: a ? 'Guardar cambios' : 'Levantar acta',
      onSubmit: function (v) {
        var cuerpo = JSON.stringify({
          tipo: v.tipo || 'conducta', usuario: v.usuario || '', nombre: v.nombre, puesto: v.puesto || '',
          lugar: v.lugar || '', fechaHechos: v.fechaHechos + 'T12:00:00', hora: v.hora || '',
          hechos: v.hechos, acuerdos: v.acuerdos || '', testigos: v.testigos || ''
        });
        var prom = a
          ? G.api('/api/actas/' + a.id, { method: 'PUT', body: cuerpo })
          : G.api('/api/actas', { method: 'POST', body: cuerpo });
        return prom.then(cargarActas);
      }
    });
  }

  // Datos fijos de la empresa para el preámbulo legal.
  var EMPRESA = 'MEDITECHNOSYS GROUP S.A. DE C.V.';
  var DOMICILIO = 'AV. MONTEVIDEO 498, COL. SAN BARTOLO ATEPEHUACAN, C.P. 07730, CIUDAD DE MÉXICO';
  var EMPRESA_SC = 'Meditechnosys Group S.A. de C.V.';
  var DOMICILIO_SC = 'Av. Montevideo 498, Col. San Bartolo Atepehuacan, C.P. 07730, Ciudad de México';
  var MESES = ['ENERO', 'FEBRERO', 'MARZO', 'ABRIL', 'MAYO', 'JUNIO', 'JULIO',
    'AGOSTO', 'SEPTIEMBRE', 'OCTUBRE', 'NOVIEMBRE', 'DICIEMBRE'];

  function partesFecha(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) d = new Date();
    return { dia: d.getDate(), mes: MESES[d.getMonth()], anio: d.getFullYear() };
  }

  function lineasTestigos(a) {
    return String(a.testigos || '').split('\n').map(function (t) { return t.trim(); }).filter(Boolean);
  }

  function bloqueFirmas(lista) {
    return '<div class="firmas">' + lista.map(function (f) {
      return '<div class="firma"><span class="raya"></span><b>' + G.esc(f.nombre || '') + '</b>' +
        G.esc(f.titulo) + '</div>';
    }).join('') + '</div>';
  }

  function estilosActa() {
    return '' +
      '@page { size: letter; margin: 22mm 20mm; } ' +
      'body { font-family: "Times New Roman", Georgia, serif; font-size: 12.5pt; color: #111; line-height: 1.55; } ' +
      '.enc { display: flex; align-items: center; gap: 14px; border-bottom: 2.5px solid #b58900; padding-bottom: 10px; } ' +
      '.enc img { width: 78px; height: auto; } ' +
      '.enc .nom { font-family: Arial, sans-serif; font-weight: 800; font-size: 17pt; color: #8a6d1a; } ' +
      '.enc .sub { font-family: Arial, sans-serif; font-size: 9pt; color: #8a6d1a; } ' +
      '.enc .dir { margin-left: auto; text-align: right; font-family: Arial, sans-serif; font-size: 8.5pt; color: #444; max-width: 46%; } ' +
      'h1 { text-align: center; font-size: 14pt; letter-spacing: .08em; margin: 26px 0 4px; } ' +
      '.folio { text-align: center; font-family: Arial, sans-serif; font-size: 10.5pt; color: #333; margin: 0 0 22px; } ' +
      'p { margin: 0 0 12px; text-align: justify; } ' +
      '.sec { font-family: Arial, sans-serif; font-weight: 700; font-size: 10.5pt; letter-spacing: .08em; text-transform: uppercase; margin: 18px 0 6px; color: #1f3864; } ' +
      '.caja { white-space: pre-wrap; margin: 0 0 12px; text-align: justify; } ' +
      '.firmas { display: flex; flex-wrap: wrap; gap: 60px 24px; margin-top: 70px; } ' +
      '.firma { flex: 1 1 40%; min-width: 200px; text-align: center; font-family: Arial, sans-serif; font-size: 9pt; color: #333; } ' +
      '.firma .raya { display: block; border-top: 1px solid #111; margin-bottom: 4px; } ' +
      '.firma b { display: block; font-size: 10pt; color: #111; min-height: 13px; } ' +
      '.pie { margin-top: 44px; border-top: 1px solid #999; padding-top: 6px; font-family: Arial, sans-serif; font-size: 8.5pt; color: #444; text-align: center; } ';
  }

  // Contenido del acta (membrete + título + cuerpo legal + firmas + pie). Función pura:
  // la usa `imprimirActa` y sirve también para previsualizar/verificar el formato.
  function cuerpoActa(a) {
    var logo = location.origin + '/img/credencial/logo.png';
    var f = partesFecha(a.fechaHechos);
    var hora = (a.hora || '').trim() || '________';
    var lugar = (a.lugar || 'Ciudad de México').trim();
    var caja = function (t) { return t ? '<div class="caja">' + G.esc(t) + '</div>' : ''; };
    var lig = a.usuario ? ', titular de la cuenta de usuario <b>' + G.esc(a.usuario) + '</b> en la intranet' : '';
    var testigos = lineasTestigos(a);

    var esFaltas = a.tipo === 'faltas';
    var titulo = esFaltas ? 'ACTA ADMINISTRATIVA POR FALTAS' : 'ACTA ADMINISTRATIVA';
    var subtitulo = esFaltas ? 'Faltas injustificadas' : 'Mala conducta';
    var cuerpo;

    if (esFaltas) {
      // Formato 2 — redacción en minúsculas, narrativa de inasistencias y sanción.
      var firmasF = [{ titulo: 'JEFE DIRECTO' }, { titulo: 'ELABORÓ' }, { titulo: 'INVOLUCRADO', nombre: a.nombre }];
      if (testigos.length) testigos.forEach(function (t) { firmasF.push({ titulo: 'TESTIGO', nombre: t }); });
      else { firmasF.push({ titulo: 'TESTIGO' }); firmasF.push({ titulo: 'TESTIGO' }); }
      cuerpo =
        '<p>En la ciudad de ' + G.esc(lugar) + ', siendo las ' + G.esc(hora) + ' horas del día ' + f.dia +
        ' del mes de ' + f.mes.charAt(0) + f.mes.slice(1).toLowerCase() + ' del año ' + f.anio +
        ', en el domicilio de la empresa ' + EMPRESA_SC +
        ', situada en ' + DOMICILIO_SC +
        ', se encuentran reunidos quienes al calce firman, así como los testigos que se indican, y de forma ' +
        'voluntaria hacen constar los hechos que se describen a continuación.</p>' +
        '<div class="sec">Hechos</div>' +
        '<p>En uso de las facultades de mi cargo hago constar que <b>' + G.esc(a.nombre) + '</b>, quien ocupa el ' +
        'cargo de <b>' + G.esc(a.puesto || 'no especificado') + '</b>' + lig + ', incurrió en las siguientes faltas:</p>' +
        caja(a.hechos) +
        '<p>A <b>' + G.esc(a.nombre) + '</b> se le ha intentado contactar sin obtener respuesta alguna; hasta el ' +
        'momento de la redacción de la presente acta no ha habido comunicación ni se ha presentado la documentación ' +
        'que justifique las repetidas inasistencias que, en su caso, contravienen el contrato firmado.</p>' +
        (a.acuerdos ? '<div class="sec">Sanción prevista y acuerdos</div>' + caja(a.acuerdos) : '') +
        '<p>Para validar la información expuesta se toma en consideración lo declarado por los testigos que al calce ' +
        'firman, quienes hacen constar que los hechos mencionados son ciertos.</p>' +
        '<p>Luego de obtener los testimonios de las partes, se da por concluida la presente acta a las ' + G.esc(hora) +
        ' horas del día ' + f.dia + ' de ' + f.mes.charAt(0) + f.mes.slice(1).toLowerCase() + ' de ' + f.anio +
        ', con firma de los testigos y demás involucrados en el hecho.</p>' +
        bloqueFirmas(firmasF);
    } else {
      // Formato 1 — acta general (mala conducta), cláusulas legales en mayúsculas.
      var firmasC = [{ titulo: 'INVOLUCRADO · NOMBRE Y FIRMA', nombre: a.nombre }];
      if (testigos.length) testigos.forEach(function (t) { firmasC.push({ titulo: 'TESTIGO', nombre: t }); });
      else { firmasC.push({ titulo: 'TESTIGO' }); firmasC.push({ titulo: 'TESTIGO' }); }
      firmasC.push({ titulo: 'ELABORÓ · ADMINISTRATIVOS', nombre: 'Meditechnosys Group S.A. de C.V.' });
      cuerpo =
        '<p>EN LA CIUDAD DE ' + G.esc(lugar.toUpperCase()) + ', SIENDO LAS ' + G.esc(String(hora).toUpperCase()) +
        ' HORAS DEL DÍA ' + f.dia + ' DE ' + f.mes + ' DEL ' + f.anio + ', EN EL DOMICILIO DE LA EMPRESA DENOMINADA ' +
        EMPRESA + ', SITUADA EN ' + DOMICILIO + ':</p>' +
        '<p>SE LEVANTA PARA LOS EFECTOS LEGALES A QUE HAYA LUGAR LA PRESENTE ACTA ADMINISTRATIVA, EN FUNCIÓN DE LOS ' +
        'HECHOS QUE A CONTINUACIÓN SE DETALLAN:</p>' +
        '<div class="sec">Hechos</div>' + caja(a.hechos) +
        '<p>INCURRIDOS POR PARTE DE: <b>' + G.esc(a.nombre.toUpperCase()) + '</b>, QUIEN SE DESEMPEÑA COMO <b>' +
        G.esc((a.puesto || 'PUESTO NO ESPECIFICADO').toUpperCase()) + '</b>' +
        (a.usuario ? ', TITULAR DE LA CUENTA DE USUARIO <b>' + G.esc(a.usuario.toUpperCase()) + '</b> EN LA INTRANET' : '') + '.</p>' +
        (a.acuerdos ? '<div class="sec">Acuerdos y compromisos</div>' + caja(a.acuerdos) : '') +
        '<p>LA PRESENTE ACTA ADMINISTRATIVA SE LEVANTA CON LA FINALIDAD DE HACER CONSTAR LO OCURRIDO, PARA TODOS LOS ' +
        'EFECTOS CIVILES Y LEGALES A QUE HAYA LUGAR, Y PARA QUE LA EMPRESA SE RESERVE EL DERECHO DE EJERCITARLOS ' +
        'POSTERIORMENTE SI ASÍ LO ESTIMAN CONVENIENTE SUS DIRECTIVOS Y/O APODERADOS.</p>' +
        '<p>FIRMAN LA PRESENTE ACTA ADMINISTRATIVA LAS PERSONAS QUE INTERVINIERON EN ELLA Y QUE, DE MANERA LIBRE Y ' +
        'ESPONTÁNEA, DESEAN HACERLO, ASÍ COMO LOS TESTIGOS QUE PRESENCIARON LOS HECHOS Y LAS DECLARACIONES CONTENIDAS ' +
        'EN ELLA, QUIENES MANIFIESTAN BAJO PROTESTA DE DECIR LA VERDAD.</p>' +
        bloqueFirmas(firmasC);
    }

    return '' +
      '<div class="enc"><img src="' + logo + '" alt="Meditechnosys">' +
      '<div><div class="nom">Meditechnosys</div><div class="sub">Medical Technology Solutions</div></div>' +
      '<div class="dir">Av. Montevideo 498 Col. San Bartolo Atepehuacan<br>C.P. 07730 CDMX<br>Meditechnosys Group S.A. de C.V.</div></div>' +
      '<h1>' + titulo + '</h1>' +
      '<p class="folio">Folio ' + G.esc(a.folio) + ' · ' + subtitulo + '</p>' +
      cuerpo +
      '<div class="pie">' + EMPRESA_SC + ' · ' + DOMICILIO_SC + ' · Registro generado por ' +
      G.esc(a.creadoPor || '—') + ' el ' + G.fmtFecha(a.creado) + '</div>';
  }

  function htmlActa(a) {
    return '<!DOCTYPE html><html><head><meta charset="utf-8"><title>' + G.esc(a.folio) + '</title><style>' +
      estilosActa() + '</style></head><body>' + cuerpoActa(a) +
      '<scr' + 'ipt>window.onload = function () { setTimeout(function () { window.print(); }, 400); };</scr' + 'ipt>' +
      '</body></html>';
  }

  function imprimirActa(a) {
    ventanaImprimir(htmlActa(a));
  }

  // ======================================================================
  //  Credenciales
  // ======================================================================
  function cargarCred() {
    return G.api('/api/credenciales').then(function (l) { CRED = l || []; pintarCred(); });
  }

  function pintarCred() {
    var tb = $('cuerpoCred');
    $('cuentaCred').textContent = CRED.length ? CRED.length + ' credencial(es)' : '';
    if (!CRED.length) {
      tb.innerHTML = '<tr><td colspan="8" class="empty">Sin credenciales todavía.</td></tr>';
      return;
    }
    tb.innerHTML = CRED.map(function (c) {
      return '<tr>' +
        '<td><img class="thumb" src="/api/credenciales/' + c.id + '/foto" alt="" onerror="this.style.visibility=\'hidden\'"></td>' +
        '<td><strong>' + G.esc(c.nombre) + '</strong></td>' +
        '<td class="mono">' + G.esc(c.usuario) + '</td>' +
        '<td>' + G.esc(c.puesto) + '</td>' +
        '<td class="mono">' + G.esc(c.nss || '—') + '</td>' +
        '<td class="mono">' + G.esc(c.cedula || '—') + '</td>' +
        '<td>' + G.esc(c.vigencia) + '</td>' +
        '<td><div class="row-actions">' +
        '<button class="btn-secondary btn-mini" data-impcred="' + c.id + '">Imprimir</button>' +
        '<button class="btn-secondary btn-mini" data-prevcred="' + c.id + '">Vista previa</button>' +
        '<button class="btn-secondary btn-mini" data-png="' + c.id + '" title="Descarga una imagen con ambas caras">PNG (2 caras)</button>' +
        '<button class="btn-secondary btn-mini" data-editcred="' + c.id + '">Editar</button>' +
        '<button class="btn-danger btn-mini" data-delcred="' + c.id + '">Eliminar</button>' +
        '</div></td></tr>';
    }).join('');
  }

  function dialogoCred(c) {
    var dlg = document.createElement('dialog');
    dlg.className = 'modal ancho';
    var form = document.createElement('form');
    form.className = 'modal-form';
    form.noValidate = true;

    var h = document.createElement('h3');
    h.textContent = c ? 'Editar credencial' : 'Nueva credencial';
    form.appendChild(h);
    var intro = document.createElement('p');
    intro.className = 'hint'; intro.style.margin = '0';
    intro.textContent = 'La credencial queda ligada al usuario de intranet que elijas. La foto se recorta a tamaño infantil (2.5 × 3 cm): arrastra para encuadrar y usa el zoom.';
    form.appendChild(intro);

    var grid = document.createElement('div');
    grid.className = 'cred-grid';
    var colIzq = document.createElement('div');
    var colDer = document.createElement('div');
    grid.appendChild(colIzq); grid.appendChild(colDer);
    form.appendChild(grid);

    function campo(label, name, valor, opts) {
      opts = opts || {};
      var lab = document.createElement('label');
      lab.textContent = label + (opts.required ? ' (obligatorio)' : '');
      var inp = document.createElement('input');
      inp.type = opts.tipo || 'text';
      inp.name = name;
      if (valor != null) inp.value = valor;
      if (opts.maxlength) inp.maxLength = opts.maxlength;
      if (opts.placeholder) inp.placeholder = opts.placeholder;
      lab.appendChild(inp);
      colIzq.appendChild(lab);
      return inp;
    }
    var ops = USUARIOS.map(function (u) { return { v: u.usuario, t: etiquetaUsuario(u) }; });
    var labUsu = document.createElement('label');
    labUsu.textContent = c ? 'Usuario de intranet (no cambia al editar)' : 'Usuario de intranet (obligatorio)';
    var selUsu = document.createElement('select');
    selUsu.name = 'usuario';
    ops.forEach(function (o) {
      var op = document.createElement('option'); op.value = o.v; op.textContent = o.t; selUsu.appendChild(op);
    });
    if (c) { selUsu.value = c.usuario; selUsu.disabled = true; }
    labUsu.appendChild(selUsu);
    colIzq.appendChild(labUsu);
    var iNombre = campo('Nombre impreso', 'nombre', c ? c.nombre : '', { required: true, maxlength: 120 });
    var iPuesto = campo('Puesto impreso', 'puesto', c ? c.puesto : '', { required: true, maxlength: 80, placeholder: 'p. ej. Ingeniero de sitio' });
    var iNss = campo('NSS', 'nss', c ? c.nss : '', { maxlength: 20 });
    var iCedula = campo('Cédula', 'cedula', c ? c.cedula : '', { maxlength: 20 });
    var iVig = campo('Vigencia', 'vigencia', c ? (c.vigencia || '').replace(/^(\d{2})\/(\d{4})$/, '$2-$1') : '', { tipo: 'month', required: true });

    // Al elegir al usuario, autocompleta el nombre y el puesto impresos con los datos de su ficha
    // (capturados en Gestión de usuarios). Al editar el usuario va deshabilitado y no dispara nada.
    selUsu.addEventListener('change', function () {
      var u = datosUsuario(selUsu.value);
      if (!u) return;
      if (u.nombre) iNombre.value = u.nombre;
      if (u.cargo) iPuesto.value = u.cargo;
    });

    // Zona de foto con recortador.
    var zona = document.createElement('div');
    zona.className = 'zona-foto';
    zona.innerHTML = '<h4>Foto tamaño infantil</h4>';
    if (c) {
      zona.innerHTML += '<div class="foto-actual"><img src="/api/credenciales/' + c.id + '/foto" alt="" onerror="this.style.visibility=\'hidden\'"><span>Foto actual (se conserva si no subes otra)</span></div>';
    }
    var iFoto = document.createElement('input');
    iFoto.type = 'file'; iFoto.accept = 'image/png,image/jpeg';
    zona.appendChild(iFoto);
    var cv = document.createElement('canvas');
    cv.id = 'cropCanvas';
    zona.appendChild(cv);
    var barra = document.createElement('div');
    barra.className = 'crop-bar';
    barra.innerHTML = '<span>Zoom</span>';
    var zoom = document.createElement('input');
    zoom.type = 'range'; zoom.min = '1'; zoom.max = '3'; zoom.step = '0.05'; zoom.value = '1';
    barra.appendChild(zoom);
    zona.appendChild(barra);
    colDer.appendChild(zona);
    var rec = Recortador(cv);
    rec.dibujar();
    zoom.addEventListener('input', function () { rec.setZoom(Number(zoom.value)); });
    var fotoCambiada = false;
    iFoto.addEventListener('change', function () {
      var f = iFoto.files && iFoto.files[0];
      if (!f) return;
      if (!/image\/(png|jpeg)/.test(f.type)) { G.error(err, 'La foto debe ser JPEG o PNG.'); return; }
      if (f.size > 2 * 1024 * 1024) { G.error(err, 'La foto no puede pesar más de 2 MB.'); return; }
      var url = URL.createObjectURL(f);
      cargarImagen(url).then(function (img) {
        rec.setImagen(img);
        fotoCambiada = true;
        G.sinError(err);
      }).catch(function (e) { G.error(err, e.message); });
    });

    var err = document.createElement('p');
    err.className = 'form-error'; err.setAttribute('role', 'alert'); err.hidden = true;
    form.appendChild(err);
    var acc = document.createElement('div');
    acc.className = 'modal-actions';
    var bc = document.createElement('button');
    bc.type = 'button'; bc.className = 'btn-secondary'; bc.textContent = 'Cancelar';
    bc.addEventListener('click', function () { dlg.close(); });
    var bs = document.createElement('button');
    bs.type = 'submit'; bs.className = 'btn-primary'; bs.textContent = c ? 'Guardar cambios' : 'Crear credencial';
    acc.appendChild(bc); acc.appendChild(bs);
    form.appendChild(acc);

    dlg.appendChild(form);
    document.body.appendChild(dlg);

    form.addEventListener('submit', function (ev) {
      ev.preventDefault();
      G.sinError(err);
      var vig = String(iVig.value || '').replace(/^(\d{4})-(\d{2})$/, '$2/$1');
      if (!/^(0[1-9]|1[0-2])\/\d{4}$/.test(vig)) { G.error(err, 'Captura la vigencia (mes y año).'); return; }
      if (!c && !rec.tiene()) { G.error(err, 'Sube la foto tamaño infantil de la persona.'); return; }
      bs.disabled = true;

      var prom;
      if (!c) {
        var fd = new FormData();
        fd.append('usuario', selUsu.value);
        fd.append('nombre', iNombre.value.trim());
        fd.append('puesto', iPuesto.value.trim());
        fd.append('nss', iNss.value.trim());
        fd.append('cedula', iCedula.value.trim());
        fd.append('vigencia', vig);
        prom = new Promise(function (res, rej) {
          rec.salida().toBlob(function (b) {
            fd.append('foto', b, 'foto.jpg');
            res(G.subir('/api/credenciales', fd));
          }, 'image/jpeg', 0.92);
        });
      } else {
        prom = G.api('/api/credenciales/' + c.id, {
          method: 'PUT',
          body: JSON.stringify({
            nombre: iNombre.value.trim(), puesto: iPuesto.value.trim(),
            nss: iNss.value.trim(), cedula: iCedula.value.trim(), vigencia: vig
          })
        });
        if (fotoCambiada) {
          prom = prom.then(function () {
            return new Promise(function (res, rej) {
              rec.salida().toBlob(function (b) {
                var fd2 = new FormData();
                fd2.append('foto', b, 'foto.jpg');
                G.subir('/api/credenciales/' + c.id + '/foto', fd2).then(res, rej);
              }, 'image/jpeg', 0.92);
            });
          });
        }
      }
      prom.then(function () { dlg.close(); return cargarCred(); })
        .catch(function (e) { G.error(err, e.message); })
        .finally(function () { bs.disabled = false; });
    });
    dlg.addEventListener('close', function () { dlg.remove(); });
    dlg.showModal();
  }

  function vistaPrevia(cred) {
    return plantillas().then(function () { return fotoDe(cred); }).then(function (foto) {
      var frente = renderFrente(cred, foto).toDataURL('image/png');
      var reverso = renderReverso().toDataURL('image/png');
      var dlg = document.createElement('dialog');
      dlg.className = 'modal ancho';
      var cuerpo = document.createElement('div');
      cuerpo.className = 'modal-form';
      cuerpo.innerHTML = '<h3>Vista previa · ' + G.esc(cred.nombre) + '</h3>' +
        '<p class="hint" style="margin:0">Así sale en la hoja: frente y reverso lado a lado (54 × 86 mm por cara), para recortar y doblar por en medio.</p>' +
        '<div style="display:flex;gap:14px;justify-content:center;flex-wrap:wrap">' +
        '<img src="' + frente + '" alt="Frente" style="width:230px;border:1px solid var(--borde);border-radius:8px">' +
        '<img src="' + reverso + '" alt="Reverso" style="width:230px;border:1px solid var(--borde);border-radius:8px">' +
        '</div>' +
        '<div class="modal-actions"><button type="button" class="btn-secondary" id="pvCerrar">Cerrar</button>' +
        '<button type="button" class="btn-primary" id="pvImprimir">Imprimir</button></div>';
      dlg.appendChild(cuerpo);
      document.body.appendChild(dlg);
      cuerpo.querySelector('#pvCerrar').addEventListener('click', function () { dlg.close(); });
      cuerpo.querySelector('#pvImprimir').addEventListener('click', function () { dlg.close(); imprimirCred(cred); });
      dlg.addEventListener('close', function () { dlg.remove(); });
      dlg.showModal();
    });
  }

  // ======================================================================
  //  Arranque
  // ======================================================================
  function buscar(lista, id) { return lista.filter(function (x) { return x.id === id; })[0]; }

  function init() {
    return G.init().then(function () {
      document.getElementById('badgeUsuario').textContent = G.YO + ' · ' + G.nombreRol(G.ROL);
      puedeActas = G.tiene('actas.administrar');
      puedeCred = G.tiene('credenciales.administrar');
      $('tabActas').style.display = puedeActas ? '' : 'none';
      $('tabCred').style.display = puedeCred ? '' : 'none';
      $('btnNuevaActa').hidden = !puedeActas;
      $('btnNuevaCred').hidden = !puedeCred;

      $('tabActas').addEventListener('click', function () { irTab('actas'); });
      $('tabCred').addEventListener('click', function () { irTab('cred'); });
      irTab(location.hash === '#credenciales' && puedeCred ? 'cred' : 'actas');

      var cargas = [];
      if (puedeActas || puedeCred) cargas.push(G.api('/api/usuarios/asignables').then(function (u) { USUARIOS = u || []; }).catch(function () { USUARIOS = []; }));
      if (puedeActas) cargas.push(cargarActas());
      if (puedeCred) cargas.push(cargarCred());
      return Promise.all(cargas);
    });
  }

  // Acciones de actas (delegación de eventos).
  $('cuerpoActas').addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (!b) return;
    var id = Number(b.dataset.imprimir || b.dataset.editar || b.dataset.eliminar || 0);
    var a = buscar(ACTAS, id);
    if (!a) return;
    if (b.dataset.imprimir) imprimirActa(a);
    else if (b.dataset.editar) dialogoActa(a);
    else if (b.dataset.eliminar) {
      G.confirmar('¿Eliminar el acta ' + a.folio + ' de ' + a.nombre + '? Esta acción no se puede deshacer.', function () {
        G.api('/api/actas/' + id, { method: 'DELETE' }).then(cargarActas)
          .catch(function (er) { G.error($('errorActas'), er.message); });
      });
    }
  });
  $('btnNuevaActa').addEventListener('click', function () { dialogoActa(null); });

  // Acciones de credenciales.
  $('cuerpoCred').addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (!b) return;
    var id = Number(b.dataset.impcred || b.dataset.png || b.dataset.editcred || b.dataset.delcred || b.dataset.prevcred || 0);
    var c = buscar(CRED, id);
    if (!c) return;
    if (b.dataset.impcred) imprimirCred(c).catch(function (er) { G.error($('errorCred'), er.message); });
    else if (b.dataset.prevcred) vistaPrevia(c).catch(function (er) { G.error($('errorCred'), er.message); });
    else if (b.dataset.png) descargarPng(c).catch(function (er) { G.error($('errorCred'), er.message); });
    else if (b.dataset.editcred) dialogoCred(c);
    else if (b.dataset.delcred) {
      G.confirmar('¿Eliminar la credencial de ' + c.nombre + '? Se borra también su foto.', function () {
        G.api('/api/credenciales/' + id, { method: 'DELETE' }).then(cargarCred)
          .catch(function (er) { G.error($('errorCred'), er.message); });
      });
    }
  });
  $('btnNuevaCred').addEventListener('click', function () { dialogoCred(null); });

  init().catch(function (e) {
    G.error($('errorActas'), e.message || 'No se pudo cargar la página.');
  });
})();
