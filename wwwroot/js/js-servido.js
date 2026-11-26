/* Módulo compartido del módulo Gestión (calendario, históricos, tickets y reportes).
   Aporta: identidad + permisos, helpers de fetch/fechas, insignias de estado/prioridad/tipo,
   diálogos genéricos de formulario y el diálogo de detalle con seguimiento completo
   (estatus, avance, reportes, asignación, historial de cambios y cierre formal).
   Las páginas lo cargan con <script src="/js/gestion.js"> antes de su propio script. */
window.Gestion = (function () {
  'use strict';
  var G = {};

  G.ROL = null; G.YO = null; G.PERMISOS = {};
  G.HOSPITALES = ['HES', 'HGR', 'HGO', 'HI'];
  G.HOSPITALES_CON_GENERAL = ['HES', 'HGR', 'HGO', 'HI', 'General'];
  // La fecha límite es exclusivamente administrativa: false para el Ingeniero y cuando el
  // Administrador la deshabilita en Configuración.
  G.FECHA_LIMITE = false;

  G.esAdmin = function () { return G.ROL === 'Admin'; };
  G.tiene = function (clave) { return !!G.PERMISOS[clave]; };
  G.usaFechaLimite = function () { return G.FECHA_LIMITE === true; };
  // Ve todos los elementos (no solo los propios): el Administrador o quien tenga permiso de
  // histórico/supervisión. Refleja en el cliente la regla VeTodo del servidor.
  G.veTodo = function () { return G.esAdmin() || G.tiene('historico.actividades') || G.tiene('supervision.porIngeniero'); };
  // Nombre para mostrar de un rol (los roles personalizados se muestran tal cual).
  G.nombreRol = function (rol) {
    if (!rol) return '(sin rol)';
    if (String(rol).toLowerCase() === 'admin') return 'Administrador';
    if (String(rol).toLowerCase() === 'ingeniero') return 'Ingeniero';
    return rol;
  };

  G.api = function (ruta, opciones) {
    return fetch(ruta, Object.assign({
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      credentials: 'same-origin'
    }, opciones || {})).then(function (r) {
      if (r.status === 204) return null;
      return r.json().catch(function () { return {}; }).then(function (data) {
        if (!r.ok) throw new Error(data.error || ('Error ' + r.status));
        return data;
      });
    });
  };

  // Descarga un archivo (Excel) usando la cookie de sesión y disparando el guardado del navegador.
  // Si el servidor responde un error JSON, se muestra en vez de bajar un archivo corrupto.
  G.descargar = function (ruta) {
    return fetch(ruta, { credentials: 'same-origin' }).then(function (r) {
      if (!r.ok) {
        return r.json().catch(function () { return {}; }).then(function (d) {
          throw new Error(d.error || ('Error ' + r.status));
        });
      }
      var nombre = 'descarga.xlsx';
      var cd = r.headers.get('Content-Disposition') || '';
      var m = /filename="?([^";]+)"?/.exec(cd);
      if (m) { try { nombre = decodeURIComponent(m[1]); } catch (e) { nombre = m[1]; } }
      return r.blob().then(function (b) {
        var url = URL.createObjectURL(b);
        var a = document.createElement('a');
        a.href = url; a.download = nombre; document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 1000);
        return nombre;
      });
    });
  };

  // Sube un formulario multipart (archivos) con la cookie de sesión.
  G.subir = function (ruta, datos) {
    return fetch(ruta, { method: 'POST', body: datos, credentials: 'same-origin' }).then(function (r) {
      if (r.status === 204) return null;
      return r.json().catch(function () { return {}; }).then(function (data) {
        if (!r.ok) throw new Error(data.error || ('Error ' + r.status));
        return data;
      });
    });
  };

  // Identidad (rol/usuario) y permisos activos de la sesión.
  G.init = function () {
    return Promise.all([
      G.api('/api/patologia/me'),
      G.api('/api/permisos/mios')
    ]).then(function (res) {
      G.YO = res[0].usuario; G.ROL = res[0].rol;
      G.PERMISOS = {};
      (res[1].permisos || []).forEach(function (k) { G.PERMISOS[k] = true; });
      G.FECHA_LIMITE = res[1].fechaLimite === true;
      if (res[1].hospitales && res[1].hospitales.length) G.HOSPITALES = res[1].hospitales;
      if (res[1].hospitalesConGeneral && res[1].hospitalesConGeneral.length) G.HOSPITALES_CON_GENERAL = res[1].hospitalesConGeneral;
      // Catálogos editables desde Programador. Si la API no los manda (versión anterior del
      // servidor) se quedan los valores por defecto de G.TIPOS_ACTIVIDAD.
      var cat = res[1].catalogos || {};
      if (cat.actividades && cat.actividades.length) G.CATALOGO.actividades = cat.actividades;
      if (cat.subopciones) G.CATALOGO.subopciones = cat.subopciones;
      if (cat.servicios) G.CATALOGO.servicios = cat.servicios;
      if (cat.serviciosPorHospital) G.CATALOGO.serviciosPorHospital = cat.serviciosPorHospital;
      return G;
    });
  };

  // Página de origen: ahora hay una única página de inicio ("/") que muestra las herramientas
  // según los permisos efectivos, así que "Regresar" siempre vuelve ahí.
  G.paginaRegreso = function () { return '/'; };

  // Desplegable de hospital para formularios y filtros.
  // opts: { general: true } agrega la opción "General" (la usa el Playbook);
  //       { vacio: 'Todos' } pone una opción vacía con ese texto (vacio: null la omite).
  G.opcionesHospital = function (opts) {
    opts = opts || {};
    var lista = opts.general ? G.HOSPITALES_CON_GENERAL : G.HOSPITALES;
    var ops = [];
    var vacio = opts.vacio === undefined ? 'Seleccione hospital' : opts.vacio;
    if (vacio) ops.push({ v: '', t: vacio });
    return ops.concat(lista.map(function (h) { return { v: h, t: h }; }));
  };
  G.campoHospital = function (valor, opts) {
    return {
      name: 'hospital', label: 'Hospital', tipo: 'select', required: true,
      value: valor || '', opciones: G.opcionesHospital(opts || {})
    };
  };

  // ---- fechas y formatos ----
  function p2(n) { return (n < 10 ? '0' : '') + n; }
  G.claveDia = function (d) { return d.getFullYear() + '-' + p2(d.getMonth() + 1) + '-' + p2(d.getDate()); };
  G.hoy = function () { return G.claveDia(new Date()); };
  G.fmtFecha = function (iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    return p2(d.getDate()) + '/' + p2(d.getMonth() + 1) + '/' + d.getFullYear();
  };
  G.fmtFechaHora = function (iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    return G.fmtFecha(iso) + ' ' + p2(d.getHours()) + ':' + p2(d.getMinutes());
  };
  G.fmtFechaLarga = function (iso) {
    return new Date(iso).toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  };

  G.ESTADOS = {
    Pendiente: 'Pendiente', EnProceso: 'En proceso', Completada: 'Completada',
    Cerrada: 'Cerrada', Cancelada: 'Cancelada'
  };
  G.ORDEN_ESTADOS = ['Pendiente', 'EnProceso', 'Completada', 'Cerrada', 'Cancelada'];
  G.PRIORIDADES = ['Baja', 'Media', 'Alta', 'Urgente'];
  G.TIPOS = ['Actividad', 'Incidente', 'Ticket'];
  // Al crear una Actividad el título se elige de esta lista (los detalles van en la descripción).
  // Son el respaldo por defecto: en cuanto responde `/api/permisos/mios`, las opciones reales son
  // las que edita la pantalla **Programador** (ver G.CATALOGO).
  G.TIPOS_ACTIVIDAD = [
    'Fusiones', 'Exportación de estudios DICOM', 'Revisión de migración', 'Capacitación',
    'Documentación', 'Verificación de servicio', 'Mantenimiento preventivo', 'Mantenimiento correctivo'
  ];
  // Catálogos del módulo Gestión leídos del servidor: las actividades, las subopciones de alguna de
  // ellas (`{ 'Capacitación': { etiqueta: 'Aplicativo', opciones: [...] } }`), los servicios y los
  // servicios que ofrece cada hospital (`{ 'HES': [...], 'HGR': [...] }`).
  G.CATALOGO = { actividades: G.TIPOS_ACTIVIDAD, subopciones: {}, servicios: [], serviciosPorHospital: {} };

  // Subopciones configuradas para una actividad, o null si esa actividad no tiene ninguna.
  G.subopcionesDe = function (actividad) {
    if (!actividad) return null;
    var sub = G.CATALOGO.subopciones || {};
    return sub[actividad] || null;
  };

  // Servicios del hospital elegido. Con el catálogo por hospital ya configurado, un hospital que
  // no aparece en él se queda sin servicios (el campo no se muestra). Mientras no se configure, o
  // contra un servidor anterior que no manda el mapa, todos usan la lista general.
  G.serviciosDe = function (hospital) {
    var general = (G.CATALOGO.servicios || []).filter(function (s) { return !!s; });
    if (!hospital) return [];
    var porHospital = G.CATALOGO.serviciosPorHospital || {};
    var h = String(hospital).toUpperCase();
    var claves = Object.keys(porHospital);
    if (!claves.length) return general;
    for (var i = 0; i < claves.length; i++) {
      if (claves[i].toUpperCase() !== h) continue;
      return (porHospital[claves[i]] || []).filter(function (s) { return !!s; });
    }
    return [];
  };

  // Título que se guarda en una Actividad: «Actividad — subopción — servicio».
  G.tituloActividad = function (actividad, subopcion, servicio) {
    return [actividad, subopcion, servicio].filter(function (x) { return !!x; }).join(' — ');
  };

  G.badge = function (clase, texto) {
    return '<span class="badge-chip ' + clase + '">' + texto + '</span>';
  };
  G.badgeEstado = function (a) {
    var h = G.badge('est-' + a.estado, G.ESTADOS[a.estado] || a.estado);
    if (G.usaFechaLimite() && a.vencida && a.estado !== 'Cancelada') h += ' ' + G.badge('est-Vencida', 'Vencida');
    return h;
  };
  G.badgePrioridad = function (p) { return G.badge('prio-' + p, p); };
  G.badgeTipo = function (t) { return G.badge('tipo-' + t, t); };
  G.avanceHtml = function (pct) {
    pct = Math.max(0, Math.min(100, pct || 0));
    return '<span class="avance"><span class="pista"><i style="width:' + pct + '%"></i></span><span>' + pct + '%</span></span>';
  };
  G.esc = function (s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  };

  G.error = function (el, msg) { el.textContent = msg; el.hidden = false; };
  G.sinError = function (el) { el.textContent = ''; el.hidden = true; };

  // ---- diálogos genéricos ----
  // Crea un <dialog> con un formulario a partir de una lista de campos y lo abre.
  // cfg: { titulo, intro, contenidoHtml (bloque de solo lectura), ancho, campos, textoSubmit, ... }
  // campo: { name, label, tipo: text|textarea|date|number|select, opciones: [{v,t}], value,
  //          required, min, max, step, hint, soloLectura, onCambio(valor, api) }
  // onSubmit(valores) debe devolver una promesa; si falla, el error se muestra dentro.
  G.formDialog = function (cfg) {
    var dlg = document.createElement('dialog');
    dlg.className = 'modal' + (cfg.ancho ? ' ancho' : '');
    var form = document.createElement('form');
    form.className = 'modal-form';
    form.noValidate = true;
    var h = document.createElement('h3'); h.textContent = cfg.titulo; form.appendChild(h);
    if (cfg.intro) {
      var pi = document.createElement('p'); pi.className = 'hint'; pi.style.margin = '0';
      pi.textContent = cfg.intro; form.appendChild(pi);
    }
    var zonaContenido = null;
    if (cfg.contenidoHtml) {
      zonaContenido = document.createElement('div');
      zonaContenido.innerHTML = cfg.contenidoHtml;
      // `contenidoDespues` coloca el bloque interactivo debajo de los campos de la cabecera
      // (p. ej. el constructor de líneas del almacén); por defecto va arriba, como resumen.
      if (!cfg.contenidoDespues) form.appendChild(zonaContenido);
    }
    var campos = {};
    (cfg.campos || []).forEach(function (c) {
      // Casilla de verificación: entrada + texto en la misma línea (no apilados).
      if (c.tipo === 'checkbox') {
        var labChk = document.createElement('label');
        labChk.className = 'campo-chk';
        var chk = document.createElement('input');
        chk.type = 'checkbox';
        chk.checked = !!c.value;
        if (c.hint) labChk.title = c.hint;
        var txtChk = document.createElement('span'); txtChk.textContent = c.label;
        labChk.appendChild(chk); labChk.appendChild(txtChk);
        form.appendChild(labChk);
        campos[c.name] = chk;
        return;
      }
      var lab = document.createElement('label');
      lab.textContent = c.label + (c.required ? ' (obligatorio)' : '');
      var inp;
      if (c.tipo === 'textarea') { inp = document.createElement('textarea'); }
      else if (c.tipo === 'select') {
        inp = document.createElement('select');
        (c.opciones || []).forEach(function (o) {
          var op = document.createElement('option'); op.value = o.v; op.textContent = o.t;
          inp.appendChild(op);
        });
      } else if (c.tipo === 'file') {
        inp = document.createElement('input');
        inp.type = 'file';
        if (c.accept) inp.accept = c.accept;
      } else {
        inp = document.createElement('input');
        inp.type = c.tipo || 'text';
        if (c.min != null) inp.min = c.min;
        if (c.max != null) inp.max = c.max;
        if (c.step != null) inp.step = c.step;
      }
      if (c.required) inp.required = true;
      if (c.maxlength) inp.maxLength = c.maxlength;
      if (c.placeholder) inp.placeholder = c.placeholder;
      if (c.value != null && c.tipo !== 'file') inp.value = c.value;
      if (c.soloLectura) { inp.disabled = true; }
      lab.appendChild(inp);
      form.appendChild(lab);
      campos[c.name] = inp;
    });
    // Una casilla con `desactiva: 'otroCampo'` deshabilita y limpia ese campo al marcarse
    // (p. ej. "Deshabilitar fecha límite" apaga el selector de fecha de ese elemento).
    (cfg.campos || []).forEach(function (c) {
      if (c.tipo !== 'checkbox' || !c.desactiva) return;
      var chk = campos[c.name], objetivo = campos[c.desactiva];
      if (!chk || !objetivo) return;
      var aplicar = function () {
        objetivo.disabled = chk.checked;
        if (chk.checked) objetivo.value = '';
      };
      chk.addEventListener('change', aplicar);
      aplicar();
    });
    if (zonaContenido && cfg.contenidoDespues) form.appendChild(zonaContenido);
    // `onCambio(valor, api)` reacciona a un campo y puede tocar los demás: api.set(nombre, valor)
    // escribe y api.get(nombre) lee. Se usa para autocompletar (p. ej. nombre y cargo al elegir
    // un usuario en las actas y en las credenciales).
    var apiCampos = {
      set: function (nombre, valor) { var i = campos[nombre]; if (i) i.value = valor == null ? '' : valor; },
      get: function (nombre) { var i = campos[nombre]; return i ? i.value : ''; },
      // Muestra u oculta un campo completo (su etiqueta incluida) y lo deja activo o inactivo.
      // Los campos inactivos no se envían: ver el recolector de valores más abajo. Se usa
      // `style.display` porque el CSS de panel.css pone `label { display: flex }` y eso gana
      // sobre el atributo `hidden`, que dejaría el campo visible.
      mostrar: function (nombre, visible) {
        var i = campos[nombre]; if (!i) return;
        var lab = i.closest('label'); if (lab) lab.style.display = visible ? '' : 'none';
        i.disabled = !visible;
      },
      // Cambia las opciones de un desplegable ya abierto (la subopción depende de la actividad
      // elegida). El valor se pierde a propósito: opciones nuevas piden una elección nueva.
      opciones: function (nombre, lista) {
        var i = campos[nombre]; if (!i || i.tagName !== 'SELECT') return;
        i.innerHTML = '';
        (lista || []).forEach(function (o) {
          var op = document.createElement('option'); op.value = o.v; op.textContent = o.t;
          i.appendChild(op);
        });
      },
      // Reescribe el texto de la etiqueta de un campo (el desplegable de subopción se llama
      // «Aplicativo», «Modelo» o lo que diga el catálogo de esa actividad).
      etiqueta: function (nombre, texto) {
        var i = campos[nombre]; if (!i) return;
        var lab = i.closest('label'); if (!lab || !lab.firstChild) return;
        lab.firstChild.nodeValue = texto + (i.required ? ' (obligatorio)' : '');
      }
    };
    (cfg.campos || []).forEach(function (c) {
      if (typeof c.onCambio !== 'function') return;
      var i = campos[c.name];
      if (!i) return;
      i.addEventListener('change', function () { c.onCambio(i.value, apiCampos); });
    });
    // Campo con `ocultoInicial: true`: arranca escondido e inactivo hasta que otro campo lo muestre
    // con api.mostrar (así la lista de actividades solo ocupa el lugar del título en una Actividad).
    (cfg.campos || []).forEach(function (c) {
      if (c.ocultoInicial) apiCampos.mostrar(c.name, false);
    });
    var err = document.createElement('p');
    err.className = 'form-error'; err.setAttribute('role', 'alert'); err.hidden = true;
    form.appendChild(err);
    var acc = document.createElement('div'); acc.className = 'modal-actions';
    // textoCancelar: null oculta el botón (diálogos de solo lectura con una única acción).
    if (cfg.textoCancelar !== null) {
      var bc = document.createElement('button'); bc.type = 'button'; bc.className = 'btn-secondary';
      bc.textContent = cfg.textoCancelar || 'Cancelar';
      acc.appendChild(bc);
      bc.addEventListener('click', function () { dlg.close(); });
    }
    var bs = document.createElement('button'); bs.type = 'submit'; bs.className = cfg.claseSubmit || 'btn-primary'; bs.textContent = cfg.textoSubmit || 'Guardar';
    acc.appendChild(bs);
    form.appendChild(acc);
    dlg.appendChild(form);
    document.body.appendChild(dlg);
    form.addEventListener('submit', function (ev) {
      ev.preventDefault();
      G.sinError(err);
      var valores = {};
      Object.keys(campos).forEach(function (k) {
        var inp = campos[k];
        if (inp.disabled) { valores[k] = undefined; return; }
        if (inp.type === 'checkbox') { valores[k] = inp.checked; return; }
        if (inp.type === 'file') { valores[k] = inp.files && inp.files[0] ? inp.files[0] : null; return; }
        valores[k] = inp.type === 'number' ? (inp.value === '' ? null : Number(inp.value)) : String(inp.value).trim();
      });
      bs.disabled = true;
      Promise.resolve().then(function () { return cfg.onSubmit(valores); })
        .then(function () { dlg.close(); })
        .catch(function (e) { G.error(err, e.message); })
        .finally(function () { bs.disabled = false; });
    });
    dlg.addEventListener('close', function () { dlg.remove(); if (cfg.onClose) cfg.onClose(); });
    dlg.showModal();
    return dlg;
  };

  G.confirmar = function (mensaje, onSi) { if (window.confirm(mensaje)) onSi(); };

  // ---- detalle con seguimiento completo ----
  // Abre el diálogo de detalle de una actividad/incidente/ticket con barra de estatus,
  // metadatos, pestañas de reportes/historial y las acciones que el rol y el estado permiten.
  // opts: { onReload: fn()->Promise (refresca la lista de la página tras cada cambio) }
  G.abrirDetalle = function (item, opts) {
    opts = opts || {};
    function recargar() { return opts.onReload ? Promise.resolve(opts.onReload()) : Promise.resolve(); }
    function refresco() {
      // Tras un cambio: refresca la página y vuelve a abrir el detalle con datos nuevos
      // (salvo que el elemento ya no sea visible).
      return recargar().then(function () {
        return G.api('/api/actividades/' + item.id).then(function (nuevo) {
          dlg.close();
          G.abrirDetalle(nuevo, opts);
        }).catch(function () { dlg.close(); });
      });
    }
    function accion(promesaFn) {
      return promesaFn().then(refresco);
    }

    var abierto = item.estado === 'Pendiente' || item.estado === 'EnProceso';
    var dueno = G.veTodo() || String(item.usuario || '').toLowerCase() === String(G.YO || '').toLowerCase();
    var creador = String(item.creadoPor || '').toLowerCase() === String(G.YO || '').toLowerCase();

    var dlg = document.createElement('dialog');
    dlg.className = 'modal ancho';
    var cuerpo = document.createElement('div');
    cuerpo.className = 'modal-form detalle';
    dlg.appendChild(cuerpo);
    document.body.appendChild(dlg);

    // Encabezado
    var h = document.createElement('h3');
    h.textContent = (item.tipo === 'Ticket' && item.numero ? '#' + String(item.numero).padStart(3, '0') + ' · ' : '') + item.titulo;
    cuerpo.appendChild(h);

    var bd = document.createElement('div');
    bd.className = 'badges';
    bd.innerHTML = G.badgeTipo(item.tipo) + ' ' + G.badgeEstado(item) + ' ' + G.badgePrioridad(item.prioridad);
    cuerpo.appendChild(bd);

    // Barra de estatus
    var barra = document.createElement('div');
    barra.className = 'estatus';
    G.ORDEN_ESTADOS.forEach(function (e) {
      var s = document.createElement('div');
      s.setAttribute('data-e', e);
      s.textContent = G.ESTADOS[e];
      if (e === item.estado) s.classList.add('on');
      barra.appendChild(s);
    });
    cuerpo.appendChild(barra);

    // Aviso de reporte pendiente (solicitud del sistema al ingeniero)
    if (item.reportePendiente && dueno && abierto) {
      var av = document.createElement('p');
      av.className = 'aviso-reporte';
      av.textContent = 'Reporte de estatus solicitado: tocaba el ' + G.fmtFecha(item.proximoReporte) +
        ' (cada ' + item.periodicidadDias + ' día(s)). Entrégalo con el botón "Entregar reporte".';
      cuerpo.appendChild(av);
    }

    // Metadatos
    var mg = document.createElement('div');
    mg.className = 'meta-grid';
    function meta(etiqueta, valorHtml) {
      var d = document.createElement('div');
      d.innerHTML = '<b>' + etiqueta + '</b>' + valorHtml;
      mg.appendChild(d);
    }
    meta('Hospital', G.esc(item.hospital || '(sin hospital)'));
    meta('Ingeniero responsable', G.esc(item.usuario || '(sin asignar)'));
    meta('Fecha de inicio', G.fmtFecha(item.fecha));
    if (G.usaFechaLimite()) meta('Fecha límite', item.fechaLimite ? G.fmtFecha(item.fechaLimite) : '—');
    meta('Registró', G.esc(item.creadoPor || '—') + ' · ' + G.fmtFecha(item.creado));
    if (item.asignado) meta('Fecha de asignación', G.fmtFecha(item.asignado));
    meta('Avance', G.avanceHtml(item.avance));
    if (item.periodicidadDias > 0) {
      meta('Periodicidad de reporte', 'cada ' + item.periodicidadDias + ' día(s)');
      meta('Último reporte', item.ultimoReporte ? G.fmtFechaHora(item.ultimoReporte) : '—');
      meta('Próximo reporte', abierto && item.proximoReporte ? G.fmtFecha(item.proximoReporte) : '—');
    }
    if (item.cerrado) meta('Fecha de cierre', G.fmtFechaHora(item.cerrado));
    cuerpo.appendChild(mg);

    if (item.descripcion) {
      var cd = document.createElement('div');
      cd.className = 'caja';
      cd.textContent = item.descripcion;
      cuerpo.appendChild(cd);
    }
    if (item.estado === 'Completada' || item.estado === 'Cerrada') {
      if (item.informe) {
        var ci = document.createElement('div');
        ci.innerHTML = '<b style="font-size:11px;text-transform:uppercase;color:var(--suave)">Informe final</b>';
        var cii = document.createElement('div'); cii.className = 'caja'; cii.textContent = item.informe;
        ci.appendChild(cii); cuerpo.appendChild(ci);
      }
    }
    if (item.estado === 'Cancelada' && item.justificacion) {
      var cj = document.createElement('div');
      cj.innerHTML = '<b style="font-size:11px;text-transform:uppercase;color:var(--suave)">Justificación de la cancelación</b>';
      var cjj = document.createElement('div'); cjj.className = 'caja'; cjj.textContent = item.justificacion;
      cj.appendChild(cjj); cuerpo.appendChild(cj);
    }

    // Pestañas Reportes / Historial
    var tabs = document.createElement('div');
    tabs.className = 'tabs';
    var btnRep = document.createElement('button'); btnRep.type = 'button'; btnRep.textContent = 'Reportes de estatus'; btnRep.className = 'on';
    var btnHis = document.createElement('button'); btnHis.type = 'button'; btnHis.textContent = 'Historial de cambios';
    tabs.appendChild(btnRep); tabs.appendChild(btnHis);
    cuerpo.appendChild(tabs);
    var zonaRep = document.createElement('div');
    var zonaHis = document.createElement('div'); zonaHis.hidden = true;
    cuerpo.appendChild(zonaRep); cuerpo.appendChild(zonaHis);
    var cargados = { rep: false, his: false };

    function pintarReportes(lista) {
      zonaRep.innerHTML = '';
      if (!lista.length) { zonaRep.innerHTML = '<p class="hint">Sin reportes de estatus todavía.</p>'; return; }
      var ul = document.createElement('ul'); ul.className = 'lista-entradas';
      lista.forEach(function (r) {
        var li = document.createElement('li');
        li.innerHTML = '<span class="cuando">' + G.fmtFechaHora(r.momento) + ' · ' + G.esc(r.autor) +
          ' · ' + G.badge('est-' + r.estado, G.ESTADOS[r.estado] || r.estado) + ' · avance ' + (r.avance || 0) + '%' +
          (r.fechaEstimada ? ' · conclusión estimada: ' + G.fmtFecha(r.fechaEstimada) : '') + '</span>' +
          '<span class="de">' + G.esc(r.descripcionAvance) +
          (r.problemas ? '\nProblemas: ' + G.esc(r.problemas) : '') +
          (r.pendientes ? '\nPendientes: ' + G.esc(r.pendientes) : '') +
          (r.proximasAcciones ? '\nPróximas acciones: ' + G.esc(r.proximasAcciones) : '') +
          (r.comentarios ? '\nComentarios: ' + G.esc(r.comentarios) : '') + '</span>';
        ul.appendChild(li);
      });
      zonaRep.appendChild(ul);
    }
    function pintarHistorial(lista) {
      zonaHis.innerHTML = '';
      if (!lista.length) { zonaHis.innerHTML = '<p class="hint">Sin cambios registrados.</p>'; return; }
      var ul = document.createElement('ul'); ul.className = 'lista-entradas';
      lista.forEach(function (c) {
        var li = document.createElement('li');
        var cambio = c.antes || c.despues
          ? G.esc(c.campo) + ': ' + G.esc(c.antes || '—') + ' → ' + G.esc(c.despues || '—')
          : G.esc(c.campo);
        li.innerHTML = '<span class="cuando">' + G.fmtFechaHora(c.momento) + ' · ' + G.esc(c.usuario) + '</span>' +
          '<span class="que">' + cambio + '</span>' +
          (c.comentario ? '<span class="de">' + G.esc(c.comentario) + '</span>' : '');
        ul.appendChild(li);
      });
      zonaHis.appendChild(ul);
    }
    btnRep.addEventListener('click', function () {
      btnRep.classList.add('on'); btnHis.classList.remove('on');
      zonaRep.hidden = false; zonaHis.hidden = true;
      if (!cargados.rep) G.api('/api/actividades/' + item.id + '/reportes').then(function (l) { cargados.rep = true; pintarReportes(l); });
    });
    btnHis.addEventListener('click', function () {
      btnHis.classList.add('on'); btnRep.classList.remove('on');
      zonaHis.hidden = false; zonaRep.hidden = true;
      if (!cargados.his) G.api('/api/actividades/' + item.id + '/historial').then(function (l) { cargados.his = true; pintarHistorial(l); });
    });
    G.api('/api/actividades/' + item.id + '/reportes').then(function (l) { cargados.rep = true; pintarReportes(l); }).catch(function () { });

    // Acciones según rol, permisos y estado
    var acc = document.createElement('div');
    acc.className = 'modal-actions';
    acc.style.flexWrap = 'wrap';
    function boton(texto, clase, fn) {
      var b = document.createElement('button');
      b.type = 'button'; b.className = clase; b.textContent = texto;
      b.addEventListener('click', fn); acc.appendChild(b); return b;
    }

    if (dueno && abierto && G.tiene('items.actualizarEstatus')) {
      if (item.estado === 'Pendiente') {
        boton('Iniciar', 'btn-primary', function () {
          accion(function () { return G.api('/api/actividades/' + item.id + '/estado', { method: 'PUT', body: JSON.stringify({ estado: 'EnProceso' }) }); });
        });
      }
      boton('Completar', 'btn-success', function () {
        G.formDialog({
          titulo: 'Completar ' + item.tipo.toLowerCase(),
          intro: item.titulo,
          campos: [{ name: 'informe', label: 'Informe del trabajo realizado', tipo: 'textarea', required: true, maxlength: 2000, placeholder: 'Qué se hizo, resultados, pendientes…' }],
          textoSubmit: 'Marcar como completada', claseSubmit: 'btn-success',
          onSubmit: function (v) {
            return G.api('/api/actividades/' + item.id + '/estado', { method: 'PUT', body: JSON.stringify({ estado: 'Completada', informe: v.informe }) });
          }
        });
      });
      boton('Cancelar', 'btn-danger', function () {
        G.formDialog({
          titulo: 'Cancelar ' + item.tipo.toLowerCase(),
          intro: item.titulo + ' — la cancelación exige justificación.',
          campos: [{ name: 'justificacion', label: 'Justificación', tipo: 'textarea', required: true, maxlength: 500, placeholder: 'Motivo por el que se cancela…' }],
          textoSubmit: 'Cancelar elemento', claseSubmit: 'btn-danger',
          onSubmit: function (v) {
            return G.api('/api/actividades/' + item.id + '/estado', { method: 'PUT', body: JSON.stringify({ estado: 'Cancelada', justificacion: v.justificacion }) });
          }
        });
      });
    }
    if (dueno && abierto && G.tiene('items.registrarAvance')) {
      boton('Registrar avance', 'btn-secondary', function () {
        G.formDialog({
          titulo: 'Registrar avance',
          intro: item.titulo,
          campos: [
            { name: 'avance', label: 'Porcentaje de avance', tipo: 'number', min: 0, max: 100, step: 5, value: item.avance, required: true },
            { name: 'comentario', label: 'Comentario / problemas encontrados', tipo: 'textarea', maxlength: 1000, placeholder: 'Qué se hizo, qué lo detiene…' }
          ],
          textoSubmit: 'Guardar avance',
          onSubmit: function (v) {
            return G.api('/api/actividades/' + item.id + '/avance', { method: 'PUT', body: JSON.stringify({ avance: v.avance, comentario: v.comentario || '' }) });
          }
        });
      });
    }
    if (dueno && abierto && G.tiene('reportes.entregar')) {
      boton('Entregar reporte', 'btn-primary', function () {
        G.formDialog({
          titulo: 'Reporte de estatus',
          intro: item.titulo + (item.periodicidadDias > 0 ? ' · se entrega cada ' + item.periodicidadDias + ' día(s)' : ''),
          ancho: true,
          campos: [
            { name: 'estado', label: 'Estatus actual', tipo: 'select', value: item.estado === 'Pendiente' ? 'EnProceso' : item.estado, opciones: [{ v: 'Pendiente', t: 'Pendiente' }, { v: 'EnProceso', t: 'En proceso' }] },
            { name: 'avance', label: 'Porcentaje de avance', tipo: 'number', min: 0, max: 100, step: 5, value: item.avance, required: true },
            { name: 'descripcionAvance', label: 'Descripción del avance', tipo: 'textarea', required: true, maxlength: 2000 },
            { name: 'problemas', label: 'Problemas encontrados', tipo: 'textarea', maxlength: 2000 },
            { name: 'pendientes', label: 'Pendientes', tipo: 'textarea', maxlength: 2000 },
            { name: 'proximasAcciones', label: 'Próximas acciones', tipo: 'textarea', maxlength: 2000 },
            { name: 'fechaEstimada', label: 'Fecha estimada de conclusión', tipo: 'date' },
            { name: 'comentarios', label: 'Comentarios adicionales', tipo: 'textarea', maxlength: 2000 }
          ],
          textoSubmit: 'Entregar reporte',
          onSubmit: function (v) {
            return G.api('/api/actividades/' + item.id + '/reportes', {
              method: 'POST',
              body: JSON.stringify({
                estado: v.estado, avance: v.avance, descripcionAvance: v.descripcionAvance,
                problemas: v.problemas || '', pendientes: v.pendientes || '', proximasAcciones: v.proximasAcciones || '',
                fechaEstimada: v.fechaEstimada ? v.fechaEstimada + 'T00:00:00' : null, comentarios: v.comentarios || ''
              })
            });
          }
        });
      });
    }
    // Acciones administrativas: cada botón se muestra solo si el usuario tiene el permiso
    // efectivo correspondiente (ya no depende de ser Admin a secas, sino de la matriz + ajustes).
    if (G.tiene('tickets.asignar') || G.tiene('items.editar') || G.tiene('tickets.observaciones') ||
        G.tiene('tickets.cerrar') || G.tiene('items.eliminar')) {
      if (abierto && G.tiene('tickets.asignar')) {
        boton(item.usuario ? 'Reasignar' : 'Asignar', 'btn-primary', function () {
          G.abrirAsignar(item, refresco);
        });
      }
      if (abierto && G.tiene('items.editar')) {
        boton('Editar', 'btn-secondary', function () { G.abrirEditar(item, refresco); });
      }
      if (abierto && G.tiene('tickets.observaciones')) {
        boton('Observación', 'btn-secondary', function () {
          G.formDialog({
            titulo: 'Agregar observación',
            intro: item.titulo + ' — quedará en el historial de cambios.',
            campos: [{ name: 'comentario', label: 'Observación', tipo: 'textarea', required: true, maxlength: 1000 }],
            textoSubmit: 'Guardar observación',
            onSubmit: function (v) {
              return G.api('/api/actividades/' + item.id + '/observaciones', { method: 'POST', body: JSON.stringify({ comentario: v.comentario }) });
            }
          });
        });
      }
      if (item.estado === 'Completada' && G.tiene('tickets.cerrar')) {
        boton('Cerrar formalmente', 'btn-success', function () {
          G.formDialog({
            titulo: 'Cerrar ' + item.tipo.toLowerCase(),
            intro: 'Revisa el informe final y cierra formalmente el seguimiento.',
            campos: [{ name: 'comentario', label: 'Comentario de cierre', tipo: 'textarea', maxlength: 500 }],
            textoSubmit: 'Cerrar', claseSubmit: 'btn-success',
            onSubmit: function (v) {
              return G.api('/api/actividades/' + item.id + '/estado', { method: 'PUT', body: JSON.stringify({ estado: 'Cerrada', comentario: v.comentario || '' }) });
            }
          });
        });
      }
      if (item.estado === 'Pendiente' && G.tiene('items.eliminar')) {
        boton('Eliminar', 'btn-danger', function () {
          G.confirmar('¿Eliminar "' + item.titulo + '"? Solo se eliminan elementos pendientes sin reportes.', function () {
            accion(function () { return G.api('/api/actividades/' + item.id, { method: 'DELETE' }); });
          });
        });
      }
    }
    boton('Cerrar', 'btn-secondary', function () { dlg.close(); });
    cuerpo.appendChild(acc);

    dlg.showModal();
    return dlg;
  };

  // Diálogo de asignación formal (Admin): ingeniero, prioridad, fecha límite y periodicidad.
  G.abrirAsignar = function (item, despues) {
    G.api('/api/usuarios/asignables').then(function (usuarios) {
      var activos = usuarios.filter(function (u) { return u.activo !== false; });
      var opciones = activos.map(function (u) { return { v: u.usuario, t: u.usuario + ' (' + G.nombreRol(u.rol) + ')' }; });
      var campos = [
        { name: 'usuario', label: 'Ingeniero responsable', tipo: 'select', opciones: opciones, value: item.usuario, required: true },
        { name: 'prioridad', label: 'Prioridad', tipo: 'select', opciones: G.PRIORIDADES.map(function (p) { return { v: p, t: p }; }), value: item.prioridad }
      ];
      if (G.usaFechaLimite()) {
        campos.push({ name: 'fechaLimite', label: 'Fecha límite', tipo: 'date', value: item.fechaLimite ? item.fechaLimite.slice(0, 10) : '' });
        campos.push({ name: 'sinFechaLimite', label: 'Deshabilitar fecha límite', tipo: 'checkbox', desactiva: 'fechaLimite', value: !item.fechaLimite, hint: 'Marca esta casilla para que este elemento no tenga fecha límite.' });
      }
      campos.push(
        { name: 'periodicidadDias', label: 'Periodicidad de reporte (días)', tipo: 'number', min: 1, max: 365, value: item.periodicidadDias || 7, required: true, hint: 'Cada cuántos días debe entregar reporte de estatus' },
        { name: 'comentario', label: 'Comentario de la asignación', tipo: 'textarea', maxlength: 500 }
      );
      G.formDialog({
        titulo: item.usuario ? 'Cambiar asignación' : 'Asignar seguimiento',
        intro: (item.tipo === 'Ticket' && item.numero ? '#' + String(item.numero).padStart(3, '0') + ' · ' : '') + item.titulo,
        campos: campos,
        textoSubmit: 'Asignar',
        onSubmit: function (v) {
          return G.api('/api/actividades/' + item.id + '/asignar', {
            method: 'PUT',
            body: JSON.stringify({
              usuario: v.usuario, prioridad: v.prioridad,
              fechaLimite: v.fechaLimite ? v.fechaLimite + 'T00:00:00' : null,
              periodicidadDias: v.periodicidadDias, comentario: v.comentario || ''
            })
          });
        },
        onClose: despues
      });
    });
  };

  // Diálogo de edición (elementos abiertos, con permiso items.editar).
  G.abrirEditar = function (item, despues) {
    var puedeReasignar = item.tipo === 'Ticket' && G.tiene('tickets.cambiarIngeniero');
    var preparar = puedeReasignar
      ? G.api('/api/usuarios/asignables').then(function (u) { return u; }).catch(function () { return []; })
      : Promise.resolve([]);
    preparar.then(function (usuarios) {
      var campos = [
        { name: 'titulo', label: 'Título', value: item.titulo, required: true, maxlength: 120 },
        G.campoHospital(item.hospital),
        { name: 'descripcion', label: 'Descripción', tipo: 'textarea', value: item.descripcion, maxlength: 1000 }
      ];
      if (usuarios.length) {
        campos.push({
          name: 'usuario', label: 'Usuario asignado', tipo: 'select', value: item.usuario,
          opciones: [{ v: '', t: '(sin asignar)' }].concat(usuarios.filter(function (u) { return u.activo !== false; })
            .map(function (u) { return { v: u.usuario, t: u.usuario }; }))
        });
      }
      campos.push({ name: 'fecha', label: 'Fecha de inicio', tipo: 'date', value: item.fecha.slice(0, 10), required: true });
      if (G.usaFechaLimite()) {
        campos.push({ name: 'fechaLimite', label: 'Fecha límite', tipo: 'date', value: item.fechaLimite ? item.fechaLimite.slice(0, 10) : '' });
        campos.push({ name: 'sinFechaLimite', label: 'Deshabilitar fecha límite', tipo: 'checkbox', desactiva: 'fechaLimite', value: !item.fechaLimite, hint: 'Marca esta casilla para que este elemento no tenga fecha límite.' });
      }
      campos.push({ name: 'prioridad', label: 'Prioridad', tipo: 'select', opciones: G.PRIORIDADES.map(function (p) { return { v: p, t: p }; }), value: item.prioridad });
      G.formDialog({
        titulo: 'Editar ' + item.tipo.toLowerCase(),
        intro: item.titulo,
        campos: campos,
        textoSubmit: 'Guardar cambios',
        onSubmit: function (v) {
          return G.api('/api/actividades/' + item.id, {
            method: 'PUT',
            body: JSON.stringify({
              tipo: item.tipo, hospital: v.hospital, titulo: v.titulo, descripcion: v.descripcion,
              usuario: v.usuario != null ? v.usuario : item.usuario,
              fecha: v.fecha + 'T00:00:00',
              fechaLimite: v.fechaLimite ? v.fechaLimite + 'T00:00:00' : null,
              prioridad: v.prioridad
            })
          });
        },
        onClose: despues
      });
    });
  };

  // Diálogo de creación (Admin e Ingeniero): solo actividades e incidentes. Los tickets se dan de
  // alta como incidencias, por eso el tipo "Ticket" ya no se ofrece aquí ni en el calendario.
  // Para el Ingeniero se omite el usuario asignado: sus elementos se le asignan a sí mismo.
  G.abrirCrear = function (opts) {
    opts = opts || {};
    // G.TIPOS sigue incluyendo 'Ticket' para el Histórico, que consulta elementos ya registrados.
    var tiposCrear = G.TIPOS.filter(function (t) { return t !== 'Ticket'; });
    function mostrar(usuarios) {
      var tipoInicial = tiposCrear.indexOf(opts.tipo) >= 0 ? opts.tipo : 'Actividad';
      var arrancaEnActividad = tipoInicial === 'Actividad';
      var hospitalInicial = opts.hospital || '';
      // El servicio ya no es una lista fija: depende del hospital elegido (Programador guarda una
      // lista por hospital), así que al abrir se pinta con los del hospital que viene precargado.
      var campoHospital = G.campoHospital(hospitalInicial);
      campoHospital.onCambio = function (valor, api) { ajustarServicio(api, api.get('tipo') === 'Actividad'); };
      function serviciosDeHospital(api) { return G.serviciosDe(api.get('hospital')); }
      // Reconstruye el desplegable: cambiar de hospital cambia las opciones y borra la elegida,
      // porque un servicio de otro hospital no tiene sentido en el título.
      function ajustarServicio(api, esActividad) {
        var lista = serviciosDeHospital(api);
        api.opciones('servicio', [{ v: '', t: '(Sin servicio)' }].concat(
          lista.map(function (s) { return { v: s, t: s }; })));
        api.mostrar('servicio', esActividad && lista.length > 0);
      }
      // Con el tipo Actividad la lista de actividades ocupa el lugar del título: la actividad
      // elegida es el título guardado y el detalle va en la descripción. Con Incidente regresa el
      // campo de texto libre.
      function ajustarTipo(valor, api) {
        var esActividad = valor === 'Actividad';
        api.mostrar('actividad', esActividad);
        api.mostrar('titulo', !esActividad);
        if (!esActividad) api.set('actividad', '');
        // Al volver de Incidente la lista viene vacía, así que la subopción se oculta hasta que se
        // elija una actividad que la tenga.
        ajustarActividad(esActividad ? api.get('actividad') : '', api);
        ajustarServicio(api, esActividad);
      }
      // Algunas actividades llevan una segunda lista (los aplicativos de una Capacitación). La
      // etiqueta y las opciones las pone el catálogo, así que el campo cambia de nombre según
      // lo que se eligió arriba y desaparece cuando la actividad no tiene subopciones.
      function ajustarActividad(valor, api) {
        var sub = G.subopcionesDe(valor);
        if (sub) {
          var vacio = '(Selecciona el ' + sub.etiqueta.charAt(0).toLowerCase() + sub.etiqueta.slice(1) + ')';
          api.opciones('subopcion', [{ v: '', t: vacio }].concat(
            (sub.opciones || []).map(function (o) { return { v: o, t: o }; })));
          api.etiqueta('subopcion', sub.etiqueta);
        } else {
          api.opciones('subopcion', [{ v: '', t: '' }]);
          api.set('subopcion', '');
        }
        api.mostrar('subopcion', !!sub);
      }
      var serviciosIniciales = G.serviciosDe(hospitalInicial);
      var campos = [
        { name: 'tipo', label: 'Tipo', tipo: 'select', value: tipoInicial, opciones: tiposCrear.map(function (t) { return { v: t, t: t }; }), onCambio: ajustarTipo },
        campoHospital,
        { name: 'actividad', label: 'Actividad', tipo: 'select', ocultoInicial: !arrancaEnActividad, onCambio: ajustarActividad,
          opciones: [{ v: '', t: '(Selecciona la actividad)' }].concat((G.CATALOGO.actividades || []).map(function (t) { return { v: t, t: t }; })) },
        { name: 'subopcion', label: 'Opción', tipo: 'select', required: true, ocultoInicial: true },
        { name: 'servicio', label: 'Servicio', tipo: 'select', ocultoInicial: !arrancaEnActividad || !serviciosIniciales.length,
          opciones: [{ v: '', t: '(Sin servicio)' }].concat(serviciosIniciales.map(function (s) { return { v: s, t: s }; })) },
        { name: 'titulo', label: 'Título', required: true, maxlength: 120, ocultoInicial: arrancaEnActividad, placeholder: 'p. ej. Falla de red en el HGO' },
        { name: 'descripcion', label: 'Descripción', tipo: 'textarea', maxlength: 1000 },
        { name: 'prioridad', label: 'Prioridad', tipo: 'select', opciones: G.PRIORIDADES.map(function (p) { return { v: p, t: p }; }), value: 'Media' },
        { name: 'fecha', label: 'Fecha de inicio', tipo: 'date', value: opts.fecha || G.hoy(), required: true }
      ];
      if (G.usaFechaLimite()) {
        campos.push({ name: 'fechaLimite', label: 'Fecha límite', tipo: 'date' });
        campos.push({ name: 'sinFechaLimite', label: 'Deshabilitar fecha límite', tipo: 'checkbox', desactiva: 'fechaLimite', value: false, hint: 'Marca esta casilla para que este elemento no tenga fecha límite.' });
      }
      if (usuarios.length) {
        campos.push({
          name: 'usuario', label: 'Ingeniero responsable', tipo: 'select', value: opts.usuario || '',
          opciones: [{ v: '', t: '(Selecciona al ingeniero responsable)' }].concat(usuarios.filter(function (u) { return u.activo !== false; })
            .map(function (u) { return { v: u.usuario, t: u.usuario }; }))
        });
      }
      G.formDialog({
        titulo: opts.titulo || 'Nuevo elemento',
        intro: usuarios.length
          ? 'Elige la actividad de la lista (el detalle va en Descripción) y su ingeniero responsable. ' +
            'Los incidentes se escriben con su propio título. Si necesitas un ticket, regístralo como incidente.'
          : 'Elige la actividad de la lista (el detalle va en Descripción) y quedará asignada a ti. ' +
            'Los incidentes se escriben con su propio título. Si necesitas un ticket, regístralo como incidente.',
        campos: campos,
        textoSubmit: 'Crear',
        onSubmit: function (v) {
          var esActividad = v.tipo === 'Actividad';
          var titulo;
          if (esActividad) {
            var sub = G.subopcionesDe(v.actividad);
            if (!v.actividad) return Promise.reject(new Error('Selecciona la actividad de la lista.'));
            if (sub && !v.subopcion) {
              return Promise.reject(new Error('Selecciona el ' + sub.etiqueta.toLowerCase() + ' de la actividad.'));
            }
            // Todo lo elegido se guarda dentro del título: «Capacitación — Aplicativos Fuji — Anatomía Patológica».
            titulo = G.tituloActividad(v.actividad, sub ? v.subopcion : '', v.servicio);
            if (titulo.length > 120) {
              return Promise.reject(new Error(
                'El título queda muy largo (' + titulo.length + ' caracteres, máximo 120). ' +
                'Acorta los nombres en Programador o deja el servicio vacío.'));
            }
          } else {
            titulo = String(v.titulo || '').trim();
            if (!titulo) return Promise.reject(new Error('Escribe el título.'));
          }
          if (usuarios.length && !v.usuario) return Promise.reject(new Error('Elige el usuario asignado.'));
          return G.api('/api/actividades', {
            method: 'POST',
            body: JSON.stringify({
              tipo: v.tipo, hospital: v.hospital, titulo: titulo, descripcion: v.descripcion,
              usuario: v.usuario || '', prioridad: v.prioridad,
              fecha: v.fecha + 'T00:00:00',
              fechaLimite: v.fechaLimite ? v.fechaLimite + 'T00:00:00' : null,
              // Sin tickets en este diálogo no hay periodicidad que definir; se ajusta al asignar.
              periodicidadDias: 0
            })
          });
        },
        onClose: opts.onClose
      });
    }
    // Solo quien puede asignar ve el selector de ingeniero responsable; los demás crean elementos
    // que quedan pendientes de asignación.
    if (G.tiene('tickets.asignar')) G.api('/api/usuarios/asignables').then(function (u) { mostrar(u); }).catch(function () { mostrar([]); });
    else mostrar([]);
  };

  return G;
})();
