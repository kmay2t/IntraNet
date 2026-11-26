/* Navegación común de Reportes. Cada pestaña conserva su URL y su guard del servidor:
   no se incrustan páginas ni se mezclan sus identificadores, filtros o ventanas.
   Solo se consultan datos del apartado abierto. Los permisos son los de la sesión,
   nunca se deducen del nombre del rol ni se guardan en almacenamiento del navegador. */
(function () {
  'use strict';
  var nav = document.querySelector('[data-reportes-activo]');
  if (!nav) return;
  var activo = nav.getAttribute('data-reportes-activo');
  var apartados = [
    { id: 'bitacora', texto: 'Bitácora', url: '/admin/bitacora/index.html', permisos: ['bitacora.ver', 'quemador.ver'] },
    { id: 'historico', texto: 'Histórico de actividades', url: '/gestion/historico/index.html', permisos: ['historico.actividades'] },
    { id: 'estatus', texto: 'Reportes de estatus', url: '/admin/reportes/index.html', permisos: ['reportes.consultar'] }
  ];
  var aviso = document.createElement('p');
  aviso.className = 'reportes-tabs-aviso';
  aviso.setAttribute('role', 'status');
  aviso.hidden = true;
  nav.parentNode.insertBefore(aviso, nav.nextSibling);

  // Son enlaces de navegación entre documentos (aria-current), no paneles ocultos con role=tab.
  // Así funcionan Tab, Enter, atrás/adelante y abrir en otra pestaña de forma nativa.
  nav.addEventListener('click', function (ev) {
    var enlace = ev.target.closest('a[aria-current="page"]');
    if (enlace && !ev.ctrlKey && !ev.metaKey && !ev.shiftKey && !ev.altKey) ev.preventDefault();
  });

  function pintar(permisos) {
    nav.textContent = '';
    apartados.forEach(function (apartado) {
      if (!apartado.permisos.some(function (clave) { return permisos.indexOf(clave) !== -1; })) return;
      var enlace = document.createElement('a');
      enlace.className = 'reportes-tab';
      enlace.href = apartado.url;
      enlace.textContent = apartado.texto;
      if (apartado.id === activo) enlace.setAttribute('aria-current', 'page');
      nav.appendChild(enlace);
    });
  }

  function cargar() {
    aviso.hidden = true;
    nav.setAttribute('aria-busy', 'true');
    fetch('/api/permisos/mios', { credentials: 'same-origin', headers: { Accept: 'application/json' }, cache: 'no-store' })
      .then(function (r) {
        if (!r.ok) throw new Error('permisos');
        return r.json();
      })
      .then(function (datos) {
        if (!datos || !Array.isArray(datos.permisos)) throw new Error('permisos');
        pintar(datos.permisos);
      })
      .catch(function () {
        // Ante un fallo no se muestran enlaces cuya autorización no pudimos comprobar.
        // La pestaña actual ya pasó el guard del servidor y sigue funcionando.
        aviso.textContent = 'No se pudieron cargar los otros apartados. ';
        var reintentar = document.createElement('button');
        reintentar.type = 'button';
        reintentar.textContent = 'Reintentar';
        reintentar.addEventListener('click', cargar);
        aviso.appendChild(reintentar);
        aviso.hidden = false;
      })
      .finally(function () { nav.removeAttribute('aria-busy'); });
  }
  cargar();
})();
