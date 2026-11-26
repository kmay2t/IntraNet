/*
 * ubicacion.js — el propio equipo avisa en qué punto del mapa está.
 *
 * Para qué sirve: la bitácora de accesos sabe la IP interna (10.252.x.x) y el nombre del aparato,
 * pero de ahí no se puede deducir la dirección física: las direcciones privadas no aparecen en ningún
 * mapa. Este archivo hace que sea el navegador del mismo aparato el que conteste: pregunta el permiso
 * de ubicación, recibe el punto real —el GPS o las redes wifi que ve ese puesto— y lo manda a
 * /api/ubicacion para que la intranet lo traduzca a dirección postal completa («Ejido 26, Col. San
 * Bartolo Atepehuacan, C.P. 07730, Gustavo A. Madero»). Nadie escribe ninguna dirección a mano.
 *
 * Reglas de esta hoja:
 *  - Nunca interrumpe la página: si algo falla (sin permiso, sin señal, sin sesión) se queda callada
 *    y ninguna pantalla de la intranet depende de su resultado.
 *  - El permiso se pide una sola vez por aparato: el navegador lo recuerda y de ahí en adelante las
 *    lecturas salen solas, sin ventanetas.
 *  - Si el permiso sigue pendiente, muestra un aviso chiquito de un solo clic («Permitir ubicación»)
 *    que desaparece en cuanto se concede o se marca «Más tarde». No pide ni escribe direcciones.
 *  - El navegador solo entrega la posición real en una dirección segura (https). En http:// ese
 *    permiso ni siquiera se puede pedir, así que ahí se reporta la aproximación por IP —zona, no
 *    edificio— y no se molesta a nadie con permisos.
 *  - Cuándo se vuelve a medir: la posición real cada 30 minutos (no cuesta nada); la aproximación por
 *    IP cada 6 horas, para no gastar la cuota de los servicios gratuitos.
 *  - No manda nombres de pacientes ni datos clínicos: solo coordenadas y su margen en metros.
 */
(function () {
  'use strict';

  var CLAVE = 'meditechnosys.ubicacion';
  var MINUTOS_GPS = 30;              // posición real del aparato: se refresca seguido
  var HORAS_IP = 6;                  // aproximación por IP: respeta la cuota del servicio
  var HORAS_AVISO = 24;              // cuánto se respeta un «más tarde»
  var ESPERA_GPS = 12000;            // ms que se le dan al aparato para contestar

  function puedeNavegador() {
    // En http:// el navegador bloquea la posición real por regla de seguridad.
    return !!window.isSecureContext && !!navigator.geolocation;
  }

  // ---- Lo que ya se midió en este aparato ----
  function leeEstado() {
    try {
      var e = JSON.parse(localStorage.getItem(CLAVE) || '{}');
      return (e && typeof e === 'object') ? e : {};
    } catch (err) { return {}; }
  }

  function marcaMedido(campo) {
    try {
      var e = leeEstado();
      e[campo] = new Date().toISOString();
      localStorage.setItem(CLAVE, JSON.stringify(e));
    } catch (err) { /* sin almacenamiento: se intenta en la próxima visita */ }
  }

  function marcaAviso(texto) {
    try { localStorage.setItem(CLAVE + '.aviso', texto); } catch (err) { /* nada */ }
  }

  function horaDe(iso) {
    if (!iso) return null;
    var t = Date.parse(iso);
    return isNaN(t) ? null : t;
  }

  // ¿Pasó tiempo suficiente para volver a medir? (un reloj corrido hacia atrás también mide)
  function toca(campo, minutos) {
    var t = horaDe(leeEstado()[campo]);
    if (t === null) return true;
    var transcurrido = (Date.now() - t) / 6e4;
    return transcurrido >= minutos || transcurrido < 0;
  }

  // ---- Los servicios que dicen desde dónde salió este equipo a Internet ----
  var PROVEEDORES = [
    {
      url: 'https://ipapi.co/json/',
      lee: function (d) {
        return { lat: num(d.latitude), lon: num(d.longitude), texto: une([d.city, d.region, d.postal]) };
      }
    },
    {
      url: 'https://ipwho.is/',
      lee: function (d) {
        if (d.success === false) return null;
        return { lat: num(d.latitude), lon: num(d.longitude), texto: une([d.city, d.region, d.postal]) };
      }
    },
    {
      // El de respaldo: casi siempre solo coordenadas, sin colonia ni código postal.
      url: 'https://freeipapi.com/api/json',
      lee: function (d) {
        return { lat: num(d.latitude), lon: num(d.longitude), texto: une([d.cityName, d.regionName, d.zipCode]) };
      }
    }
  ];

  function num(v) {
    var n = typeof v === 'string' ? parseFloat(v) : v;
    return isFinite(n) ? n : null;
  }

  function une(partes) {
    var vistas = {};
    var salida = [];
    (partes || []).forEach(function (p) {
      var s = String(p || '').trim();
      if (!s) return;
      var k = s.toLowerCase();
      if (vistas[k]) return;
      vistas[k] = true;
      salida.push(s);
    });
    return salida.join(', ');
  }

  function post(data) {
    return fetch('/api/ubicacion', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        latitud: data.lat,
        longitud: data.lon,
        precision: data.precision || 0,
        direccion: data.texto || '',
        fuente: data.fuente || 'ip'
      })
    }).then(function (r) { return r.ok; }, function () { return false; });
  }

  // Intenta los servicios uno tras otro hasta que alguno dé coordenadas válidas.
  function porIP(i) {
    if (i >= PROVEEDORES.length) return Promise.resolve(null);
    var p = PROVEEDORES[i];
    return fetch(p.url, { credentials: 'omit' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        var leido = d ? p.lee(d) : null;
        if (!leido || leido.lat === null || leido.lon === null) return porIP(i + 1);
        return { lat: leido.lat, lon: leido.lon, texto: leido.texto, precision: 0, fuente: 'ip' };
      })
      .catch(function () { return porIP(i + 1); });   // sin salida a Internet: así queda
  }

  // La posición real del aparato. Aquí es donde el navegador muestra su ventaneta de permiso: la
  // primera vez que se pregunta; después el permiso queda acordado y esto responde solo.
  function porNavegador() {
    if (!puedeNavegador()) return Promise.resolve(null);
    return new Promise(function (res) {
      var contestado = false;
      function ya(v) { if (!contestado) { contestado = true; res(v); } }
      navigator.geolocation.getCurrentPosition(function (pos) {
        ya({
          lat: pos.coords.latitude, lon: pos.coords.longitude,
          precision: Math.round(pos.coords.accuracy || 0), texto: '', fuente: 'navegador'
        });
      }, function () {
        ya(null);       // negado, sin señal o sin tiempo: se queda callado
      }, { enableHighAccuracy: true, timeout: ESPERA_GPS, maximumAge: 5 * 60000 });
    });
  }

  function estadoPermiso() {
    if (!puedeNavegador()) return Promise.resolve('no-se-puede');
    if (!navigator.permissions || !navigator.permissions.query) return Promise.resolve('desconocido');
    return navigator.permissions.query({ name: 'geolocation' })
      .then(function (p) { return p.state; }, function () { return 'desconocido'; });
  }

  // ---- Las dos lecturas, con su propia frecuencia ----
  function intentaPosicionReal(forzar) {
    if (!puedeNavegador()) return Promise.resolve(false);
    if (!forzar && !toca('nav', MINUTOS_GPS)) return Promise.resolve(false);
    return porNavegador().then(function (dato) {
      if (!dato) return false;
      return post(dato).then(function (ok) {
        if (ok) { marcaMedido('nav'); quitarAviso(); }
        return ok;
      });
    });
  }

  function intentaPorIP() {
    if (!toca('ip', HORAS_IP * 60)) return Promise.resolve(false);
    return porIP(0).then(function (dato) {
      if (!dato) return false;
      return post(dato).then(function (ok) { if (ok) marcaMedido('ip'); return ok; });
    });
  }

  // ---- El aviso de un solo clic, solo mientras el permiso siga pendiente ----
  var _aviso = null;

  function quitarAviso() {
    if (_aviso && _aviso.parentNode) _aviso.parentNode.removeChild(_aviso);
    _aviso = null;
  }

  function avisoRespetado() {
    try {
      var t = horaDe(localStorage.getItem(CLAVE + '.aviso') || '');
      if (t === null) return false;
      return (Date.now() - t) / 36e5 < HORAS_AVISO;
    } catch (err) { return false; }
  }

  function muestraAviso() {
    if (_aviso || !puedeNavegador() || avisoRespetado()) return;
    var caja = document.createElement('div');
    caja.setAttribute('role', 'note');
    caja.style.cssText = 'position:fixed;left:14px;bottom:14px;z-index:2147483000;max-width:330px;' +
      'padding:12px 14px;border-radius:10px;border:1px solid #cbd5e1;background:#fff;' +
      'box-shadow:0 6px 18px rgba(15,23,42,.18);font:13px/1.45 "Segoe UI",system-ui,sans-serif;color:#0f172a;';

    var titulo = document.createElement('strong');
    titulo.textContent = 'Ubicación para la bitácora de accesos';
    titulo.style.cssText = 'display:block;font-size:.86rem;margin-bottom:4px;';
    caja.appendChild(titulo);

    var texto = document.createElement('span');
    texto.textContent = 'Permite que este aparato diga en qué punto está. Es una sola vez por equipo; ' +
      'después la lectura sale sola y no se vuelve a preguntar. No se guarda ningún otro dato.';
    caja.appendChild(texto);

    var botones = document.createElement('div');
    botones.style.cssText = 'display:flex;gap:8px;margin-top:10px;';

    var dar = document.createElement('button');
    dar.type = 'button';
    dar.textContent = 'Permitir ubicación';
    dar.style.cssText = 'padding:6px 12px;border:0;border-radius:7px;background:#0d6efd;color:#fff;' +
      'font:600 13px "Segoe UI",system-ui,sans-serif;cursor:pointer;';
    dar.addEventListener('click', function () {
      dar.disabled = true; dar.textContent = 'Preguntando…';
      // El clic es el gesto que el navegador quiere para mostrar su ventaneta de permiso.
      intentaPosicionReal(true).then(function (ok) {
        if (ok) { quitarAviso(); return; }
        dar.disabled = false; dar.textContent = 'Permitir ubicación';
        // Siguió pendiente o lo negó: ya no se insiste durante el día.
        marcaAviso(new Date().toISOString());
        quitarAviso();
      });
    });

    var tarde = document.createElement('button');
    tarde.type = 'button';
    tarde.textContent = 'Más tarde';
    tarde.style.cssText = 'padding:6px 12px;border:1px solid #cbd5e1;border-radius:7px;background:#fff;' +
      'color:#334155;font:13px "Segoe UI",system-ui,sans-serif;cursor:pointer;';
    tarde.addEventListener('click', function () {
      marcaAviso(new Date().toISOString());
      quitarAviso();
    });

    botones.appendChild(dar); botones.appendChild(tarde);
    caja.appendChild(botones);
    document.body.appendChild(caja);
    _aviso = caja;
  }

  // ---- Arranque: primero la posición real, y si no, la aproximación por IP ----
  function arrancar() {
    conSesion(function () {
      intentaPosicionReal(false)
        .then(function (ok) { return ok ? true : intentaPorIP(); })
        .then(function () {
          // Queda pendiente el permiso: se ofrece el aviso de un clic una sola vez al día.
          return estadoPermiso().then(function (estado) {
            if (estado === 'prompt' || estado === 'desconocido') muestraAviso();
          });
        })
        .catch(function () { /* nunca rompe la página */ });
    });
  }

  // Solo tiene sentido si hay sesión abierta: sin ella el reporte no a quién pertenecería.
  function conSesion(cb) {
    fetch('/api/patologia/me', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (me) { if (me && me.usuario) cb(); })
      .catch(function () { /* nada */ });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', arrancar);
  } else {
    arrancar();
  }
})();
