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
      // Región del usuario: si tiene una sola región asignada, se usará como valor por defecto
      G.MIS_REGIONES = res[1].regiones || [];
      G.MI_REGION = res[1].region || '';
      G.VE_TODAS_REGIONES = res[1].veTodasRegiones === true;
      // Catálogos editables desde Programador. Si la API no los manda (versión anterior del
      // servidor) se quedan los valores por defecto de G.TIPOS_ACTIVIDAD.
      var cat = res[1].catalogos || {};
      if (cat.actividades && cat.actividades.length) G.CATALOGO.actividades = cat.actividades;
      if (cat.subopciones) G.CATALOGO.subopciones = cat.subopciones;
      // Los tipos de cada aplicativo. Un servidor anterior no los manda y la lista queda vacía: el
      // campo «Tipo de aplicativo» simplemente no aparece, sin romper nada más.
      if (cat.subopcionesTipos) G.CATALOGO.subopcionesTipos = cat.subopcionesTipos;
      if (cat.servicios) G.CATALOGO.servicios = cat.servicios;
      if (cat.serviciosPorHospital) G.CATALOGO.serviciosPorHospital = cat.serviciosPorHospital;
      if (cat.regiones) G.CATALOGO.regiones = cat.regiones;
      if (cat.regionPorHospital) G.CATALOGO.regionPorHospital = cat.regionPorHospital;
      return G;
    });
  };

  // Página de origen: ahora hay una única página de inicio ("/") que muestra las herramientas
  // según los permisos efectivos, así que "Regresar" siempre vuelve ahí.
  G.paginaRegreso = function () { return '/'; };

  // Desplegable de hospital para formularios y filtros.
  // opts: { general: true } agrega la opción "General" (la usa el Playbook);
  //       { vacio: 'Todos' } pone una opción vacía con ese texto (vacio: null la omite);
  //       { region: 'CMN La Raza' } limita la lista a los hospitales de esa región; con '' lista
  //       todos, que es exactamente como se veía antes de que existieran las regiones.
  G.opcionesHospital = function (opts) {
    opts = opts || {};
    var lista = opts.general ? G.HOSPITALES_CON_GENERAL : G.HOSPITALES;
    if (opts.region) {
      lista = G.hospitalesDeRegion(opts.region);
      // «General» no es un hospital de ninguna región: en el Playbook se sigue ofreciendo aparte.
      if (opts.general) lista = lista.concat(['General']);
    }
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
  // Peso de un archivo adjunto (la evidencia del cierre se lista con su tamaño).
  G.fmtPeso = function (n) {
    n = Number(n) || 0;
    if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB';
    if (n >= 1024) return Math.max(1, Math.round(n / 1024)) + ' KB';
    return n + ' bytes';
  };

  // Tres estados y nada más: «solo debe estar el estado pendiente, completado y cancelado».
  // "En proceso" y "Cerrada" se retiraron de toda la intranet; el servidor traduce los registros
  // antiguos al leerlos, así que aquí nunca llega otro valor.
  G.ESTADOS = { Pendiente: 'Pendiente', Completada: 'Completada', Cancelada: 'Cancelada' };
  G.ORDEN_ESTADOS = ['Pendiente', 'Completada', 'Cancelada'];
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
  // ellas (`{ 'Capacitación': { etiqueta: 'Aplicativo', opciones: [...] } }`), los tipos que ofrece
  // cada uno de esos aplicativos (`{ 'Aplicativos Fuji': ['Estación de trabajo', …] }`), los servicios,
  // los servicios que ofrece cada hospital (`{ 'HES': [...], 'HGR': [...] }`), las regiones médicas y a
  // qué región pertenece cada hospital (`{ 'HES': 'CMN La Raza' }`).
  G.CATALOGO = {
    actividades: G.TIPOS_ACTIVIDAD, subopciones: {}, subopcionesTipos: {}, servicios: [],
    serviciosPorHospital: {}, regiones: [], regionPorHospital: {}
  };

  // Subopciones configuradas para una actividad, o null si esa actividad no tiene ninguna.
  G.subopcionesDe = function (actividad) {
    if (!actividad) return null;
    var sub = G.CATALOGO.subopciones || {};
    return sub[actividad] || null;
  };

  // Tipos configurados para un aplicativo (el escalón siguiente: actividad → aplicativo → tipo).
  // Lista vacía —y entonces el campo no se muestra— si el Programador no escribió ninguno, si el
  // aplicativo se dejó vacío o si el servidor todavía no manda este catálogo.
  G.tiposDeSubopcion = function (subopcion) {
    var s = String(subopcion || '');
    if (!s) return [];
    var tipos = G.CATALOGO.subopcionesTipos || {};
    var claves = Object.keys(tipos);
    for (var i = 0; i < claves.length; i++) {
      if (claves[i].toUpperCase() !== s.toUpperCase()) continue;
      return (tipos[claves[i]] || []).filter(function (t) { return !!t; });
    }
    return [];
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

  // ---- regiones médicas ----
  // La región NO es un dato del registro: es solo una forma de agrupar hospitales (los agrupa quien
  // edita los catálogos en Programador). Por eso aquí se deriva siempre del hospital y nada se guarda
  // con ella: mover un hospital de región reordena lo ya capturado sin tocar un solo elemento.

  // Si el Programador ya configuró al menos una región. Sin ninguna, los formularios se ven y
  // funcionan exactamente igual que antes.
  G.hayRegiones = function () { return (G.CATALOGO.regiones || []).filter(function (r) { return !!r; }).length > 0; };

  // Región a la que pertenece un hospital ('' si está sin asignar o el catálogo no la conoce). La
  // comparación ignora mayúsculas: las claves las escribe el Programador.
  G.regionDe = function (hospital) {
    if (!hospital) return '';
    var mapa = G.CATALOGO.regionPorHospital || {};
    var h = String(hospital).toUpperCase();
    var claves = Object.keys(mapa);
    for (var i = 0; i < claves.length; i++)
      if (claves[i].toUpperCase() === h) return mapa[claves[i]] || '';
    return '';
  };

  // Hospitales de una región, en el orden del catálogo de hospitales. Con región vacía son todos,
  // así que un formulario que no conozca las regiones sigue mostrando lo de siempre. Los hospitales
  // que el Programador no asignó a ninguna región aparecen solo en esa lista «todas»: así nunca
  // quedan fuera del formulario por muy incompleta que esté la configuración.
  G.hospitalesDeRegion = function (region) {
    var todos = G.HOSPITALES || [];
    var r = String(region || '').trim().toUpperCase();
    if (!r) return todos.slice();
    return todos.filter(function (h) { return G.regionDe(h).toUpperCase() === r; });
  };

  // Opciones del desplegable Región: «Todas las regiones» (vacío) más las configuradas. No es un
  // campo obligatorio: solo filtra, y elegir nada deja ver todos los hospitales.
  //
  // Solo se ofrecen las regiones que de verdad traen hospitales al usuario: las que están en el
  // catálogo sin desglose, y las de otra región cuando el usuario tiene la suya asignada, dejarían el
  // desplegable de Hospital vacío (un callejón sin salida). Con el catálogo ya acotado por región que
  // manda `/api/permisos/mios`, `hospitalesDeRegion` contesta exactamente lo que se podría capturar.
  G.opcionesRegion = function (vacio) {
    var ops = [{ v: '', t: vacio === undefined ? 'Todas las regiones' : vacio }];
    return ops.concat((G.CATALOGO.regiones || []).filter(function (r) {
      return !!r && G.hospitalesDeRegion(r).length > 0;
    }).map(function (r) { return { v: r, t: r }; }));
  };
  G.campoRegion = function (valor, opts) {
    opts = opts || {};
    var mias = G.MIS_REGIONES || [];
    // La regla es una sola: **quien tiene exactamente una región asignada no elige** —el campo sale con
    // esa región escrita y sin desplegable (no hay nada más que elegir, y «Todas las regiones» lo único
    // que hacía era llevarlo a un hospital vacío). Con dos o más regiones el desplegable vuelve, con
    // «Todas las regiones» arriba y solo sus regiones debajo.
    //
    // `VE_TODAS_REGIONES` (root, el Administrador, quien no tiene región o el catálogo sin desglose) NO
    // bloquea: esa persona sí maneja varias regiones y necesita poder filtrar por una.
    var unaSola = !G.VE_TODAS_REGIONES && mias.length === 1;
    // Valor por defecto: el que traiga el formulario (al editar algo ya capturado se conserva su
    // región); si viene vacío y la persona tiene una sola, esa.
    var valorInicial = valor;
    if (!valorInicial && unaSola) valorInicial = mias[0];
    var opciones = unaSola ? [{ v: mias[0], t: mias[0] }] : G.opcionesRegion(opts.vacio);
    return {
      name: 'region', label: 'Región', tipo: 'select',
      value: valorInicial || '', opciones: opciones, disabled: unaSola
    };
  };

  // Título que se guarda en una Actividad o Incidente, en el orden de la cadena que se configura en
  // Programador: «Actividad — aplicativo — tipo de aplicativo — servicio». En un incidente la cadena
  // es más corta: «Título escrito — servicio». Las piezas vacías no se escriben, así que un registro
  // sin servicio queda igual que antes: «Capacitación — Aplicativos Fuji — Anatomía Patológica».
  G.tituloActividad = function (actividad, subopcion, tipo, servicio) {
    return [actividad, subopcion, tipo, servicio].filter(function (x) { return !!x; }).join(' — ');
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
  // campo: { name, label, tipo: text|textarea|date|number|select|checkbox|file, opciones: [{v,t}],
  //          value, required, min, max, step, hint, soloLectura, onCambio(valor, api) }
  //   · file: `accept` (qué deja elegir el navegador), `multiple` (varios archivos; sin ella el valor
  //     es un solo archivo, como siempre) y `miniaturas: false` para no pintar la caja de abajo.
  //     Con `required` y nada elegido, el envío avisa sin salir al servidor.
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
    var defs = {};
    // Campos de archivo: cada uno trae su caja de miniaturas debajo, para que lo que se adjuntó
    // se vea antes de enviar (la evidencia del cierre es una foto o un archivo, y si no se ve no
    // se puede comprobar). `vistas` guarda las URL temporales para liberarlas al cerrar.
    var cajas = {};
    var vistas = [];
    function pintaMiniaturas(inp, caja) {
      vistas.forEach(function (u) { URL.revokeObjectURL(u); });
      vistas = [];
      caja.innerHTML = '';
      var fs = inp.files || [];
      if (!fs.length) {
        var nada = document.createElement('span');
        nada.className = 'hint'; nada.textContent = 'Nada adjunto todavía.';
        caja.appendChild(nada);
        return;
      }
      for (var i = 0; i < fs.length; i++) {
        var f = fs[i];
        var esFoto = /^image\//i.test(f.type || '') || /\.(png|jpe?g|gif|bmp|webp|tiff?)$/i.test(f.name || '');
        var mini = document.createElement('div');
        mini.className = 'mini' + (esFoto ? ' mini-foto' : ' mini-archivo');
        if (esFoto) {
          var url = URL.createObjectURL(f); vistas.push(url);
          var img = document.createElement('img');
          img.src = url; img.alt = f.name;
          mini.appendChild(img);
        } else {
          var icono = document.createElement('span');
          icono.className = 'mini-icono';
          var ext = /\.([a-z0-9]+)$/i.exec(f.name || '');
          icono.textContent = ext ? ext[1].toUpperCase() : 'ARCHIVO';
          mini.appendChild(icono);
        }
        var nom = document.createElement('span');
        nom.className = 'mini-nombre'; nom.textContent = f.name;
        var meta = document.createElement('span');
        meta.className = 'mini-meta'; meta.textContent = G.fmtPeso(f.size);
        mini.appendChild(nom); mini.appendChild(meta);
        caja.appendChild(mini);
      }
    }
    (cfg.campos || []).forEach(function (c) {
      defs[c.name] = c;
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
        // `multiple: true` admite varios archivos (la evidencia del cierre pueden ser varias fotos
        // o una foto más el reporte). Sin esa bandera se comporta igual que antes: un solo archivo.
        if (c.multiple) inp.multiple = true;
        if (c.miniaturas !== false) {
          var caja = document.createElement('div');
          caja.className = 'miniaturas';
          cajas[c.name] = caja;
          inp.addEventListener('change', function () { pintaMiniaturas(inp, caja); });
        }
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
      if (c.soloLectura || c.disabled) { inp.disabled = true; }
      lab.appendChild(inp);
      form.appendChild(lab);
      if (cajas[c.name]) {
        pintaMiniaturas(inp, cajas[c.name]);
        form.appendChild(cajas[c.name]);
      }
      // El texto de ayuda de un archivo (qué se acepta y cuánto pesa) va pegado a su caja.
      if (c.hint && c.tipo === 'file') {
        var hint = document.createElement('span');
        hint.className = 'hint'; hint.textContent = c.hint;
        form.appendChild(hint);
      }
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
        if (inp.type === 'file') {
          var fs = Array.prototype.slice.call(inp.files || []);
          valores[k] = inp.multiple ? fs : (fs[0] || null);
          return;
        }
        valores[k] = inp.type === 'number' ? (inp.value === '' ? null : Number(inp.value)) : String(inp.value).trim();
      });
      // Un archivo obligatorio sin nada elegido se avisa aquí: el navegador ya no valida porque el
      // formulario se maneja a mano (noValidate), y dejarlo pasar solo daría un 400 del servidor.
      var sinArchivo = Object.keys(campos).filter(function (k) {
        var inp = campos[k], d = defs[k] || {};
        return inp.type === 'file' && !inp.disabled && d.required && !(inp.files && inp.files.length);
      });
      if (sinArchivo.length) {
        var dSin = defs[sinArchivo[0]];
        G.error(err, 'Adjunta ' + String(dSin.label || 'el archivo').toLowerCase() +
          ' antes de continuar. ' + (dSin.hint || ''));
        return;
      }
      bs.disabled = true;
      Promise.resolve().then(function () { return cfg.onSubmit(valores); })
        .then(function () { dlg.close(); })
        .catch(function (e) { G.error(err, e.message); })
        .finally(function () { bs.disabled = false; });
    });
    dlg.addEventListener('close', function () {
      vistas.forEach(function (u) { URL.revokeObjectURL(u); });
      vistas = [];
      dlg.remove(); if (cfg.onClose) cfg.onClose();
    });
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

    var abierto = item.estado === 'Pendiente';
    // Una cancelada se congela: «las canceladas ya no se pueden reabrir y solo queda el registro».
    // La tarjeta enseña la justificación, su historial y su evidencia, pero ningún botón de acción.
    var congelado = item.estado === 'Cancelada';
    var dueno = G.veTodo() || String(item.usuario || '').toLowerCase() === String(G.YO || '').toLowerCase();
    var creador = String(item.creadoPor || '').toLowerCase() === String(G.YO || '').toLowerCase();
    // La misma clave que en «+ Nuevo» separa los dos lados de la tarjeta: quien puede hacer la
    // asignación formal (el Administrador, o el rol al que se le dé `tickets.asignar`) mueve el
    // estatus, reasigna y entrega reportes. El Ingeniero responsable únicamente completa su elemento
    // —con su evidencia— y deja comentarios. Cancelar es de quien tiene `items.cancelar`.
    var puedeAsignar = G.tiene('tickets.asignar');
    var puedeCompletar = dueno && abierto && G.tiene('items.actualizarEstatus');

    var dlg = document.createElement('dialog');
    // `.detalle` además de `.ancho`: la tarjeta del elemento es la única que ocupa el ancho de verdad
    // de la pantalla (el calendario deja 1 100 px libres en un monitor de oficina normal).
    dlg.className = 'modal ancho detalle';
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

    // Aviso de reporte pendiente (solicitud del sistema al ingeniero). Solo a quien puede
    // entregarlo: en la tarjeta del Ingeniero ya no existe ese botón, y un aviso que manda a un
    // botón que no está es justo lo que hace parecer roto algo.
    if (item.reportePendiente && dueno && abierto && puedeAsignar) {
      var av = document.createElement('p');
      av.className = 'aviso-reporte';
      av.textContent = 'Reporte de estatus solicitado: tocaba el ' + G.fmtFecha(item.proximoReporte) +
        ' (cada ' + item.periodicidadDias + ' día(s)). Entrégalo con el botón "Entregar reporte".';
      cuerpo.appendChild(av);
    }

    // Recordatorio de seguimiento: «el registrar avance queda en comentarios y se debe recordar cada
    // 3 días». El servidor marca `leTocaReporte` cuando pasaron 3 días o más desde el último comentario
    // (o desde la fecha de inicio, si todavía no tiene ninguno); cada comentario nuevo reinicia la cuenta.
    // El aviso es NARANJA y dice solo «Favor de registrar avances en comentarios.».
    if (item.leTocaReporte && dueno && abierto) {
      var av3 = document.createElement('p');
      av3.className = 'aviso-avance';
      av3.textContent = 'Favor de registrar avances en comentarios.';
      cuerpo.appendChild(av3);
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
    // El porcentaje de avance ya no se usa: el seguimiento se escribe en los comentarios, así que la
    // tarjeta enseña cuándo fue el último en vez de una barra que nadie puede mover.
    meta('Último comentario', item.ultimoComentario ? G.fmtFechaHora(item.ultimoComentario) : '—');
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
    if (item.estado === 'Completada') {
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

    // Evidencia del trabajo: la foto o el archivo que se adjuntó al completar. La ven los dos
    // perfiles; lo que cambia es quién puede adjuntarla (la tarjeta la pide al cerrar).
    var zonaEv = document.createElement('div');
    zonaEv.className = 'evidencia';
    var titEv = document.createElement('b');
    titEv.textContent = 'Evidencia del trabajo';
    var cajaEv = document.createElement('div');
    cajaEv.className = 'miniaturas';
    zonaEv.appendChild(titEv); zonaEv.appendChild(cajaEv);
    cuerpo.appendChild(zonaEv);
    G.api('/api/actividades/' + item.id + '/evidencias').then(function (evs) {
      cajaEv.innerHTML = '';
      if (!evs.length) {
        var nada = document.createElement('span');
        nada.className = 'hint';
        nada.textContent = puedeCompletar
          ? 'Sin evidencia todavía: se adjunta (foto o archivo) al marcar como completada.'
          : 'Sin evidencia adjuntada.';
        cajaEv.appendChild(nada);
        return;
      }
      evs.forEach(function (e) {
        var url = '/api/evidencias/' + e.id;
        var foto = e.esImagen === true || /^image\//i.test(e.tipo || '');
        var mini = document.createElement('div');
        mini.className = 'mini ' + (foto ? 'mini-foto' : 'mini-archivo');
        if (foto) {
          var env = document.createElement('a');
          env.href = url; env.target = '_blank'; env.rel = 'noopener';
          env.title = 'Abrir ' + e.nombreArchivo + ' a tamaño completo';
          var img = document.createElement('img');
          img.src = url; img.alt = e.nombreArchivo;
          env.appendChild(img); mini.appendChild(env);
        } else {
          var icono = document.createElement('span');
          icono.className = 'mini-icono';
          var m = /\.([a-z0-9]+)$/i.exec(e.nombreArchivo || '');
          icono.textContent = m ? m[1].toUpperCase() : 'ARCHIVO';
          mini.appendChild(icono);
        }
        var nom = document.createElement('a');
        nom.className = 'mini-nombre'; nom.href = url; nom.target = '_blank'; nom.rel = 'noopener';
        nom.textContent = e.nombreArchivo;
        mini.appendChild(nom);
        var mt = document.createElement('span');
        mt.className = 'mini-meta';
        mt.textContent = (e.subidoPor || '—') + ' · ' + G.fmtFechaHora(e.subido) + ' · ' + G.fmtPeso(e.tamano);
        mini.appendChild(mt);
        cajaEv.appendChild(mini);
      });
    }).catch(function () { zonaEv.remove(); });

    // Pestañas Reportes / Historial. El historial de cambios **no** es seguimiento del elemento: lo
    // gobierna el permiso `tickets.historialCambios`, que el Ingeniero no trae por defecto. Sin él la
    // barra queda con su única pestaña, «Reportes de estatus», y nunca se pide /historial al servidor.
    var veHistorial = G.tiene('tickets.historialCambios');
    var tabs = document.createElement('div');
    tabs.className = 'tabs';
    var btnRep = document.createElement('button'); btnRep.type = 'button'; btnRep.textContent = 'Reportes de estatus'; btnRep.className = 'on';
    var btnHis = null;
    tabs.appendChild(btnRep);
    if (veHistorial) {
      btnHis = document.createElement('button'); btnHis.type = 'button'; btnHis.textContent = 'Historial de cambios';
      tabs.appendChild(btnHis);
    }
    cuerpo.appendChild(tabs);
    var zonaRep = document.createElement('div');
    var zonaHis = document.createElement('div'); zonaHis.hidden = true;
    cuerpo.appendChild(zonaRep);
    if (veHistorial) cuerpo.appendChild(zonaHis);
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
      btnRep.classList.add('on'); if (btnHis) btnHis.classList.remove('on');
      zonaRep.hidden = false; zonaHis.hidden = true;
      if (!cargados.rep) G.api('/api/actividades/' + item.id + '/reportes').then(function (l) { cargados.rep = true; pintarReportes(l); });
    });
    if (btnHis) btnHis.addEventListener('click', function () {
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

    // Cerrar el elemento con su evidencia: un solo envío multipart que adjunta la foto o el
    // archivo (uno o varios) y pasa a Completada. El informe es opcional; la evidencia es
    // obligatoria para cualquiera de los dos perfiles, así que ningún cierre queda sin nada
    // que enseñar. La misma ventana la reabre el alta del Ingeniero justo después de crear.
    function dialogoCompletar() { G.dialogoCierre(item, refresco); }

    // Lo único que puede escribir el Ingeniero además de cerrar: un comentario que queda en el
    // historial de cambios del elemento (el mismo camino de la observación del Administrador).
    function dialogoComentario() {
      G.formDialog({
        titulo: 'Agregar comentario',
        intro: item.titulo + ' — queda en el historial de cambios.',
        campos: [{ name: 'comentario', label: 'Comentario', tipo: 'textarea', required: true, maxlength: 1000, placeholder: 'Qué se hizo, qué hace falta, con quién se habló…' }],
        textoSubmit: 'Guardar comentario',
        onSubmit: function (v) {
          return G.api('/api/actividades/' + item.id + '/observaciones', { method: 'POST', body: JSON.stringify({ comentario: v.comentario }) })
            .then(refresco);
        }
      });
    }

    if (puedeCompletar) {
      boton('Completar', 'btn-success', dialogoCompletar);
    }
    // El comentario del responsable es ahora la manera de informar cómo va el trabajo (ya no hay
    // porcentaje de avance) y es lo que reinicia el recordatorio de 3 días.
    if (dueno && !puedeAsignar && !congelado) {
      boton('Agregar comentario', 'btn-secondary', dialogoComentario);
    }
    if (puedeAsignar && dueno && abierto && G.tiene('reportes.entregar')) {
      boton('Entregar reporte', 'btn-primary', function () {
        G.formDialog({
          titulo: 'Reporte de estatus',
          intro: item.titulo + (item.periodicidadDias > 0 ? ' · se entrega cada ' + item.periodicidadDias + ' día(s)' : ''),
          ancho: true,
          campos: [
            // El reporte ya no mueve el estatus (no existe "En proceso"): describe cómo va y queda en
            // el historial. El estado del elemento sigue siendo Pendiente hasta que se complete.
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
                estado: item.estado, avance: v.avance, descripcionAvance: v.descripcionAvance,
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
        G.tiene('items.cancelar') || G.tiene('items.eliminar') || G.tiene('items.reabrir')) {
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
              return G.api('/api/actividades/' + item.id + '/observaciones', { method: 'POST', body: JSON.stringify({ comentario: v.comentario }) })
                .then(refresco);
            }
          });
        });
      }
      // «El estado cancelado solo admin o quien tenga ese permiso la puede cambiar a dicho estado»:
      // cancelar exige justificación y deja el elemento congelado (ya no se reabre, solo queda el registro).
      if (abierto && G.tiene('items.cancelar')) {
        boton('Cancelar', 'btn-danger', function () {
          G.formDialog({
            titulo: 'Cancelar ' + item.tipo.toLowerCase(),
            intro: item.titulo + ' — la cancelación exige justificación y no tiene vuelta: el elemento ' +
              'queda solo como registro.',
            campos: [{ name: 'justificacion', label: 'Justificación', tipo: 'textarea', required: true, maxlength: 500, placeholder: 'Motivo por el que se cancela…' }],
            textoSubmit: 'Cancelar elemento', claseSubmit: 'btn-danger',
            onSubmit: function (v) {
              return G.api('/api/actividades/' + item.id + '/estado', { method: 'PUT', body: JSON.stringify({ estado: 'Cancelada', justificacion: v.justificacion }) })
                .then(refresco);
            }
          });
        });
      }
      // «Solo el administrador debe poder reabrir la actividad»: devuelve el elemento **Completada** a
      // Pendiente para que el ingeniero lo vuelva a completar. La evidencia y el informe anteriores se
      // conservan y se les pueden sumar archivos nuevos. Lo cancelado no se reabre.
      if (item.estado === 'Completada' && G.tiene('items.reabrir')) {
        boton('Reabrir', 'btn-secondary', function () {
          G.formDialog({
            titulo: 'Reabrir ' + item.tipo.toLowerCase(),
            intro: item.titulo + ' — vuelve a Pendiente con su evidencia intacta, para que se cierre otra vez.',
            campos: [{ name: 'motivo', label: 'Motivo de la reapertura', tipo: 'textarea', maxlength: 1000,
              placeholder: 'Qué faltó, qué se va a corregir…' }],
            textoSubmit: 'Reabrir',
            onSubmit: function (v) {
              return G.api('/api/actividades/' + item.id + '/reabrir', {
                method: 'POST', body: JSON.stringify({ motivo: v.motivo || '' })
              }).then(refresco);
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
  // ---- cierre con evidencia (informe opcional, evidencia obligatoria) ----
  // La ventana que pide «Informe del trabajo realizado (opcional) + evidencia (obligatorio)». La
  // usan dos caminos: el botón *Completar* de la tarjeta y el alta del Ingeniero, que la abre
  // justo después de crear el elemento. `despues` se llama una sola vez al terminar (guardado o
  // cancelado) para que la pantalla de atrás se refresque.
  /// Ventana de la evidencia (informe opcional + archivo obligatorio). Sirve para dos cosas:
  ///  · `item.id`   → el elemento ya existe: adjunta y lo marca **Completada**
  ///                   (`POST /api/actividades/{id}/completar`).
  ///  · `item.alta` → todavía no existe nada: la ventana hace el **alta completa** en un solo envío
  ///                   (`POST /api/actividades` en multipart), así la actividad del Ingeniero nace ya
  ///                   Completada y nunca queda Pendiente. Si se cierra sin enviar, no se guardó nada.
  G.dialogoCierre = function (item, despues) {
    var listo = false;
    function fin() { if (!listo) { listo = true; if (despues) despues(); } }
    var esAlta = item.alta === true;
    G.formDialog({
      titulo: esAlta
        ? 'Registrar y completar ' + String(item.tipo || 'actividad').toLowerCase()
        : 'Completar ' + String(item.tipo || '').toLowerCase(),
      intro: item.titulo + (esAlta
        ? ' — este envío da de alta la actividad y la deja completada: el informe es opcional y la ' +
          'evidencia (foto o archivo) es obligatoria. Si cierras la ventana sin enviar, no se guarda nada.'
        : ' — el informe es opcional; la evidencia (foto o archivo) es obligatoria. ' +
          'Sin evidencia no se marca como completada.'),
      ancho: true,
      campos: [
        { name: 'informe', label: 'Informe del trabajo realizado (opcional)', tipo: 'textarea', maxlength: 2000, placeholder: 'Qué se hizo, resultados, pendientes…' },
        {
          name: 'archivos', label: 'Evidencia (foto o archivo)', tipo: 'file', required: true, multiple: true,
          accept: 'image/*,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.log,.zip,.rar,.7z',
          hint: 'Hasta 5 archivos de 20 MB. Se listan abajo para que los veas antes de enviar.'
        }
      ],
      textoSubmit: esAlta ? 'Crear y completar' : 'Marcar como completada', claseSubmit: 'btn-success',
      onSubmit: function (v) {
        var datos = new FormData();
        datos.append('informe', v.informe || '');
        (v.archivos || []).forEach(function (f) { datos.append('archivos', f, f.name); });
        if (esAlta) {
          datos.append('tipo', item.tipo || 'Actividad');
          datos.append('hospital', item.hospital || '');
          datos.append('titulo', item.titulo || '');
          datos.append('descripcion', item.descripcion || '');
          datos.append('fecha', item.fecha || G.hoy());
          return G.subir('/api/actividades', datos).then(function (r) { fin(); return r; });
        }
        return G.subir('/api/actividades/' + item.id + '/completar', datos).then(function (r) { fin(); return r; });
      },
      onClose: fin
    });
  };

  G.abrirCrear = function (opts) {
    opts = opts || {};
    // G.TIPOS sigue incluyendo 'Ticket' para el Histórico, que consulta elementos ya registrados.
    var tiposCrear = G.TIPOS.filter(function (t) { return t !== 'Ticket'; });
    function mostrar(usuarios) {
      var tipoInicial = tiposCrear.indexOf(opts.tipo) >= 0 ? opts.tipo : 'Actividad';
      var arrancaEnActividad = tipoInicial === 'Actividad';
      // Quien puede hacer la asignación formal es quien decide prioridad, fechas e ingeniero. Los
      // demás dan de alta el elemento y esas tres decisiones las toma después el Administrador.
      var puedeAsignar = G.tiene('tickets.asignar');
      // «en el perfil del ing, quita la descripcion y despues de crear que arroje el apartado de
      // Informe del trabajo realizado (opcional)+evidencia (obligatorio)» + «directamente al crear una
      // actividad debe estar en completo, no puede quedar pendiente»: quien no hace la asignación
      // formal registra la actividad **sin Descripción** y, al guardar, pasa a la ventana de la
      // evidencia, que es la que de verdad la crea ya **Completada**. Los incidentes y el lado
      // administrativo siguen igual: nacen pendientes y conservan su Descripción.
      var cierraAlCrear = !puedeAsignar;
      // «El perfil de ingeniero solo puede subir las actividades del día de hoy»: para quien no hace la
      // asignación formal la fecha del alta es **siempre hoy**, aunque el diálogo se haya abierto desde
      // el «+» de otro día (en el calendario ese botón ya ni siquiera le sale). El Administrador conserva
      // la fecha del día elegido y puede escribirla. El servidor repite la regla: no basta esconder el botón.
      var fechaAlta = puedeAsignar ? (opts.fecha || G.hoy()) : G.hoy();
      var hospitalInicial = opts.hospital || '';
      // Región: el campo solo aparece si el Programador ya configuró regiones. No se envía ni se
      // guarda en el elemento —es un filtro del desplegable de hospital—, así que un registro hecho
      // con regiones se guarda idéntico que uno hecho sin ellas y las consultas por hospital no
      // cambian de nada.
      var hayRegiones = G.hayRegiones();
      var regionInicial = hospitalInicial ? G.regionDe(hospitalInicial) : '';
      // El servicio ya no es una lista fija: depende del hospital elegido (Programador guarda una
      // lista por hospital), así que al abrir se pinta con los del hospital que viene precargado.
      // Se ofrece tanto para Actividad como para Incidente: el servicio también identifica dónde
      // ocurrió una falla y debe quedar visible en el título con el que se lista el incidente.
      var campoHospital = G.campoHospital(hospitalInicial, { region: regionInicial });
      campoHospital.onCambio = function (valor, api) { ajustarServicio(api); };
      function serviciosDeHospital(api) { return G.serviciosDe(api.get('hospital')); }
      // Cambiar de región cambia los hospitales que se pueden elegir. Se limpia el elegido —un
      // hospital de otra región no tiene sentido aquí— y, como el servicio depende del hospital,
      // el servicio se vuelve a pintar vacío.
      function ajustarHospital(api) {
        api.opciones('hospital', G.opcionesHospital({ region: api.get('region') }));
        api.set('hospital', '');
        ajustarServicio(api);
      }
      // Reconstruye el desplegable: cambiar de hospital cambia las opciones y borra la elegida,
      // porque un servicio de otro hospital no tiene sentido en el título.
      function ajustarServicio(api) {
        var lista = serviciosDeHospital(api);
        api.opciones('servicio', [{ v: '', t: '(Sin servicio)' }].concat(
          lista.map(function (s) { return { v: s, t: s }; })));
        api.mostrar('servicio', lista.length > 0);
      }
      // Con el tipo Actividad la lista de actividades ocupa el lugar del título: la actividad
      // elegida es el título guardado y el detalle va en la descripción. Con Incidente regresa el
      // campo de texto libre; el servicio elegido se agrega al final del título al guardar.
      function ajustarTipo(valor, api) {
        var esActividad = valor === 'Actividad';
        api.mostrar('actividad', esActividad);
        api.mostrar('titulo', !esActividad);
        if (!esActividad) api.set('actividad', '');
        // Al volver de Incidente la lista viene vacía, así que la subopción se oculta hasta que se
        // elija una actividad que la tenga.
        ajustarActividad(esActividad ? api.get('actividad') : '', api);
        ajustarServicio(api);
      }
      // Algunas actividades llevan una segunda lista (los aplicativos de una Capacitación). La
      // etiqueta y las opciones las pone el catálogo que se edita en Programador, así que el campo
      // cambia de nombre según lo que se eligió arriba y desaparece cuando la actividad no tiene
      // subopciones. Es opcional: la primera opción dice «(Sin aplicativo)».
      function ajustarActividad(valor, api) {
        var sub = G.subopcionesDe(valor);
        if (sub) {
          var vacio = '(Sin ' + sub.etiqueta.charAt(0).toLowerCase() + sub.etiqueta.slice(1) + ')';
          api.opciones('subopcion', [{ v: '', t: vacio }].concat(
            (sub.opciones || []).map(function (o) { return { v: o, t: o }; })));
          api.etiqueta('subopcion', sub.etiqueta);
        } else {
          api.opciones('subopcion', [{ v: '', t: '' }]);
          api.set('subopcion', '');
        }
        api.mostrar('subopcion', !!sub);
        // Cambiar de actividad reconstruye el aplicativo y con eso se pierde el elegido, así que el
        // tipo —que cuelga del aplicativo— se vuelve a preguntar desde aquí.
        ajustarSubtipo(api.get('subopcion'), api);
      }
      // El tercer escalón de la cadena: los tipos que el Programador escribió para el aplicativo
      // elegido. Si ese aplicativo no tiene lista, el campo no aparece (igual que con Región y con
      // Servicio), y cuando la tiene es opcional: la primera opción dice «(Sin tipo de aplicativo)».
      function ajustarSubtipo(valor, api) {
        var tipos = G.tiposDeSubopcion(valor);
        if (tipos.length) {
          api.opciones('subtipo', [{ v: '', t: '(Sin tipo de aplicativo)' }].concat(
            tipos.map(function (t) { return { v: t, t: t }; })));
        } else {
          api.opciones('subtipo', [{ v: '', t: '' }]);
          api.set('subtipo', '');
        }
        api.mostrar('subtipo', tipos.length > 0);
      }
      var serviciosIniciales = G.serviciosDe(hospitalInicial);
      // El orden es el de la cadena que se configuró en Programador: región → hospital → servicio del
      // hospital → actividad → aplicativo (u otra lista) de esa actividad → tipo de ese aplicativo.
      // Por eso `servicio` va pegado a `hospital` y no al final del formulario.
      var campos = [
        { name: 'tipo', label: 'Tipo', tipo: 'select', value: tipoInicial, opciones: tiposCrear.map(function (t) { return { v: t, t: t }; }), onCambio: ajustarTipo },
        campoHospital,
        { name: 'servicio', label: 'Servicio', tipo: 'select', ocultoInicial: !serviciosIniciales.length,
          opciones: [{ v: '', t: '(Sin servicio)' }].concat(serviciosIniciales.map(function (s) { return { v: s, t: s }; })) },
        { name: 'actividad', label: 'Actividad', tipo: 'select', ocultoInicial: !arrancaEnActividad, onCambio: ajustarActividad,
          opciones: [{ v: '', t: '(Selecciona la actividad)' }].concat((G.CATALOGO.actividades || []).map(function (t) { return { v: t, t: t }; })) },
        // El aplicativo (o la lista que diga el catálogo de la actividad) es opcional: hay
        // capacitaciones o documentaciones que todavía no saben en qué aplicativo caen.
        { name: 'subopcion', label: 'Opción', tipo: 'select', ocultoInicial: true, onCambio: ajustarSubtipo },
        // Y su tipo, solo si el aplicativo elegido tiene lista propia en Programador.
        { name: 'subtipo', label: 'Tipo de aplicativo', tipo: 'select', ocultoInicial: true },
        { name: 'titulo', label: 'Título', required: true, maxlength: 120, ocultoInicial: arrancaEnActividad, placeholder: 'p. ej. Falla de red en el HGO' }
      ];
      // El Ingeniero no escribe Descripción: el detalle de lo hecho va en el informe (opcional) de
      // la ventana de evidencia que se abre al crear. Quien asigna sí conserva su Descripción.
      if (!cierraAlCrear) {
        campos.push({ name: 'descripcion', label: 'Descripción', tipo: 'textarea', maxlength: 1000 });
      }
      // El campo Región se inserta entre Tipo y Hospital: primero la región, después el hospital de
      // esa región y al final el servicio del hospital. Sin regiones configuradas no se agrega nada.
      if (hayRegiones) {
        var campoRegion = G.campoRegion(regionInicial);
        campoRegion.onCambio = function (valor, api) { ajustarHospital(api); };
        campos.splice(1, 0, campoRegion);
      }
      // Prioridad, fechas e ingeniero responsable son la parte de la asignación formal: solo se
      // ofrecen a quien tenga el permiso `tickets.asignar` (el Administrador por defecto, o el rol al
      // que se le conceda en «Roles y permisos»). El Ingeniero registra la actividad y el elemento
      // queda pendiente de que el Administrador le ponga prioridad, fecha límite e ingeniero.
      if (puedeAsignar) {
        campos.push({ name: 'prioridad', label: 'Prioridad', tipo: 'select', opciones: G.PRIORIDADES.map(function (p) { return { v: p, t: p }; }), value: 'Media' });
        campos.push({ name: 'fecha', label: 'Fecha de inicio', tipo: 'date', value: opts.fecha || G.hoy(), required: true });
      }
      if (puedeAsignar && G.usaFechaLimite()) {
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
        intro: (usuarios.length
          ? 'Elige la actividad de la lista (el detalle va en Descripción) y su ingeniero responsable. ' +
            'Los incidentes se escriben con su propio título y el servicio se agrega al final. Si necesitas un ticket, regístralo como incidente.'
          : 'Elige la actividad de la lista y quedará asignada a ti. ' +
            'Los incidentes se escriben con su propio título y el servicio se agrega al final. Si necesitas un ticket, regístralo como incidente. ' +
            'La fecha de inicio es el día del calendario desde el que abres esta ventana; la prioridad y la ' +
            'fecha límite las fija el Administrador al asignar.') +
          (cierraAlCrear
            ? ' Con una Actividad, al guardar se abre la ventana de la evidencia: el informe es opcional ' +
              'y la evidencia (foto o archivo) es obligatoria. Hasta que la envíes no se guarda nada, y ' +
              'la actividad nace ya completada. El Administrador es el único que puede reabrirla y ' +
              'ponerle fecha límite.'
            : '') +
          (hayRegiones ? ' La Región solo acomoda al Hospital: se guarda lo mismo de siempre.' : ''),
        campos: campos,
        textoSubmit: 'Crear',
        onSubmit: function (v) {
          // Una región recién creada todavía no tiene hospitales: si se elige, el desplegable de
          // Hospital queda solo con su aviso y el alta no podría terminar. Se dice aquí, en vez de
          // dejar que el servidor conteste «el hospital es obligatorio».
          if (hayRegiones && v.region && !G.hospitalesDeRegion(v.region).length)
            return Promise.reject(new Error(
              'La región «' + v.region + '» todavía no tiene hospitales asignados. Elija otra región ' +
              'o escríbalos primero en Programador.'));
          var esActividad = v.tipo === 'Actividad';
          var titulo;
          if (esActividad) {
            var sub = G.subopcionesDe(v.actividad);
            if (!v.actividad) return Promise.reject(new Error('Selecciona la actividad de la lista.'));
            // El aplicativo y su tipo ya no son obligatorios: si se dejan en «(Sin …)», el título
            // simplemente se arma sin esa pieza.
            // Todo lo elegido se guarda dentro del título, en el orden de la cadena:
            // «Capacitación — Aplicativos Fuji — Estación de trabajo — Anatomía Patológica».
            titulo = G.tituloActividad(v.actividad, sub ? v.subopcion : '', sub ? v.subtipo : '', v.servicio);
          } else {
            titulo = String(v.titulo || '').trim();
            if (!titulo) return Promise.reject(new Error('Escribe el título.'));
            // El incidente conserva el título escrito por la persona y agrega el servicio al final,
            // igual que las actividades. Así el servicio se ve en el calendario y en el histórico,
            // sin una columna nueva ni registros antiguos a medias.
            titulo = G.tituloActividad(titulo, '', '', v.servicio);
          }
          if (titulo.length > 120) {
            return Promise.reject(new Error(
              'El título queda muy largo (' + titulo.length + ' caracteres, máximo 120). ' +
              'Acorta el texto o deja el servicio vacío.'));
          }
          if (usuarios.length && !v.usuario) return Promise.reject(new Error('Elige el usuario asignado.'));
          // «Directamente al crear una actividad debe estar en completo, no puede quedar pendiente»:
          // la actividad del Ingeniero **no se guarda en este paso**. Todo lo escrito se lleva a la
          // ventana de la evidencia, que es la que hace el alta completa (multipart) y la deja
          // Completada. Si esa ventana se cierra sin enviar, no quedó nada en el servidor.
          if (cierraAlCrear && esActividad) {
            var alta = {
              alta: true, tipo: v.tipo, hospital: v.hospital, titulo: titulo,
              descripcion: v.descripcion || '',
              fecha: fechaAlta + 'T00:00:00'
            };
            // Se abre en cuanto esta ventana termine de cerrarse (formDialog cierra al resolver).
            setTimeout(function () { G.dialogoCierre(alta, opts.onClose); }, 0);
            return Promise.resolve(null);
          }
          // El resto (incidentes del Ingeniero y todo el lado administrativo) viaja en JSON y nace
          // Pendiente, como siempre.
          return G.api('/api/actividades', {
            method: 'POST',
            body: JSON.stringify({
              tipo: v.tipo, hospital: v.hospital, titulo: titulo, descripcion: v.descripcion,
              usuario: v.usuario || '', prioridad: puedeAsignar ? v.prioridad : 'Media',
              // Sin el campo a la vista, la fecha de inicio es **hoy** para quien no asigna (solo
              // registra lo del día); el Administrador usa la fecha que escribió en el campo, que al
              // abrir trae la del día elegido en el calendario. El alta nunca viaja sin fecha.
              fecha: (puedeAsignar ? v.fecha : fechaAlta) + 'T00:00:00',
              fechaLimite: puedeAsignar && v.fechaLimite ? v.fechaLimite + 'T00:00:00' : null,
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
