// Diálogo ⚙ de la cabecera (Pages/Shared/_PrefDialog.cshtml, incluido por las dos cabeceras Razor).
//
// Cuatro bloques, cada uno con su dueño:
//   · Tema (Oscuro · Claro)  → la cuenta, en el servidor (GET/PUT /api/tema). js/theme.js lo pinta
//     antes del primer fotograma leyendo la copia que viaja en la cookie `meditechnosys.tema`.
//   · Tamaño de la letra → este equipo (localStorage, lo aplica js/theme.js antes del pintado).
//   · Tiempo de sesión           → la cuenta, en el servidor (GET/PUT /api/sesion).
//   · Parámetros del módulo Gestión → solo si el rol trae `configuracion.administrar` (GET/PUT /api/config).
//
// Ningún valor se inventa: si una API no contesta, el bloque lo dice y ofrece Reintentar.
(function () {
  'use strict';

  var boton = document.getElementById('pref-btn');
  var caja = document.getElementById('pref-dialog');
  if (!boton || !caja) return;

  var Apariencia = window.MeditechnosysApariencia || null;
  var estado = document.getElementById('pref-estado');
  var grupoTema = document.getElementById('pref-tema');
  var grupoFuente = document.getElementById('pref-fuente');
  var grupoSesion = document.getElementById('pref-sesion');
  var bloqueSistema = document.getElementById('pref-sistema');
  var campoPeriodicidad = document.getElementById('pref-periodicidad');
  var campoFechaLimite = document.getElementById('pref-fechalimite');
  var botonGuardar = document.getElementById('pref-guardar');

  var CLAVE_ADMIN_CONFIG = 'configuracion.administrar';
  var CLAVE_FECHA_LIMITE = 'configuracion.fechaLimite';

  // ── utilidades propias del diálogo ────────────────────────────────────────────────
  function decir(mensaje, esError) {
    if (!estado) return;
    estado.textContent = mensaje || '';
    estado.hidden = !mensaje;
    estado.className = 'pref-estado' + (esError ? ' mal' : '');
  }

  function opciones(grupo) {
    return grupo ? [].slice.call(grupo.querySelectorAll('input')) : [];
  }

  function marcado(grupo) {
    var sel = opciones(grupo).filter(function (i) { return i.checked; })[0];
    return sel ? sel.value : null;
  }

  function marcar(grupo, valor) {
    opciones(grupo).forEach(function (i) { i.checked = String(i.value) === String(valor); });
  }

  function nota(texto) {
    var p = document.createElement('p');
    p.className = 'pref-nota';
    p.textContent = texto;
    return p;
  }

  function pide(url, opcionesPeticion) {
    var op = opcionesPeticion || {};
    var cabeceras = Object.assign({ Accept: 'application/json' }, op.headers || {});
    return fetch(url, Object.assign({}, op, { credentials: 'same-origin', headers: cabeceras }))
      .then(function (r) {
        if (r.status === 204) return null;
        if (!r.ok) {
          return r.json().catch(function () { return {}; }).then(function (c) {
            throw new Error((c && c.error) || 'el servidor contestó ' + r.status);
          });
        }
        var esTexto = (r.headers.get('content-type') || '').indexOf('json') === -1;
        return esTexto ? null : r.json();
      });
  }

  function conReintento(contenedor, mensaje, reintentar) {
    contenedor.textContent = '';
    contenedor.appendChild(nota(mensaje));
    var botonera = document.createElement('button');
    botonera.type = 'button';
    botonera.className = 'pref-reintentar';
    botonera.textContent = 'Reintentar';
    botonera.addEventListener('click', reintentar);
    contenedor.appendChild(botonera);
  }

  // ── abrir y cerrar ────────────────────────────────────────────────────────────────
  function abrir() {
    if (caja.open) return;
    caja.showModal();
    boton.setAttribute('aria-expanded', 'true');
    pintaApariencia();
    cargarTema();
    cargarSesion();
    cargarSistema();
  }

  function cerrar() { if (caja.open) caja.close(); }

  caja.addEventListener('close', function () { boton.setAttribute('aria-expanded', 'false'); });
  boton.addEventListener('click', function () { if (caja.open) cerrar(); else abrir(); });

  // Enlace directo al diálogo: `/?configuracion=1` (o `#configuracion`) lo abre solo, para que
  // cualquier pantalla pueda mandar a alguien a ajustar su tema, su letra o su sesión.
  var url = new URL(window.location.href);
  var loPiden = url.searchParams.get('configuracion') === '1' || window.location.hash === '#configuracion';
  if (loPiden) {
    abrir();
    url.searchParams.delete('configuracion');
    var limpio = url.pathname + url.search;
    try { window.history.replaceState(null, '', limpio); } catch (_) { /* navegación de prueba */ }
  }

  var x = document.getElementById('pref-cerrar');
  if (x) x.addEventListener('click', cerrar);
  // Un clic fuera de la caja (en el oscuro de detrás) la cierra, como en las ventanas del panel.
  caja.addEventListener('click', function (e) {
    var r = caja.getBoundingClientRect();
    var dentro = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
    if (!dentro) cerrar();
  });

  // ── tema (en la cuenta) ───────────────────────────────────────────────────────────
  // A diferencia del tiempo de sesión, estas dos opciones ya vienen escritas por el servidor
  // (`_PrefDialog.cshtml` las marca con el valor de la cuenta), así que el diálogo se ve correcto
  // aunque `/api/tema` no conteste: aquí la llamada solo confirma lo marcado y, si la copia que
  // pintó la pantalla estaba vieja —se cambió el tema desde otro equipo—, repinta esta.
  var ultimoTema = null;      // lo último que el servidor confirmó para esta cuenta
  var guardandoTema = false;  // hay un PUT en vuelo: la lectura que abras no debe pisar el clic

  function etiquetaTema(valor) {
    var sel = opciones(grupoTema).filter(function (i) { return i.value === valor; })[0];
    var texto = sel && sel.parentNode ? (sel.parentNode.textContent || '') : '';
    return String(texto).replace(/\s+/g, ' ').trim() || valor;
  }

  function pintaTema(tema) {
    if (!grupoTema || !tema) return;
    marcar(grupoTema, tema);
    ultimoTema = tema;
  }

  function cargarTema() {
    if (!grupoTema) return;
    grupoTema.setAttribute('aria-busy', 'true');
    pide('/api/tema').then(function (datos) {
      // Si la persona ya marcó una opción mientras esto llegaba, manda su elección: esta respuesta
      // trae el valor ANTERIOR y reescribirla en pantalla desharía el clic que acaba de hacer.
      if (guardandoTema) return;
      grupoTema.removeAttribute('aria-busy');
      var guardado = (datos && datos.tema) || null;
      if (!guardado) throw new Error('no devolvió tu tema');
      var lista = (datos && datos.opciones) || [];
      if (lista.length) {
        // Se rehace la lista con las etiquetas del servidor, para que el bloque no se desvíe si un
        // día hay más de dos temas.
        grupoTema.textContent = '';
        lista.forEach(function (o) {
          var label = document.createElement('label');
          var radio = document.createElement('input');
          radio.type = 'radio';
          radio.name = 'pref-tema';
          radio.value = o.valor;
          radio.checked = o.valor === guardado;
          var texto = document.createElement('span');
          texto.textContent = o.etiqueta;
          label.appendChild(radio);
          label.appendChild(texto);
          grupoTema.appendChild(label);
        });
      }
      pintaTema(guardado);
      if (Apariencia && Apariencia.tema() !== guardado) Apariencia.cambiarTema(guardado);
    }).catch(function () {
      if (guardandoTema) return;
      grupoTema.removeAttribute('aria-busy');
      // Se queda lo marcado por la página: también es el valor de la cuenta, escrito en el servidor.
      ultimoTema = marcado(grupoTema);
    });
  }

  if (grupoTema) grupoTema.addEventListener('change', function () {
    var elegido = marcado(grupoTema);
    if (!elegido) return;
    if (Apariencia) Apariencia.cambiarTema(elegido);   // se nota en el acto, sin recargar
    decir('Guardando tu tema…');
    grupoTema.setAttribute('aria-busy', 'true');
    guardandoTema = true;                              // mientras esto dure, ninguna lectura pisa el clic
    pide('/api/tema', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tema: elegido })
    }).then(function () {
      guardandoTema = false;
      grupoTema.removeAttribute('aria-busy');
      ultimoTema = elegido;
      decir('Listo: tu pantalla se abre en ' + etiquetaTema(elegido) + ', en el equipo que abras.');
    }).catch(function (e) {
      guardandoTema = false;
      grupoTema.removeAttribute('aria-busy');
      // Se regresa a lo que el servidor tenga, para que el diálogo no mienta. Si la lectura también
      // falla, se vuelve al último valor confirmado.
      pide('/api/tema').then(function (datos) {
        var guardado = (datos && datos.tema) || ultimoTema || 'dark';
        if (Apariencia) Apariencia.cambiarTema(guardado);
        pintaTema(guardado);
        decir('No se pudo guardar tu tema: ' + e.message + '.', true);
      }).catch(function () {
        if (Apariencia && ultimoTema) Apariencia.cambiarTema(ultimoTema);
        pintaTema(ultimoTema);
        decir('No se pudo guardar tu tema: ' + e.message + '.', true);
      });
    });
  });

  // ── letra (en este equipo) ────────────────────────────────────────────────────────
  function pintaApariencia() {
    if (!Apariencia) return;
    marcar(grupoFuente, Apariencia.fuente());
    marcar(grupoTema, Apariencia.tema());
  }

  if (grupoFuente) grupoFuente.addEventListener('change', function () {
    if (!Apariencia) return;
    Apariencia.cambiarFuente(marcado(grupoFuente));
    pintaApariencia();
    decir('');
  });

  // Si otra pestaña cambia la preferencia, el diálogo la refleja mientras esté abierto.
  window.addEventListener('storage', pintaApariencia);

  // ── tiempo de sesión (en la cuenta) ───────────────────────────────────────────────
  var etiquetas = {};      // minutos → cómo se llama en pantalla
  var sesionLista = false;

  function cargarSesion() {
    if (!grupoSesion || sesionLista) return;
    grupoSesion.setAttribute('aria-busy', 'true');
    pide('/api/sesion').then(function (datos) {
      grupoSesion.textContent = '';
      etiquetas = {};
      var lista = (datos && datos.opciones) || [];
      lista.forEach(function (o) {
        var minutos = Number(o.minutos);
        if (!minutos) return;
        etiquetas[String(minutos)] = o.etiqueta;
        var label = document.createElement('label');
        var radio = document.createElement('input');
        radio.type = 'radio';
        radio.name = 'pref-sesion';
        radio.value = String(minutos);
        radio.checked = minutos === Number(datos.minutos);
        var texto = document.createElement('span');
        texto.textContent = o.etiqueta;
        label.appendChild(radio);
        label.appendChild(texto);
        grupoSesion.appendChild(label);
      });
      if (!lista.length) throw new Error('no devolvió opciones de tiempo de sesión');
      sesionLista = true;
      grupoSesion.removeAttribute('aria-busy');
    }).catch(function (e) {
      grupoSesion.removeAttribute('aria-busy');
      conReintento(grupoSesion, 'No se pudo leer tu tiempo de sesión: ' + e.message + '.', function () {
        grupoSesion.textContent = '';
        cargarSesion();
      });
    });
  }

  if (grupoSesion) grupoSesion.addEventListener('change', function () {
    var minutos = Number(marcado(grupoSesion));
    if (!minutos) return;
    decir('Guardando tu tiempo de sesión…');
    grupoSesion.setAttribute('aria-busy', 'true');
    pide('/api/sesion', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ minutos: minutos })
    }).then(function () {
      grupoSesion.removeAttribute('aria-busy');
      decir('Listo: tu sesión ahora aguanta ' + (etiquetas[String(minutos)] || minutos + ' minutos') +
        ' sin actividad.');
    }).catch(function (e) {
      grupoSesion.removeAttribute('aria-busy');
      // Se regresa a lo que estaba guardado, para que el diálogo no mienta.
      pide('/api/sesion').then(function (datos) {
        marcar(grupoSesion, datos ? datos.minutos : null);
        decir('No se pudo guardar: ' + e.message + '.', true);
      }).catch(function () {
        decir('No se pudo guardar: ' + e.message + '.', true);
      });
    });
  });

  // ── parámetros del módulo Gestión (solo Administración) ───────────────────────────
  var sistemaIntentado = false;
  var puedeFechaLimite = false;

  function cargarSistema() {
    if (!bloqueSistema || sistemaIntentado) return;
    sistemaIntentado = true;
    pide('/api/permisos/mios').then(function (mios) {
      var tener = (mios && mios.permisos) || [];
      if (tener.indexOf(CLAVE_ADMIN_CONFIG) === -1) return null;   // el bloque se queda oculto
      return pide('/api/config').then(function (cfg) {
        if (!cfg) return;
        puedeFechaLimite = tener.indexOf(CLAVE_FECHA_LIMITE) !== -1;
        if (campoPeriodicidad) campoPeriodicidad.value = cfg['reportes.periodicidadDias'] || '7';
        if (campoFechaLimite) {
          campoFechaLimite.checked = cfg['items.fechaLimiteHabilitada'] !== 'false';
          campoFechaLimite.disabled = !puedeFechaLimite;
        }
        bloqueSistema.hidden = false;
      });
    }).catch(function (e) {
      if (!bloqueSistema.hidden) decir('No se pudieron cargar los parámetros del módulo: ' + e.message + '.', true);
    });
  }

  if (botonGuardar) botonGuardar.addEventListener('click', function () {
    var valores = {};
    var dias = Number(campoPeriodicidad ? campoPeriodicidad.value : NaN);
    if (!dias || dias < 1 || dias > 365) {
      decir('La periodicidad debe ser un número de días entre 1 y 365.', true);
      return;
    }
    valores['reportes.periodicidadDias'] = String(dias);
    if (campoFechaLimite && puedeFechaLimite) valores['items.fechaLimiteHabilitada'] = campoFechaLimite.checked ? 'true' : 'false';
    botonGuardar.disabled = true;
    decir('Guardando los parámetros…');
    pide('/api/config', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ valores: valores })
    }).then(function () {
      botonGuardar.disabled = false;
      decir('Parámetros del módulo guardados.');
    }).catch(function (e) {
      botonGuardar.disabled = false;
      decir('No se pudieron guardar los parámetros: ' + e.message + '.', true);
    });
  });
})();
