/* Gestión de los avisos del inicio (publicar / editar / eliminar).
   Solo se carga cuando el usuario tiene el permiso `avisos.administrar`; el panel lo ven todos.
   Los cambios se guardan por la API /api/avisos y, al terminar, se recarga la página para
   reflejar el panel actualizado. */
(function () {
  'use strict';

  var dlg = document.getElementById('dlgAviso');
  var form = document.getElementById('formAviso');
  if (!dlg || !form) return; // sin permiso de administración: el panel es de solo lectura

  var encabezado = document.getElementById('dlgAvisoEncabezado');
  var error = document.getElementById('avisoError');
  var fTitulo = document.getElementById('avTitulo');
  var fEtiqueta = document.getElementById('avEtiqueta');
  var fCuerpo = document.getElementById('avCuerpo');
  var fActivo = document.getElementById('avActivo');
  var editandoId = null;

  function mostrarError(msg) { error.textContent = msg; error.hidden = false; }
  function limpiarError() { error.textContent = ''; error.hidden = true; }

  function abrir(datos) {
    editandoId = datos ? datos.id : null;
    encabezado.textContent = datos ? 'Editar aviso' : 'Publicar aviso';
    fTitulo.value = datos ? datos.titulo : '';
    fEtiqueta.value = datos ? datos.etiqueta : '';
    fCuerpo.value = datos ? datos.cuerpo : '';
    fActivo.checked = datos ? datos.activo : true;
    limpiarError();
    if (typeof dlg.showModal === 'function') dlg.showModal(); else dlg.setAttribute('open', '');
    fTitulo.focus();
  }

  function cerrar() { if (typeof dlg.close === 'function') dlg.close(); else dlg.removeAttribute('open'); }

  function guardar(ev) {
    ev.preventDefault();
    limpiarError();
    var titulo = (fTitulo.value || '').trim();
    if (titulo.length < 3) { mostrarError('El título debe tener al menos 3 caracteres.'); return; }
    var cuerpo = {
      titulo: titulo,
      etiqueta: (fEtiqueta.value || '').trim(),
      cuerpo: (fCuerpo.value || '').trim(),
      activo: fActivo.checked
    };
    var url = editandoId ? '/api/avisos/' + encodeURIComponent(editandoId) : '/api/avisos';
    var metodo = editandoId ? 'PUT' : 'POST';
    fetch(url, {
      method: metodo,
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(cuerpo)
    }).then(function (r) {
      if (r.ok || r.status === 201 || r.status === 204) { window.location.reload(); return; }
      return r.json().then(function (j) { mostrarError((j && j.error) || 'No se pudo guardar el aviso.'); });
    }).catch(function () { mostrarError('No se pudo conectar con el servidor.'); });
  }

  function eliminar(id, titulo) {
    if (!window.confirm('¿Eliminar el aviso "' + titulo + '"? Esta acción no se puede deshacer.')) return;
    fetch('/api/avisos/' + encodeURIComponent(id), { method: 'DELETE', credentials: 'same-origin' })
      .then(function (r) {
        if (r.ok || r.status === 204) { window.location.reload(); return; }
        return r.json().then(function (j) { window.alert((j && j.error) || 'No se pudo eliminar el aviso.'); });
      })
      .catch(function () { window.alert('No se pudo conectar con el servidor.'); });
  }

  document.getElementById('btnNuevoAviso').addEventListener('click', function () { abrir(null); });
  document.getElementById('avCancelar').addEventListener('click', cerrar);
  form.addEventListener('submit', guardar);
  dlg.addEventListener('cancel', function (ev) { ev.preventDefault(); cerrar(); });

  // Botones por aviso (Editar / Eliminar) pintados por el servidor solo con permiso.
  Array.prototype.forEach.call(document.querySelectorAll('[data-edit]'), function (b) {
    b.addEventListener('click', function () {
      abrir({
        id: b.getAttribute('data-edit'),
        titulo: b.getAttribute('data-titulo') || '',
        cuerpo: b.getAttribute('data-cuerpo') || '',
        etiqueta: b.getAttribute('data-etiqueta') || '',
        activo: b.getAttribute('data-activo') === '1'
      });
    });
  });
  Array.prototype.forEach.call(document.querySelectorAll('[data-del]'), function (b) {
    b.addEventListener('click', function () {
      eliminar(b.getAttribute('data-del'), b.getAttribute('data-titulo') || '');
    });
  });
})();
