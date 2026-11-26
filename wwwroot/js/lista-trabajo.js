(() => {
    'use strict';

    const root = document.getElementById('lista-trabajo');
    if (!root) return;
    const byId = id => root.querySelector(`#${id}`);
    const API = '/api/dicom/worklist';
    const PAGE_SIZE = 50;
    const TEXTO_LIMITE = 'Resultados parciales: se alcanzó el límite de seguridad de la consulta (hasta 5000 entradas visibles). '
        + 'Aplica un filtro de modalidad o de fechas más específico; los filtros de nombre, ID, acceso, hora y procedimiento '
        + 'solo actúan sobre lo recibido, así que pueden quedar coincidencias fuera de la lista.';
    const localAe = byId('lt-ae-local');
    const modality = byId('lt-modality');
    const queryButton = byId('lt-query');
    const cancelButton = byId('lt-cancel-query');
    const sendButton = byId('lt-send');
    const dialog = byId('lt-confirm-dialog');
    const rowsElement = byId('lt-rows');
    const resultStatus = byId('lt-results-status');
    const sendStatus = byId('lt-send-status');
    const filterStatus = byId('lt-filter-status');
    const selectedSummary = byId('lt-selected-summary');
    const seleccionRadio = byId('lt-origen-seleccion');
    const origenRadios = [...root.querySelectorAll('input[name="lt-origen"]')];
    const manualFields = byId('lt-manual-fields');
    const confirmData = byId('lt-confirm-data');
    const confirmWarning = byId('lt-confirm-warning');
    const filters = {
        nombre: byId('lt-f-nombre'),
        id: byId('lt-f-id'),
        acceso: byId('lt-f-acceso'),
        desde: byId('lt-f-desde'),
        hasta: byId('lt-f-hasta'),
        horaDesde: byId('lt-f-hora-desde'),
        horaHasta: byId('lt-f-hora-hasta'),
        procedimiento: byId('lt-f-procedimiento')
    };
    const manual = { nombre: byId('lt-m-nombre'), id: byId('lt-m-id'), acceso: byId('lt-m-acceso') };
    const dataFields = byId('lt-data-fields');
    const dataStatus = byId('lt-data-status');
    const dataBoxes = [...root.querySelectorAll('#lt-data-fields input[type="checkbox"]')];
    // UNA SOLA conexión: la de arriba. Se usa para el C-FIND de la lista, para el Ping y para el C-STORE
    // de la prueba. Antes había un segundo bloque «destino Storage»; ya no existe.
    const connection = {
        host: byId('lt-mwl-host'),
        port: byId('lt-mwl-port'),
        ae: byId('lt-mwl-ae'),
        ping: byId('lt-mwl-ping'),
        status: byId('lt-mwl-ping-status'),
        error: byId('lt-mwl-error'),
        controller: null
    };
    const destinoNota = byId('lt-destino');
    const registro = {
        status: byId('lt-registro-status'),
        rows: byId('lt-registro-rows'),
        count: byId('lt-registro-count'),
        empty: byId('lt-registro-vacio'),
        refresh: byId('lt-registro-refresh'),
        tabla: byId('lt-registro-body')
    };
    let studies = [];          // recibido del nodo, sin filtrar
    let visible = [];          // lo que dejan ver los filtros
    let page = 0;
    let selected = null;       // entrada marcada para la prueba
    let queryController = null;
    let canSend = false;
    let sending = false;
    let pendingRequest = null;
    let localEditedOrUsed = false;
    let summary = '';          // resumen de la última consulta, sin filtros encima
    let summaryKind = '';
    let consultaParcial = false;
    let consultaVieja = false; // la lista recibida no corresponde a la modalidad/periodo actuales
    let filtrosPintados = ''; // estado de filtros que tiene pintado el repintado de la lista
    let envios = [];           // registro del servidor: lo que devolvió /envios, refrescado tras cada envío
    let registroCargando = false;

    class ApiError extends Error {
        constructor(message, status = 0) {
            super(message);
            this.status = status;
            this.auth = status === 401 || status === 403;
        }
    }

    function message(element, text = '', kind = '') {
        // Un campo oculto (la hora, que ahora escribe la hoja) puede no tener donde mostrar su
        // mensaje: no se rompe la pantalla por eso.
        if (!element) return;
        element.textContent = text;
        element.dataset.kind = kind;
        element.hidden = !text;
    }

    function origen() {
        const radio = origenRadios.find(item => item.checked);
        return radio ? radio.value : 'anonima';
    }

    // ---------- el menú de datos que regresan ----------
    // Una casilla por dato de la entrada. Lo marcado viaja tal cual lo devolvió el nodo; lo desmarcado se
    // queda con el valor técnico de la prueba, que es lo que llevaría una prueba anónima.
    const DATOS = [
        { clave: 'nombre', etiqueta: 'Nombre', defecto: 'PRUEBA^CIRCULO' },
        { clave: 'id', etiqueta: 'ID del paciente', defecto: 'PRUEBA-NO-CLINICA' },
        { clave: 'acceso', etiqueta: 'Número de acceso', defecto: 'sin número de acceso' },
        { clave: 'modalidad', etiqueta: 'Modalidad', defecto: 'OT' },
        { clave: 'procedimiento', etiqueta: 'Procedimiento', defecto: 'PRUEBA TECNICA - NO DIAGNOSTICO' },
        { clave: 'programado', etiqueta: 'Fecha y hora programadas', defecto: 'la fecha y la hora de ahora' },
        { clave: 'estudioUid', etiqueta: 'UID del estudio', defecto: 'un estudio nuevo' }
    ];
    const ETIQUETA = Object.fromEntries(DATOS.map(dato => [dato.clave, dato.etiqueta]));
    const PATRONES = {
        modalidad: /^[A-Z0-9_ ]{1,16}$/,
        acceso: /^[A-Za-z0-9 ._-]{1,16}$/,
        fecha: /^\d{8}$/,
        hora: /^\d{2}(\d{2}(\d{2}(\.\d{1,6})?)?)?$/,
        uid: /^[0-9]{1,64}(\.[0-9]{1,64})*$/
    };

    // El valor de la entrada, listo para el POST: vacío si el nodo no lo mandó o si no cabe en el tag
    // (mandar lo que no cabe sería un 400 del servidor y un envío rechazado sin motivo para el operador).
    // Los topes son los del servidor: nombre 200, ID 64, procedimiento 200 (VR LO, la imagen recorta a 64).
    const LARGOS = { nombre: 200, id: 64, procedimiento: 200 };
    function valorDe(clave) {
        const crudo = String(selected?.[campoDe(clave)] ?? '').trim();
        if (!crudo || crudo.length > (LARGOS[clave] ?? 64)) return '';
        switch (clave) {
            case 'nombre':
            case 'id':
            case 'procedimiento':
                return /[\u0000-\u001f\u007f\\]/.test(crudo) ? '' : crudo;
            case 'acceso': return PATRONES.acceso.test(crudo) ? crudo : '';
            case 'modalidad': return PATRONES.modalidad.test(crudo) ? crudo : '';
            case 'estudioUid': return PATRONES.uid.test(crudo) && crudo.length <= 64 ? crudo : '';
            default: return crudo;
        }
    }

    function campoDe(clave) {
        return {
            nombre: 'paciente', id: 'idPaciente', acceso: 'acceso', modalidad: 'modalidad',
            procedimiento: 'procedimiento', estudioUid: 'estudioUid'
        }[clave];
    }

    // La cita programada es una sola casilla para dos tags: se muestra y se manda por separado.
    function citaProgramada() {
        const fecha = PATRONES.fecha.test(String(selected?.fecha ?? '').trim()) ? String(selected.fecha).trim() : '';
        const hora = PATRONES.hora.test(String(selected?.hora ?? '').trim()) ? String(selected.hora).trim() : '';
        return { fecha, hora, texto: fecha || hora ? `${dicomDate(fecha)} ${dicomTime(hora)}`.trim() : '' };
    }

    function camposMarcados() {
        return dataBoxes.filter(box => box.checked).map(box => box.value);
    }

    function pintaDatosStatus() {
        if (origen() !== 'seleccion' || !selected) { message(dataStatus); return; }
        const marcados = camposMarcados();
        if (!marcados.length) {
            message(dataStatus, 'Sin ninguna casilla marcada: la prueba sale como anónima aunque la entrada esté elegida.', 'warning');
            return;
        }
        const queFaltan = marcados.filter(clave => clave === 'programado'
            ? !citaProgramada().texto : !valorDe(clave)).map(clave => ETIQUETA[clave]);
        message(dataStatus, queFaltan.length
            ? `El nodo no envió (o envió algo que no cabe en el tag): ${queFaltan.join(', ')}. Esos datos viajan con el valor por defecto de la prueba.`
            : '', queFaltan.length ? 'warning' : '');
    }

    function updateActions() {
        queryButton.disabled = Boolean(queryController) || sending;
        queryButton.textContent = queryController ? 'Consultando…' : 'Consultar';
        cancelButton.disabled = !queryController;
        sendButton.hidden = !canSend;
        sendButton.disabled = !canSend || sending || dialog.open;
        sendButton.textContent = sending ? 'Enviando…' : 'Enviar prueba';
        byId('lt-confirm-send').disabled = sending || !canSend;
        byId('lt-source-fields').disabled = sending;
        // Durante un envío la conexión NO se puede tocar: es también el destino de la prueba.
        for (const input of [localAe, connection.host, connection.port, connection.ae]) input.disabled = sending;
        connection.ping.disabled = Boolean(connection.controller) || sending;
        connection.ping.textContent = connection.controller ? 'Probando…' : 'Ping';
        registro.refresh.disabled = registroCargando || sending;
        registro.refresh.textContent = registroCargando ? 'Leyendo…' : 'Actualizar';
    }

    function authMessage(status) {
        if (status === 401) return 'La sesión ha caducado o no está iniciada. Vuelve a iniciar sesión desde el inicio.';
        return 'No tienes permiso para esta operación. Si tu acceso cambió, vuelve a iniciar sesión.';
    }

    function revokeSendPermission(error) {
        if (!error.auth) return;
        canSend = false;
        if (dialog.open) dialog.close();
        message(byId('lt-send-permission'), authMessage(error.status));
        updateActions();
    }

    async function request(path, body, signal) {
        const headers = { Accept: 'application/json' };
        const options = { credentials: 'same-origin', cache: 'no-store', headers, signal };
        if (body !== undefined) {
            const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
            if (!token) throw new ApiError('No se encontró el token de seguridad. Recarga la página antes de continuar.', 400);
            options.method = 'POST';
            headers['Content-Type'] = 'application/json';
            headers.RequestVerificationToken = token;
            options.body = JSON.stringify(body);
        }
        const response = await fetch(`${API}/${path}`, options);
        if (response.redirected || response.status === 401 || response.status === 403) {
            const status = response.status === 403 ? 403 : 401;
            throw new ApiError(authMessage(status), status);
        }
        let data = null;
        if ((response.headers.get('Content-Type') || '').includes('json')) {
            try { data = await response.json(); }
            catch (error) { if (error.name === 'AbortError') throw error; }
        }
        if (!response.ok) {
            const details = [];
            if (typeof data?.title === 'string') details.push(data.title);
            if (typeof data?.detail === 'string') details.push(data.detail);
            if (data?.errors && typeof data.errors === 'object') {
                for (const errors of Object.values(data.errors)) {
                    for (const item of (Array.isArray(errors) ? errors : [errors])) {
                        if (typeof item === 'string') details.push(item);
                    }
                }
            }
            const fallback = response.status === 400
                ? 'Solicitud no válida. Revisa los campos; si persiste, recarga la página para renovar el token de seguridad.'
                : 'No se pudo completar la operación con el nodo DICOM.';
            throw new ApiError(details.length ? details.join(' ') : fallback, response.status);
        }
        if (!data || typeof data !== 'object' || Array.isArray(data)) {
            throw new ApiError('El servicio devolvió una respuesta no válida.', response.status);
        }
        return data;
    }

    function fieldError(input, text = '') {
        input.setCustomValidity(text);
        input.setAttribute('aria-invalid', text ? 'true' : 'false');
        message(byId(`${input.id}-error`), text);
        return !text;
    }

    function validAe(value) {
        // VR AE: ASCII imprimible, sin separador DICOM ni valor vacío; máximo 16.
        return value.length > 0 && value.length <= 16 && /^[\x20-\x7e]+$/.test(value) && !value.includes('\\');
    }

    function getConnection() {
        const fields = connection;
        const conexion = {
            host: fields.host.value.trim(),
            puerto: Number(fields.port.value),
            aeRemoto: fields.ae.value.trim(),
            aeLocal: localAe.value.trim()
        };
        const checks = [
            [fields.host, conexion.host && conexion.host.length <= 253 && !/[\s/\\?#]/.test(conexion.host)
                ? '' : 'Escribe un host o IP, sin URL, espacios ni ruta.'],
            [fields.port, fields.port.value.trim() && Number.isInteger(conexion.puerto) && conexion.puerto >= 1 && conexion.puerto <= 65535
                ? '' : 'El puerto debe ser un entero entre 1 y 65535.'],
            [fields.ae, validAe(conexion.aeRemoto) && fields.ae.value.length <= 16
                ? '' : 'Escribe un AE remoto de 1 a 16 caracteres ASCII, sin barra inversa.'],
            [localAe, validAe(conexion.aeLocal) && localAe.value.length <= 16
                ? '' : 'Escribe un AE local de 1 a 16 caracteres ASCII, sin barra inversa.']
        ];
        for (const [input, text] of checks) fieldError(input, text);
        const invalid = checks.find(([, text]) => text);
        if (invalid) {
            invalid[0].focus();
            invalid[0].reportValidity();
            return null;
        }
        localEditedOrUsed = true;
        return conexion;
    }

    function invalidatePing() {
        connection.controller?.abort();
        connection.controller = null;
        message(connection.status);
        message(connection.error);
        updateActions();
    }

    // El destino de la prueba es la conexión de arriba: se muestra siempre, para que no haya duda.
    function pintaDestino() {
        const host = connection.host.value.trim();
        const ae = connection.ae.value.trim();
        const puerto = connection.port.value.trim();
        if (!host || !ae || !puerto) {
            message(destinoNota, 'Destino: todavía no hay una conexión válida escrita arriba.', 'warning');
            return;
        }
        message(destinoNota, `Destino de la prueba: ${ae}@${host}:${puerto} · AE local ${localAe.value.trim() || '—'}`);
    }

    // ---------- registro de pruebas enviadas ----------
    // Ya no es una ventana de esta pestaña: cada envío confirmado queda apuntado en el servidor
    // (App_Data/dicom-pruebas.jsonl) y la pantalla lo lee de ahí, así que sigue vivo al recargar.
    // Cada renglón trae lo que respondió el propio nodo cuando se le preguntó por el estudio enviado.

    const KIND_ENVIO = { enviada: 'success', advertencia: 'warning', incierta: 'warning', fallida: 'error' };

    function horaLarga(valor) {
        const fecha = new Date(valor);
        if (Number.isNaN(fecha.getTime())) return String(valor ?? '');
        const dos = n => String(n).padStart(2, '0');
        return `${dos(fecha.getDate())}/${dos(fecha.getMonth() + 1)}/${fecha.getFullYear()} `
            + `${dos(fecha.getHours())}:${dos(fecha.getMinutes())}:${dos(fecha.getSeconds())}`;
    }

    function textoLista(partes) {
        return partes.filter(part => typeof part === 'string' && part.trim()).join(' · ');
    }

    // Una línea por estudio que el destino devolvió en la verificación, más una comparación honesta con
    // lo que se envió: si el acceso que reporta el PACS no es el que salió en la imagen, ahí se ve.
    function lineaEstudioPacs(estudio, tags) {
        const linea = document.createElement('span');
        linea.textContent = textoLista([
            estudio.estudioUid ? `UID ${estudio.estudioUid}` : 'UID sin dato',
            estudio.acceso ? `acceso ${estudio.acceso}` : 'sin número de acceso',
            estudio.fecha ? `fecha ${dicomDate(estudio.fecha)}` : '',
            estudio.hora ? `hora ${dicomTime(estudio.hora)}` : '',
            estudio.modalidades ? `modalidades ${estudio.modalidades}` : '',
            estudio.descripcion ? `descripción ${estudio.descripcion}` : '',
            estudio.instancias ? `${estudio.instancias} instancia(s) en el estudio` : 'el nodo no devolvió el conteo de instancias'
        ]);
        if (tags?.estudioUid && estudio.estudioUid && estudio.estudioUid !== tags.estudioUid) {
            linea.textContent += ' · no es el UID enviado';
        }
        if (tags?.acceso && estudio.acceso && estudio.acceso !== tags.acceso) {
            linea.textContent += ' · el acceso del nodo difiere del enviado';
        }
        return linea;
    }

    function pintaRegistro() {
        const fragment = document.createDocumentFragment();
        for (const envio of envios) {
            const tr = document.createElement('tr');
            for (const texto of [
                horaLarga(envio.momento),
                envio.usuario || '—',
                textoLista([`${envio.aeRemoto || '—'}@${envio.host}:${envio.puerto}`, `AE local ${envio.aeLocal}`])
            ]) {
                const celda = document.createElement('td');
                celda.textContent = texto;
                tr.appendChild(celda);
            }
            const resultado = document.createElement('td');
            resultado.textContent = `${envio.estado || 'sin estado'} · ${envio.mensaje || 'el servicio no dio mensaje'}`;
            resultado.dataset.kind = KIND_ENVIO[envio.estado] ?? '';
            tr.appendChild(resultado);

            const detalle = document.createElement('td');
            const verificacion = envio.verificacion || {};
            const bloque = document.createElement('p');
            bloque.className = 'lt-registro-pacs';
            const cabecera = document.createElement('strong');
            cabecera.textContent = verificacion.soportada ? 'El nodo, al preguntarle por ese estudio:'
                : 'Verificación en el nodo: no se pudo hacer.';
            bloque.append(cabecera, document.createTextNode(' ' + (verificacion.mensaje || 'Sin respuesta.')));
            for (const estudio of (Array.isArray(verificacion.estudios) ? verificacion.estudios : []))
                bloque.appendChild(lineaEstudioPacs(estudio, envio.tags));
            detalle.appendChild(bloque);

            const datos = document.createElement('div');
            datos.className = 'lt-registro-datos';
            for (const campo of (Array.isArray(envio.campos) ? envio.campos : [])) {
                const linea = document.createElement('span');
                linea.className = campo.viajo ? '' : 'lt-registro-no';
                linea.textContent = campo.viajo
                    ? `${campo.etiqueta}: ${campo.valor}${campo.motivo ? ` (${campo.motivo})` : ''}`
                    : `${campo.etiqueta}: no viaja · ${campo.motivo}`;
                datos.appendChild(linea);
            }
            const uids = document.createElement('span');
            uids.textContent = textoLista([
                `UID de serie: ${envio.tags?.serieUid || '—'}`,
                `UID de imagen: ${envio.tags?.instanciaUid || '—'}`
            ]);
            datos.appendChild(uids);
            detalle.appendChild(datos);
            tr.appendChild(detalle);
            fragment.appendChild(tr);
        }
        registro.rows.replaceChildren(fragment);
        registro.empty.hidden = envios.length > 0;
        registro.count.textContent = `${envios.length} ${envios.length === 1 ? 'envío registrado' : 'envíos registrados'}`;
    }

    async function cargaRegistro() {
        if (registroCargando) return;
        registroCargando = true;
        updateActions();
        message(registro.status);
        try {
            const data = await request('envios');
            if (!Array.isArray(data.envios)) throw new ApiError('El servicio devolvió un registro no válido.');
            envios = data.envios;
            pintaRegistro();
        } catch (error) {
            envios = [];
            pintaRegistro();
            message(registro.status, error.auth ? error.message
                : `No se pudo leer el registro de pruebas enviadas: ${error instanceof ApiError ? error.message : 'no hay respuesta del servicio.'}`,
                'error');
        } finally {
            registroCargando = false;
            updateActions();
        }
    }

    // ---------- filtros ----------

    function textoBuscado(value) {
        // Sin acentos y en minúsculas: «Gómez» encuentra a «GOMEZ».
        return String(value ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
    }

    function fechaClave(value) {
        // '2026-11-05' (campo de fecha) y '20261105' (DA de DICOM) → '20261105'; '' si no es fecha completa.
        const digits = String(value ?? '').replace(/\D/g, '');
        return digits.length === 8 ? digits : '';
    }

    function horaClave(value) {
        // '09:30' (campo de hora) → minutos del día; null si está vacío o incompleto.
        const text = String(value ?? '').trim();
        if (!text) return null;
        const parts = text.split(':');
        if (parts.length < 2) return null;
        const hour = Number(parts[0]);
        const minute = Number(parts[1]);
        if (!Number.isInteger(hour) || !Number.isInteger(minute) || hour > 23 || minute > 59) return null;
        return hour * 60 + minute;
    }

    function ventanaHora(value) {
        // TM del nodo → intervalo aprovechable en minutos; null si el nodo no mandó hora legable.
        if (typeof value !== 'string' || !/^\d{2}(\d{2}(\d{2}(\.\d{1,6})?)?)?$/.test(value)) return null;
        const hour = Number(value.slice(0, 2));
        if (hour > 23) return null;
        if (value.length === 2) return { from: hour * 60, to: hour * 60 + 59 };
        const minute = Number(value.slice(2, 4));
        if (minute > 59) return null;
        return { from: hour * 60 + minute, to: hour * 60 + minute };
    }

    function filtrosActivos() {
        return Object.values(filters).some(field => String(field.value ?? '').trim() !== '')
            || String(modality.value ?? '').trim() !== '';
    }

    // Estado completo de los filtros. Sirve para no repintar la tabla cuando algo no cambió nada,
    // como el change que el navegador lanza al salir de un campo que ya estaba filtrado:
    // repintar sustituiría la fila bajo el cursor y el clic siguiente no seleccionaría nada.
    function estadoFiltros() {
        return [modality.value, ...Object.values(filters).map(campo => campo.value)].join('\u0000');
    }

    function coincide(study) {
        const nombre = textoBuscado(filters.nombre.value);
        if (nombre && !textoBuscado(study.paciente).includes(nombre)) return false;
        const id = textoBuscado(filters.id.value);
        if (id && !textoBuscado(study.idPaciente).includes(id)) return false;
        const acceso = textoBuscado(filters.acceso.value);
        if (acceso && !textoBuscado(study.acceso).includes(acceso)) return false;
        const procedimiento = textoBuscado(filters.procedimiento.value);
        if (procedimiento && !textoBuscado(study.procedimiento).includes(procedimiento)) return false;
        const modalidad = String(modality.value).trim().toUpperCase();
        if (modalidad && textoBuscado(study.modalidad) !== textoBuscado(modalidad)) return false;
        const desde = fechaClave(filters.desde.value);
        const hasta = fechaClave(filters.hasta.value);
        if (desde || hasta) {
            const fecha = fechaClave(study.fecha);
            if (!fecha) return false; // sin fecha legable no se puede afirmar que esté dentro
            if (desde && fecha < desde) return false;
            if (hasta && fecha > hasta) return false;
        }
        const horaDesde = horaClave(filters.horaDesde.value);
        const horaHasta = horaClave(filters.horaHasta.value);
        if (horaDesde !== null || horaHasta !== null) {
            const ventana = ventanaHora(study.hora);
            if (!ventana) return false; // sin hora legable tampoco
            if (horaDesde !== null && ventana.to < horaDesde) return false;
            if (horaHasta !== null && ventana.from > horaHasta) return false;
        }
        return true;
    }

    function pintaFiltros() {
        visible = studies.filter(coincide);
        page = 0;
        filtrosPintados = estadoFiltros();
        if (!studies.length) {
            message(filterStatus);
            message(byId('lt-page-status'));
            byId('lt-results').hidden = true;
            rowsElement.replaceChildren();
            return;
        }
        // El aviso se muestra siempre que los filtros oculten entradas, incluidas las de una
        // consulta recién hecha: hay servidores que ignoran la modalidad y el periodo del C-FIND.
        if (filtrosActivos() && visible.length !== studies.length) {
            message(filterStatus, visible.length === 0
                ? `Ninguna de las ${studies.length} ${studies.length === 1 ? 'entrada recibida' : 'entradas recibidas'} coincide con los filtros.`
                : `${visible.length} de ${studies.length} ${studies.length === 1 ? 'entrada recibida' : 'entradas recibidas'} coinciden con los filtros.`
                    + (consultaVieja ? ' Pulsa Consultar para pedir al nodo esta modalidad y este periodo.' : ''));
        } else {
            message(filterStatus, consultaVieja
                ? 'Pulsa Consultar para pedir al nodo esta modalidad y este periodo.' : '');
        }
        renderPage();
    }

    // ---------- resultados ----------

    function clearResults(text) {
        studies = [];
        visible = [];
        page = 0;
        selected = null;
        summary = '';
        summaryKind = '';
        consultaParcial = false;
        consultaVieja = false;
        rowsElement.replaceChildren();
        byId('lt-results').hidden = true;
        message(byId('lt-page-status'));
        message(byId('lt-missing-status'));
        message(byId('lt-not-scheduled'));
        message(byId('lt-limited'));
        message(filterStatus);
        byId('lt-prev').disabled = true;
        byId('lt-next').disabled = true;
        message(resultStatus, text);
        pintaSeleccion();
    }

    function cancelQuery(text) {
        queryController?.abort();
        queryController = null;
        byId('lt-results').setAttribute('aria-busy', 'false');
        clearResults(text);
        updateActions();
    }

    function connectionChanged() {
        invalidatePing();
        pintaDestino();
        if (sending) return;   // a mitad de un envío la conexión está deshabilitada; nada que invalidar
        cancelQuery('Conexión modificada. Pulsa Consultar para obtener resultados de este nodo.');
        message(sendStatus);
        pendingRequest = null;
        if (dialog.open) dialog.close();
    }

    function dicomDate(value) {
        if (typeof value !== 'string' || !/^\d{8}$/.test(value)) return 'Fecha no disponible';
        const year = Number(value.slice(0, 4));
        const month = Number(value.slice(4, 6));
        const day = Number(value.slice(6, 8));
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        if (!year || month < 1 || month > 12 || day < 1 || day > days[month - 1]) return 'Fecha no válida';
        return `${value.slice(6, 8)}/${value.slice(4, 6)}/${value.slice(0, 4)}`;
    }

    function dicomTime(value) {
        if (typeof value !== 'string' || !/^\d{2}(?:\d{2}(?:\d{2}(?:\.\d{1,6})?)?)?$/.test(value)) return 'Hora no disponible';
        const hour = Number(value.slice(0, 2));
        const minute = Number(value.slice(2, 4));
        const second = Number(value.slice(4, 6));
        if (hour > 23 || minute > 59 || second > 60) return 'Hora no válida';
        // TM permite precisión reducida: no inventar minutos ni segundos que el nodo no envió.
        if (value.length === 2) return `${value} h`;
        if (value.length === 4) return `${value.slice(0, 2)}:${value.slice(2, 4)}`;
        return `${value.slice(0, 2)}:${value.slice(2, 4)}:${value.slice(4, 6)}`;
    }

    function renderPage() {
        const start = page * PAGE_SIZE;
        const end = Math.min(start + PAGE_SIZE, visible.length);
        const fragment = document.createDocumentFragment();
        visible.slice(start, end).forEach((study, offset) => {
            const index = start + offset;
            const row = document.createElement('tr');
            if (study === selected) row.className = 'lt-selected';
            const celda = document.createElement('td');
            const radio = document.createElement('input');
            radio.type = 'radio';
            radio.name = 'lt-pick';
            radio.value = String(index);
            radio.checked = study === selected;
            radio.setAttribute('aria-label', 'Elegir esta entrada para la prueba de envío');
            const etiqueta = document.createElement('span');
            etiqueta.className = 'lt-sr-only';
            etiqueta.textContent = `${study.paciente || 'paciente sin nombre'} · ${study.idPaciente || 'ID sin dato'}`;
            celda.append(radio, etiqueta);
            row.appendChild(celda);
            const values = [
                `${dicomDate(study.fecha)}\n${dicomTime(study.hora)}`,
                study.paciente, study.idPaciente, study.acceso,
                study.modalidad, study.procedimiento, study.estacion
            ];
            for (const value of values) {
                const cell = document.createElement('td');
                cell.textContent = typeof value === 'string' && value.trim() ? value : '—';
                row.appendChild(cell);
            }
            row.addEventListener('click', event => {
                if (event.target === radio) return;
                radio.checked = true;
                radio.dispatchEvent(new Event('change', { bubbles: true }));
            });
            fragment.appendChild(row);
        });
        rowsElement.replaceChildren(fragment);
        if (visible.length === 0) {
            // Los filtros del navegador no borran ninguna entrada recibida: si ninguna coincide,
            // la tabla se queda en su sitio con un renglón que lo dice, en lugar de desaparecer
            // y parecer que el nodo no mandó nada.
            const vacia = document.createElement('tr');
            vacia.className = 'lt-row-vacia';
            const celda = document.createElement('td');
            celda.colSpan = 8;
            celda.textContent = `Ninguna de las ${studies.length} ${studies.length === 1 ? 'entrada recibida'
                : 'entradas recibidas'} coincide con los filtros. Siguen aquí: quita el filtro para volver a verlas.`;
            vacia.appendChild(celda);
            rowsElement.replaceChildren(vacia);
        }
        byId('lt-results').hidden = false;
        const matiz = filtrosActivos() && visible.length !== studies.length
            ? ` · filtradas de ${studies.length} recibidas` : '';
        message(byId('lt-page-status'), visible.length
            ? `${start + 1}–${end} de ${visible.length} mostradas${matiz} · Página ${page + 1} de ${Math.ceil(visible.length / PAGE_SIZE)}`
            : '');
        byId('lt-prev').disabled = page === 0;
        byId('lt-next').disabled = end >= visible.length;
        byId('lt-table-scroll').scrollTop = 0;
    }

    function pintaSeleccion() {
        seleccionRadio.disabled = !selected;
        if (selected) {
            const uid = String(selected.estudioUid ?? '').trim();
            selectedSummary.textContent = [
                'Entrada elegida:',
                selected.paciente || 'paciente sin nombre',
                `ID ${selected.idPaciente || '—'}`,
                `acceso ${selected.acceso || '—'}`,
                selected.modalidad || 'modalidad sin código',
                `${dicomDate(selected.fecha)} ${dicomTime(selected.hora)}`,
                uid ? `UID programado ${uid} · la prueba se manda dentro de ese estudio`
                    : 'El nodo no envió el UID del estudio: la prueba creará un estudio nuevo.'
            ].join(' · ');
            selectedSummary.hidden = false;
        } else {
            if (origen() === 'seleccion') origenRadios[0].checked = true;
            selectedSummary.textContent = 'Ninguna entrada seleccionada. Marca la casilla de una fila de la lista para enviarle la prueba.';
            selectedSummary.hidden = false;
        }
        manualFields.hidden = origen() !== 'manual';
        dataFields.hidden = origen() !== 'seleccion';
        pintaDatosStatus();
        updateActions();
    }

    async function search(event) {
        event.preventDefault();
        if (queryController) return;
        const conexion = getConnection();
        const filtroModalidad = modality.value.trim().toUpperCase();
        const modalityValida = /^[A-Z0-9_ ]{0,16}$/.test(filtroModalidad) && modality.value.length <= 16;
        fieldError(modality, modalityValida ? '' : 'Usa hasta 16 caracteres: letras mayúsculas, números, espacios o guion bajo.');
        const desde = fechaClave(filters.desde.value);
        const hasta = fechaClave(filters.hasta.value);
        const desdeValida = !filters.desde.value || desde.length === 8;
        const hastaValida = !filters.hasta.value || hasta.length === 8;
        fieldError(filters.desde, desdeValida ? '' : 'Escribe una fecha completa en el calendario.');
        fieldError(filters.hasta, hastaValida ? '' : 'Escribe una fecha completa en el calendario.');
        const fechasAlReves = Boolean(desde) && Boolean(hasta) && desde > hasta;
        if (fechasAlReves) fieldError(filters.desde, 'La fecha desde no puede ser posterior a la fecha hasta.');
        // La hora no se valida: la escribe la hoja de horas y sus dos campos ocultos solo
        // pueden traer 'HH:MM' o estar vacíos. Además no viaja al nodo, solo recorta lo recibido.
        const campos = [modality, filters.desde, filters.hasta];
        if (!conexion || !modalityValida || !desdeValida || !hastaValida || fechasAlReves) {
            const mala = campos.find(input => input.getAttribute('aria-invalid') === 'true');
            if (mala) { mala.focus(); mala.reportValidity(); }
            cancelQuery('Revisa los campos antes de consultar.');
            return;
        }
        const cuerpo = { conexion, modalidad: filtroModalidad };
        if (desde) cuerpo.fechaDesde = desde;
        if (hasta) cuerpo.fechaHasta = hasta;
        const controller = new AbortController();
        queryController = controller;
        clearResults('Consultando entradas programadas con la modalidad y el periodo elegidos…');
        byId('lt-results').setAttribute('aria-busy', 'true');
        updateActions();
        try {
            const data = await request('buscar', cuerpo, controller.signal);
            if (queryController !== controller) return;
            if (!Array.isArray(data.estudios) || data.estudios.some(study => !study || typeof study !== 'object')) {
                throw new ApiError('El servicio devolvió una lista de trabajo no válida.');
            }
            // El backend aplica el filtro estricto SCHEDULED y contabiliza las omisiones.
            studies = data.estudios;
            consultaParcial = data.limitado === true;
            const recuento = studies.length
                ? `${studies.length} ${studies.length === 1 ? 'entrada programada recibida' : 'entradas programadas recibidas'}.`
                : 'No se recibieron entradas confirmadas como programadas para esta consulta.';
            summary = `${consultaParcial ? 'Consulta parcial. ' : ''}${recuento}`;
            summaryKind = consultaParcial ? 'warning' : '';
            message(resultStatus, summary, summaryKind);
            if (data.omitidosSinEstado > 0) {
                message(byId('lt-missing-status'), data.omitidosSinEstado === 1
                    ? 'Se omitió 1 entrada sin estado; no se puede confirmar como programada.'
                    : `Se omitieron ${data.omitidosSinEstado} entradas sin estado; no se pueden confirmar como programadas.`, 'warning');
            }
            if (data.omitidosNoProgramados > 0) {
                message(byId('lt-not-scheduled'), data.omitidosNoProgramados === 1
                    ? 'Se excluyó 1 entrada que ya no está programada.'
                    : `Se excluyeron ${data.omitidosNoProgramados} entradas que ya no están programadas.`);
            }
            if (consultaParcial) message(byId('lt-limited'), TEXTO_LIMITE, 'warning');
            pintaFiltros();
        } catch (error) {
            if (queryController !== controller || error.name === 'AbortError') return;
            clearResults('');
            message(resultStatus, `No se pudo consultar. ${error instanceof ApiError ? error.message : 'No se pudo contactar con el servicio. Comprueba la conexión.'}`, 'error');
            revokeSendPermission(error);
        } finally {
            if (queryController === controller) {
                queryController = null;
                byId('lt-results').setAttribute('aria-busy', 'false');
                updateActions();
            }
        }
    }

    // ---------- envío de la prueba ----------

    function textoSinSeparador(valor) {
        return valor.length <= 200 && !/[\\\u0000-\u001f\u007f]/.test(valor);
    }

    // Devuelve los datos listos para el POST, o un código con lo que falta.
    function datosDePrueba() {
        const elegido = origen();
        if (elegido === 'anonima') return { origen: 'anonima' };
        if (elegido === 'seleccion') {
            if (!selected) return 'sin-seleccion';
            const campos = camposMarcados();
            const cita = citaProgramada();
            // Cada casilla marcada manda su valor tal cual lo devolvió el nodo. Las desmarcadas se envían
            // igual en la lista `campos`: el servidor decide el valor técnico con el que sale el tag.
            const datos = {
                origen: 'seleccion',
                campos,
                paciente: valorDe('nombre'),
                idPaciente: valorDe('id'),
                acceso: valorDe('acceso'),
                modalidad: valorDe('modalidad'),
                procedimiento: valorDe('procedimiento'),
                fechaProgramada: cita.fecha,
                horaProgramada: cita.hora,
                estudioUid: valorDe('estudioUid')
            };
            // La exigencia de nombre e ID solo aplica a lo que el operador marcó.
            if (campos.includes('nombre') && !datos.paciente) return 'seleccion-incompleta';
            if (campos.includes('id') && !datos.idPaciente) return 'seleccion-incompleta';
            return datos;
        }
        const fuente = {
            paciente: manual.nombre.value.trim(),
            idPaciente: manual.id.value.trim(),
            acceso: manual.acceso.value.trim()
        };
        const errores = [
            [manual.nombre, fuente.paciente && textoSinSeparador(fuente.paciente)
                ? '' : 'Escribe el nombre del paciente, hasta 200 caracteres y sin barra inversa.'],
            [manual.id, fuente.idPaciente && fuente.idPaciente.length <= 64 && textoSinSeparador(fuente.idPaciente)
                ? '' : 'Escribe el ID del paciente, hasta 64 caracteres y sin barra inversa.'],
            [manual.acceso, fuente.acceso && !/^[A-Za-z0-9 ._-]{1,16}$/.test(fuente.acceso)
                ? 'El acceso admite hasta 16 caracteres: letras, números, espacio, punto, guion o guion bajo.' : '']
        ];
        for (const [campo, textoError] of errores) fieldError(campo, textoError);
        const malo = errores.find(([, textoError]) => textoError);
        if (malo) {
            malo[0].focus();
            malo[0].reportValidity();
            return 'manual-invalido';
        }
        return { origen: 'manual', ...fuente };
    }

    function fila(etiqueta, valor) {
        const dt = document.createElement('dt');
        dt.textContent = etiqueta;
        const dd = document.createElement('dd');
        dd.textContent = valor && String(valor).trim() ? String(valor) : '—';
        confirmData.append(dt, dd);
    }

    function pintaConfirmacion(datos, conexion) {
        confirmData.replaceChildren();
        const etiquetas = { anonima: 'Sin datos de paciente', seleccion: 'Entrada seleccionada', manual: 'Datos escritos a mano' };
        fila('Origen de los datos', etiquetas[datos.origen] ?? datos.origen);
        if (datos.origen === 'seleccion') {
            // Una línea por dato de la entrada: si viaja, su valor; si no, lo que lleva en su lugar.
            const cita = citaProgramada();
            for (const dato of DATOS) {
                const marcado = datos.campos.includes(dato.clave);
                const valor = dato.clave === 'programado' ? cita.texto : valorDe(dato.clave);
                fila(dato.etiqueta, !marcado ? `no viaja · la imagen lleva ${dato.defecto}`
                    : valor ? valor : `la entrada no lo trajo · la imagen lleva ${dato.defecto}`);
            }
        } else if (datos.origen === 'anonima') {
            fila('Paciente', 'PRUEBA^CIRCULO');
            fila('PatientID', 'PRUEBA-NO-CLINICA');
        } else {
            fila('Paciente', datos.paciente);
            fila('PatientID', datos.idPaciente);
            fila('Acceso', datos.acceso);
        }
        fila('Nodo (el de arriba)', conexion.host);
        fila('Puerto', String(conexion.puerto));
        fila('AE remoto', conexion.aeRemoto);
        fila('AE local', conexion.aeLocal);
        const serieNueva = ' La serie y la imagen son nuevas, así que no se reemplaza ninguna imagen que ya existiera.';
        const marcados = datos.origen === 'seleccion' ? datos.campos : [];
        const dentro = marcados.includes('estudioUid') && Boolean(valorDe('estudioUid'));
        const pareceClinico = marcados.includes('modalidad') || marcados.includes('procedimiento') || marcados.includes('programado');
        const sinAlguno = datos.origen === 'seleccion' && marcados.length !== DATOS.length;
        confirmWarning.textContent = datos.origen === 'anonima'
            ? 'La imagen se registra como prueba técnica no diagnóstica, en un estudio nuevo. No se reintentará el envío automáticamente si su resultado es incierto.'
            : dentro
                ? `Atención: con una entrada de la lista la prueba se manda DENTRO del estudio programado de ese paciente, con su mismo UID de estudio —así el destino no abre un segundo registro bajo el mismo acceso, igual que haría el equipo real. Después habrá que borrarla desde el estudio del paciente, no como prueba aparte.${serieNueva}`
                    + (pareceClinico ? ' Además viajan la modalidad, el procedimiento o la fecha de la cita que escribió el nodo: en las listas del PACS se parecerá más a un estudio real, aunque los píxeles sigan siendo el círculo sintético y la imagen siga marcada ConversionType = SYN.' : '')
                    + (sinAlguno ? ' Los datos que no marcaste salen con el valor técnico de la prueba.' : '')
                    + ' No se reintentará el envío automáticamente si su resultado es incierto.'
                : 'La prueba se guarda en un estudio nuevo, aparte del programado. No se reintentará el envío automáticamente si su resultado es incierto.';
    }

    function confirmTest(event) {
        event.preventDefault();
        if (!canSend || sending || dialog.open) return;
        const conexion = getConnection();
        if (!conexion) return;
        const datos = datosDePrueba();
        if (datos === 'sin-seleccion') {
            message(sendStatus, 'Marca primero una entrada de la lista: sin selección no se envían datos de paciente.', 'warning');
            return;
        }
        if (datos === 'seleccion-incompleta') {
            message(sendStatus, 'La entrada seleccionada no trae nombre o ID de paciente; elige otro origen de datos para la prueba.', 'error');
            return;
        }
        if (datos === 'manual-invalido') {
            message(sendStatus, 'Revisa los datos escritos a mano antes de enviar la prueba.', 'error');
            return;
        }
        message(sendStatus);
        pendingRequest = Object.freeze({
            conexion: Object.freeze({ ...conexion }),
            datos: Object.freeze({ ...datos }),
            confirmar: true
        });
        pintaConfirmacion(datos, conexion);
        dialog.showModal();
        updateActions();
    }

    function uncertain(text) {
        message(sendStatus, `Resultado incierto. ${text} El registro de abajo dice qué responde el nodo al preguntarle por ese estudio. No se reintentará automáticamente.`, 'warning');
    }

    async function sendTest() {
        if (!canSend || sending || !pendingRequest || !dialog.open) return;
        const cuerpo = pendingRequest;
        pendingRequest = null;
        sending = true;
        dialog.close();
        invalidatePing();
        updateActions();
        message(sendStatus, 'Enviando una imagen de prueba y preguntando al nodo qué guarda de ese estudio. Espera el resultado; no se harán reintentos automáticos.');
        try {
            // Una sola petición por confirmación. No cancelar/reintentar un C-STORE desde la UI.
            const data = await request('prueba', cuerpo);
            const detail = typeof data.mensaje === 'string' ? data.mensaje : '';
            const verificacion = data.verificacion && typeof data.verificacion === 'object'
                ? (data.verificacion.mensaje || '') : '';
            switch (data.estado) {
                case 'enviada':
                    message(sendStatus, `Prueba enviada. ${detail}${verificacion ? ` En el nodo: ${verificacion}` : ''}`, 'success');
                    break;
                case 'advertencia':
                    message(sendStatus, `Envío con advertencia. ${detail} ${verificacion ? `En el nodo: ${verificacion}` : ''} Comprueba el resultado en el destino.`, 'warning');
                    break;
                case 'fallida':
                    message(sendStatus, `Prueba fallida. ${detail}${verificacion ? ` En el nodo: ${verificacion}` : ''}`, 'error');
                    break;
                case 'incierta':
                    uncertain(detail);
                    break;
                default:
                    uncertain('La respuesta del servicio no permite confirmar el envío.');
            }
        } catch (error) {
            if (error.auth || error.status === 400) {
                message(sendStatus, `Envío rechazado. ${error.message}`, 'error');
                revokeSendPermission(error);
            } else {
                // La pérdida de la respuesta HTTP no demuestra que C-STORE haya fallado.
                uncertain(error instanceof ApiError ? error.message : 'Se perdió la comunicación con el servicio.');
            }
        } finally {
            sending = false;
            updateActions();
            // El registro vive en el servidor: se vuelve a leer para que el renglón de este envío aparezca.
            await cargaRegistro();
        }
    }

    async function loadOptions() {
        try {
            const data = await request('opciones');
            canSend = data.puedeEnviar === true;
            if (!localEditedOrUsed && typeof data.aeLocal === 'string' && validAe(data.aeLocal.trim()) && data.aeLocal.length <= 16) {
                localAe.value = data.aeLocal;
            }
            message(byId('lt-options-status'));
            message(byId('lt-send-permission'), canSend ? '' : 'Tu cuenta permite consultar, pero no tiene permiso para enviar la prueba.');
        } catch (error) {
            canSend = false;
            message(byId('lt-options-status'), error.auth ? error.message : 'No se pudo verificar el permiso de envío. Puedes escribir los datos de conexión para consultar o hacer Ping.');
            message(byId('lt-send-permission'), 'Envío deshabilitado: no se pudo verificar el permiso. Recarga la página para volver a comprobarlo.');
        } finally {
            updateActions();
        }
    }

    // ---------- manejadores ----------

    for (const input of [connection.host, connection.port, connection.ae]) {
        const changed = () => {
            fieldError(input);
            connectionChanged();
        };
        input.addEventListener('input', changed);
        input.addEventListener('change', changed);
    }
    connection.ping.addEventListener('click', ping);

    async function ping() {
        if (connection.controller || sending) return;
        message(connection.status);
        message(connection.error);
        const conexion = getConnection();
        if (!conexion) return;
        const controller = new AbortController();
        connection.controller = controller;
        updateActions();
        try {
            // Eco recibe la conexión DIRECTA, no el envoltorio de buscar/prueba.
            const data = await request('eco', conexion, controller.signal);
            if (connection.controller !== controller) return;
            const connected = data.conectado === true;
            message(connection.status, connected ? 'Conexión establecida' : 'Sin conexión', connected ? 'success' : 'error');
        } catch (error) {
            if (connection.controller !== controller || error.name === 'AbortError') return;
            if (error.auth || error.status === 400) {
                // Autenticación, CSRF y validación se explican fuera del resultado Ping.
                message(connection.error, error.message, 'error');
                revokeSendPermission(error);
            } else {
                message(connection.status, 'Sin conexión', 'error');
            }
        } finally {
            if (connection.controller === controller) {
                connection.controller = null;
                updateActions();
            }
        }
    }

    function localChanged() {
        localEditedOrUsed = true;
        fieldError(localAe);
        connectionChanged();
    }
    localAe.addEventListener('input', localChanged);
    localAe.addEventListener('change', localChanged);

    function modalityChanged() {
        modality.value = modality.value.toUpperCase();
        fieldError(modality);
        // El navegador lanza change al salir del campo aunque no haya cambiado nada; repintar
        // entonces sustituiría la fila que el usuario está apuntando y el clic no seleccionaría.
        if (estadoFiltros() === filtrosPintados) return;
        if (studies.length) consultaVieja = true;
        pintaFiltros();
    }
    modality.addEventListener('input', modalityChanged);
    modality.addEventListener('change', modalityChanged);

    for (const [clave, campo] of Object.entries(filters)) {
        const changed = () => {
            fieldError(campo);
            if (!studies.length || estadoFiltros() === filtrosPintados) return;
            if (clave === 'desde' || clave === 'hasta') consultaVieja = true;
            pintaFiltros();
        };
        campo.addEventListener('input', changed);
        campo.addEventListener('change', changed);
    }

    byId('lt-clear-filters').addEventListener('click', () => {
        for (const campo of Object.values(filters)) {
            campo.value = '';
            fieldError(campo);
        }
        modality.value = '';
        fieldError(modality);
        // Al vaciar todo, lo mostrado vuelve a ser exactamente lo que devolvió el nodo:
        // ya no hay modalidad ni periodo pendientes de volver a consultar.
        consultaVieja = false;
        pintaFiltros();
        if (studies.length) {
            message(resultStatus, summary, summaryKind);
            if (consultaParcial) message(byId('lt-limited'), TEXTO_LIMITE, 'warning');
        } else {
            message(resultStatus, 'Configura el nodo MWL y pulsa Consultar.');
        }
        queryButton.focus();
    });

    rowsElement.addEventListener('change', event => {
        const radio = event.target;
        if (!radio || radio.type !== 'radio' || radio.name !== 'lt-pick') return;
        selected = visible[Number(radio.value)] ?? null;
        if (selected) {
            seleccionRadio.checked = true;
            manualFields.hidden = true;
        }
        for (const row of rowsElement.querySelectorAll('tr')) {
            row.classList.toggle('lt-selected', Boolean(row.querySelector('input:checked')));
        }
        pintaSeleccion();
    });

    for (const radio of origenRadios) {
        radio.addEventListener('change', () => {
            if (radio.value === 'seleccion' && !selected) {
                message(sendStatus, 'Marca primero una entrada de la lista; sin selección no se envían datos de paciente.', 'warning');
                origenRadios[0].checked = true;
            } else if (radio.value !== 'manual') {
                message(sendStatus);
            }
            pintaSeleccion();
        });
    }
    for (const campo of Object.values(manual)) {
        const changed = () => fieldError(campo);
        campo.addEventListener('input', changed);
        campo.addEventListener('change', changed);
    }

    // Las casillas de datos: cambiarlas no obliga a volver a consultar, solo reescribe el aviso y,
    // si la ventana de confirmación estuviera abierta, su contenido.
    for (const box of dataBoxes) {
        box.addEventListener('change', () => {
            pintaDatosStatus();
            if (dialog.open && origen() === 'seleccion') {
                const datos = datosDePrueba();
                const conexion = getConnection();
                if (conexion && typeof datos === 'object') {
                    pendingRequest = Object.freeze({
                        conexion: Object.freeze({ ...conexion }),
                        datos: Object.freeze({ ...datos }),
                        confirmar: true
                    });
                    pintaConfirmacion(datos, conexion);
                }
            }
        });
    }

    registro.refresh.addEventListener('click', () => { cargaRegistro(); registro.refresh.focus(); });

    byId('lt-query-form').addEventListener('submit', search);
    cancelButton.addEventListener('click', () => {
        cancelQuery('Consulta cancelada. No se muestran resultados de la petición cancelada.');
        queryButton.focus();
    });
    byId('lt-prev').addEventListener('click', () => { if (page > 0) { page--; renderPage(); } });
    byId('lt-next').addEventListener('click', () => { if ((page + 1) * PAGE_SIZE < visible.length) { page++; renderPage(); } });
    byId('lt-test-form').addEventListener('submit', confirmTest);
    byId('lt-confirm-cancel').addEventListener('click', () => dialog.close());
    byId('lt-confirm-send').addEventListener('click', sendTest);
    dialog.addEventListener('close', () => { pendingRequest = null; updateActions(); });
    const pattern = byId('lt-pattern');
    const patternError = byId('lt-pattern-error');
    pattern.addEventListener('error', () => { patternError.hidden = false; });
    pattern.addEventListener('load', () => { patternError.hidden = true; });
    if (pattern.complete && !pattern.naturalWidth) patternError.hidden = false;

    // La hoja de calendario compartida de la intranet (Día · Mes · Año · Rango) y su hoja
    // de horas (Una hora · Rango), con el mismo formato que tiene la Bitácora.
    function horasRecibidas() {
        // Cuántas entradas caen en cada hora de lo que devolvió el nodo, para que la hoja
        // lo diga en el título de cada casilla. Sin lista recibida no hay nada que contar.
        if (!studies.length) return null;
        const por = {};
        for (const study of studies) {
            const ventana = ventanaHora(study.hora);
            if (!ventana) continue;
            const clave = String(Math.floor(ventana.from / 60)).padStart(2, '0');
            por[clave] = (por[clave] || 0) + 1;
        }
        return por;
    }

    if (window.HojaFecha) {
        HojaFecha.instalar({ desde: 'lt-f-desde', hasta: 'lt-f-hasta', titulo: 'Periodo', cualquier: 'Cualquier día' });
        HojaFecha.instalarHora({
            desde: 'lt-f-hora-desde',
            hasta: 'lt-f-hora-hasta',
            titulo: 'Hora',
            cualquier: 'Todas las horas',
            conteos: horasRecibidas
        });
    }

    // La lista consultada y las conexiones no se guardan en el navegador. Lo que sí queda en el servidor
    // es el registro de envíos (App_Data/dicom-pruebas.jsonl): se escribe al enviar y se lee desde aquí.
    window.addEventListener('pagehide', () => {
        cancelQuery('Configura el nodo MWL y pulsa Consultar.');
        invalidatePing();
        if (dialog.open) dialog.close();
    });
    updateActions();
    pintaSeleccion();
    pintaDestino();
    pintaRegistro();
    cargaRegistro();
    loadOptions();
})();
