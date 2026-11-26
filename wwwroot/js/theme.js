// Preferencias de apariencia, aplicadas en el <head> antes del primer pintado para que la pantalla
// no dé un salto.
//
//   · TEMA (Oscuro · Claro) → es de **la cuenta**, no de este equipo. Lo guarda el servidor en
//     `tema.<usuario>` (GET/PUT /api/tema, desde el bloque «Tema» del botón ⚙) y lo trae al navegador
//     en la cookie `meditechnosys.tema`, que es solo una copia para poder pintar sin esperar una
//     llamada: las pantallas autónomas (`/dicom/`, `/gestion/`…) son archivos estáticos. Sin cookie,
//     o con un valor que no se reconoce, la pantalla abre en **Oscuro**.
//   · TAMAÑO DE LA LETRA → sigue siendo de **este equipo** (`localStorage['meditechnosys.fuente']`),
//     porque es cuestión del monitor y la vista de quien está sentado, no de la cuenta.
//
// El interruptor Día/Noche de la cabecera ya no existe: se retiró junto con `localStorage` del tema.
// Si el navegador bloquea las cookies o el almacenamiento, todo sigue funcionando —se ve en Oscuro y
// la letra queda normal—; nada de esto depende de escribir para poder leer.
(function () {
  'use strict';
  var OSCURO = 'dark';
  var CLARO = 'light';
  var CLAVE_COOKIE = 'meditechnosys.tema';
  var CLAVE_FUENTE = 'meditechnosys.fuente';
  var CLAVE_TEMA_VIEJA = 'meditechnosys.theme';   // la que escribía el interruptor; ya no manda
  var raiz = document.documentElement;

  // Acepta los valores internos y los nombres de pantalla («oscuro», «claro», «noche»); devuelve
  // null para cualquier basura, igual que hace el servidor con `TemaPreferencias.Normalizar`.
  function esTema(v) {
    v = (v == null ? '' : String(v)).trim().toLowerCase();
    if (v === OSCURO || v === 'oscuro' || v === 'noche') return OSCURO;
    if (v === CLARO || v === 'claro' || v === 'dia' || v === 'día') return CLARO;
    return null;
  }
  function esFuente(v) { return v === 'chica' || v === 'grande' ? v : 'normal'; }

  function leerCookie() {
    try {
      var encontrado = document.cookie.match(/(?:^|;\s*)meditechnosys\.tema=([^;]*)/);
      return esTema(encontrado ? decodeURIComponent(encontrado[1]) : '');
    } catch (_) { return null; }
  }

  // La cookie es la copia autorizada mientras el navegador la tenga; si falta, Oscuro.
  function temaActual() { return leerCookie() || OSCURO; }

  function escribeCookie(tema) {
    try {
      // Mismos atributos que pone el servidor (Path=/), para que no queden dos cookies distintas.
      document.cookie = CLAVE_COOKIE + '=' + tema + '; Path=/; SameSite=Lax; Max-Age=31536000';
    } catch (_) { /* sin cookies: la preferencia dura lo que dure la visita */ }
  }

  function leerFuente() {
    try { return esFuente(window.localStorage.getItem(CLAVE_FUENTE)); } catch (_) { return 'normal'; }
  }
  function guardarFuente(valor) {
    try { window.localStorage.setItem(CLAVE_FUENTE, valor); } catch (_) { /* sin almacenamiento */ }
  }

  function aplicaTema(tema) {
    tema = esTema(tema) || OSCURO;
    raiz.setAttribute('data-theme', tema);
    window.dispatchEvent(new CustomEvent('meditechnosys:themechange', { detail: { theme: tema } }));
  }

  // El tamaño de fuente se aplica sobre el `root`: todo lo que esté en rem (la intranet y los
  // paneles) crece o se encoge junto, así que no hace falta retocar hoja por hoja.
  function aplicaFuente(fuente) {
    raiz.setAttribute('data-fuente', esFuente(fuente));
  }

  // Se limpia el recuerdo del interruptor: si alguien entra con `meditechnosys.theme = light` de
  // cuando el tema era de este equipo, la cuenta manda y no debe quedar un valor muerto que alguien
  // interprete como preferencia vigente.
  try { window.localStorage.removeItem(CLAVE_TEMA_VIEJA); } catch (_) { /* sin almacenamiento */ }

  aplicaTema(temaActual());
  aplicaFuente(leerFuente());

  // API mínima para el diálogo ⚙ (js/preferencias.js). Sin ella la página se ve igual que antes:
  // en Oscuro. `cambiarTema` pinta ya mismo y deja la copia en la cookie; a la cuenta lo que lo
  // escribe es el `PUT /api/tema` que manda el diálogo, y su respuesta reescribe esa misma cookie.
  window.MeditechnosysApariencia = {
    tema: function () { return temaActual(); },
    cambiarTema: function (v) {
      var tema = esTema(v) || OSCURO;
      escribeCookie(tema);
      aplicaTema(tema);
      return tema;
    },
    fuente: function () { return esFuente(raiz.getAttribute('data-fuente')); },
    cambiarFuente: function (v) { guardarFuente(esFuente(v)); aplicaFuente(v); return this.fuente(); }
  };

  // La letra sigue compartida entre pestañas del mismo equipo (es preferencia de equipo); el tema
  // ya no viaja por ahí porque ahora vive en la cuenta y se lee de la cookie al cargar.
  window.addEventListener('storage', function (event) {
    try { if (event.storageArea !== window.localStorage) return; } catch (_) { return; }
    if (event.key === CLAVE_FUENTE || event.key === null) aplicaFuente(leerFuente());
  });

  // Al volver por el historial (bfcache) la cookie puede haber cambiado mientras se navegaba en
  // otra pantalla, así que el tema se vuelve a leer de ella antes de seguir mostrando la página.
  window.addEventListener('pageshow', function (event) {
    if (!event.persisted) return;
    aplicaTema(temaActual());
    aplicaFuente(leerFuente());
  });
})();
