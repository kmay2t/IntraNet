/* Pantalla compartida de los inventarios del módulo Gestión (equipos, IPs y AETitle).
   Cada inventario tiene su propia página (no se mezclan en una sola pantalla); la página declara
   su colección en `window.INVENTARIO` y aquí se genera la consulta, el filtro por hospital, la
   búsqueda, la tabla y el alta/edición/baja a partir del esquema que publica la API.
   Ampliar un inventario = agregar campos en Models/Inventario.cs: la interfaz se adapta sola. */
window.Inventario = (function () {
  'use strict';

  // Regla de búsqueda compartida (/js/filtro.js): compara sin acentos ni mayúsculas.
  var Busca = window.Filtro || { norm: function (x) { return String(x == null ? '' : x).toLowerCase(); } };

  function iniciar() {
    var coleccion = window.INVENTARIO;
    var G = window.Gestion;
    var esquema = null;
    var todo = [];

    var titulo = document.getElementById('tituloPagina');
    var cuerpo = document.getElementById('cuerpo');
    var encabezado = document.getElementById('encabezado');
    var error = document.getElementById('error');
    var vacio = document.getElementById('vacio');
    var contador = document.getElementById('contador');
    var fTexto = document.getElementById('fTexto');
    var fHospital = document.getElementById('fHospital');
    var btnExcel = document.getElementById('btnExcel');
    var btnNuevo = document.getElementById('btnNuevo');

    function puedeAdministrar() { return G.tiene('inventario.administrar'); }

    function llenarSelect(sel, valores) {
      valores.forEach(function (v) {
        var o = document.createElement('option');
        o.value = v.v !== undefined ? v.v : v;
        o.textContent = v.t !== undefined ? v.t : v;
        sel.appendChild(o);
      });
    }

    function camposVisibles() { return esquema.campos; }

    function pintarEncabezado() {
      encabezado.innerHTML = '';
      function th(texto) { var e = document.createElement('th'); e.textContent = texto; encabezado.appendChild(e); }
      th('Hospital');
      camposVisibles().forEach(function (c) { th(c.etiqueta); });
      th('Registrado por');
      th('Alta');
      if (puedeAdministrar()) th('Acciones');
    }

    function pintarTabla(lista) {
      cuerpo.innerHTML = '';
      vacio.hidden = lista.length > 0;
      contador.textContent = lista.length + ' registro(s)';
      lista.forEach(function (it) {
        var tr = document.createElement('tr');
        tr.dataset.id = it.id;
        function td(texto) { var e = document.createElement('td'); e.textContent = texto || '—'; tr.appendChild(e); }
        td(it.hospital);
        camposVisibles().forEach(function (c) {
          var v = (it.datos && it.datos[c.clave]) || '';
          if (c.tipo === 'area' && v.length > 60) v = v.slice(0, 60) + '…';
          td(v);
        });
        td(it.creadoPor);
        td(G.fmtFecha(it.creado));
        if (puedeAdministrar()) {
          var acc = document.createElement('td');
          acc.className = 'col-acciones';
          var bEdit = document.createElement('button');
          bEdit.type = 'button'; bEdit.className = 'btn-secondary'; bEdit.textContent = 'Editar';
          bEdit.addEventListener('click', function (ev) { ev.stopPropagation(); abrirFormulario(it); });
          var bDel = document.createElement('button');
          bDel.type = 'button'; bDel.className = 'btn-danger'; bDel.textContent = 'Eliminar';
          bDel.addEventListener('click', function (ev) { ev.stopPropagation(); eliminar(it); });
          acc.appendChild(bEdit); acc.appendChild(bDel);
          tr.appendChild(acc);
        }
        cuerpo.appendChild(tr);
      });
    }

    function filtrar() {
      var t = Busca.norm(fTexto.value.trim());
      var h = fHospital.value;
      return todo.filter(function (it) {
        if (h && it.hospital !== h) return false;
        if (!t) return true;
        if (Busca.norm(it.hospital).indexOf(t) >= 0) return true;
        return Object.keys(it.datos || {}).some(function (k) {
          return Busca.norm(it.datos[k]).indexOf(t) >= 0;
        });
      });
    }

    function repintar() { pintarTabla(filtrar()); }

    function query() {
      var p = [];
      if (fHospital.value) p.push('hospital=' + encodeURIComponent(fHospital.value));
      if (fTexto.value.trim()) p.push('q=' + encodeURIComponent(fTexto.value.trim()));
      return p.length ? '?' + p.join('&') : '';
    }

    // Formulario de alta/edición generado a partir del esquema de la colección.
    function abrirFormulario(it) {
      var campos = [G.campoHospital(it ? it.hospital : '')];
      camposVisibles().forEach(function (c) {
        var base = {
          name: c.clave,
          label: c.etiqueta,
          required: !!c.obligatorio,
          maxlength: c.max,
          value: it && it.datos ? (it.datos[c.clave] || '') : ''
        };
        if (c.tipo === 'area') base.tipo = 'textarea';
        else if (c.tipo === 'numero') { base.tipo = 'number'; base.min = 1; }
        else if (c.tipo === 'lista') {
          base.tipo = 'select';
          base.opciones = [{ v: '', t: '—' }].concat((c.opciones || []).map(function (o) { return { v: o, t: o }; }));
        }
        campos.push(base);
      });
      G.formDialog({
        titulo: (it ? 'Editar ' : 'Nuevo ') + esquema.singular.toLowerCase(),
        intro: esquema.nombre + ' — la clasificación por hospital es obligatoria.',
        ancho: true,
        campos: campos,
        textoSubmit: it ? 'Guardar cambios' : 'Guardar',
        onSubmit: function (v) {
          var datos = {};
          camposVisibles().forEach(function (c) { datos[c.clave] = v[c.clave] == null ? '' : String(v[c.clave]); });
          return G.api('/api/inventario/' + coleccion + (it ? '/' + it.id : ''), {
            method: it ? 'PUT' : 'POST',
            body: JSON.stringify({ hospital: v.hospital, datos: datos })
          });
        },
        onClose: function () { cargar(); }
      });
    }

    // Ficha de solo lectura (la usan ambos roles; el Admin edita con los botones de la tabla).
    function abrirDetalle(it) {
      var lineas = [{ k: 'Hospital', v: it.hospital || '—' }]
        .concat(camposVisibles().map(function (c) { return { k: c.etiqueta, v: (it.datos && it.datos[c.clave]) || '—' }; }))
        .concat([
          { k: 'Registrado por', v: (it.creadoPor || '—') + ' · ' + G.fmtFechaHora(it.creado) },
          { k: 'Última actualización', v: it.actualizado ? G.fmtFechaHora(it.actualizado) : '—' }
        ]);
      var html = '<div class="meta-grid">' + lineas.map(function (l) {
        return '<div><b>' + G.esc(l.k) + '</b>' + G.esc(l.v) + '</div>';
      }).join('') + '</div>';
      G.formDialog({
        titulo: esquema.singular + ' · ' + (it.hospital || 'sin hospital'),
        intro: 'Registro #' + it.id,
        ancho: true,
        contenidoHtml: html,
        campos: [],
        textoCancelar: null,
        textoSubmit: 'Cerrar',
        claseSubmit: 'btn-secondary',
        onSubmit: function () { return Promise.resolve(); }
      });
    }

    function eliminar(it) {
      G.confirmar('¿Eliminar este registro del ' + esquema.nombre + '? Esta acción no se puede deshacer.', function () {
        G.api('/api/inventario/' + coleccion + '/' + it.id, { method: 'DELETE' })
          .then(function () { cargar(); })
          .catch(function (e) { G.error(error, e.message); });
      });
    }

    function cargar() {
      return G.api('/api/inventario/' + coleccion + query()).then(function (lista) {
        todo = lista || [];
        repintar();
      }).catch(function (e) { G.error(error, e.message); });
    }

    document.getElementById('btnRegresar').addEventListener('click', function () {
      window.location.href = G.paginaRegreso();
    });
    fTexto.addEventListener('input', repintar);
    fHospital.addEventListener('change', repintar);
    btnExcel.addEventListener('click', function () {
      G.sinError(error);
      btnExcel.disabled = true;
      G.descargar('/api/inventario/' + coleccion + '/excel' + query())
        .catch(function (e) { G.error(error, 'No se pudo generar el Excel: ' + e.message); })
        .finally(function () { btnExcel.disabled = false; });
    });
    btnNuevo.addEventListener('click', function () { G.sinError(error); abrirFormulario(null); });
    cuerpo.addEventListener('click', function (ev) {
      var tr = ev.target.closest('tr[data-id]');
      if (!tr) return;
      var it = todo.find(function (x) { return String(x.id) === tr.dataset.id; });
      if (it) abrirDetalle(it);
    });

    G.init().then(function () {
      document.getElementById('badgeUsuario').textContent =
        G.YO + ' · ' + G.nombreRol(G.ROL);
      llenarSelect(fHospital, G.opcionesHospital({ vacio: null }));
      btnExcel.hidden = !G.tiene('inventario.exportar');
      btnNuevo.hidden = !puedeAdministrar();
      return G.api('/api/inventario');
    }).then(function (colecciones) {
      esquema = (colecciones || []).find(function (c) { return c.clave === coleccion; });
      if (!esquema) throw new Error('El inventario no existe.');
      titulo.textContent = esquema.nombre;
      document.title = esquema.nombre + ' — Intranet meditechnosys';
      pintarEncabezado();
      return cargar();
    }).catch(function (e) { G.error(error, e.message); });
  }

  return { iniciar: iniciar };
})();
