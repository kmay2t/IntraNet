(() => {
  "use strict";

  const API = "/api/consulta-patologia";
  const AREAS = {
    esp: "Patología Especialidades",
    gineco: "Patología Ginecología"
  };
  const POR_PAGINA = 25;
  const estado = {
    area: "esp",
    pagina: 1,
    total: 0,
    cargando: false,
    controlador: null,
    focoAnterior: null,
    pdfActual: null,
    controladorPdf: null,
    blobPdf: "",
    solicitudPdf: 0
  };
  let temporizadorBusqueda = 0;

  const $ = (id) => document.getElementById(id);
  const crear = (etiqueta, clase, texto) => {
    const nodo = document.createElement(etiqueta);
    if (clase) nodo.className = clase;
    if (texto !== undefined) nodo.textContent = texto;
    return nodo;
  };

  function fmtFecha(iso) {
    if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return "—";
    const [anio, mes, dia] = iso.split("-");
    return `${dia}/${mes}/${anio}`;
  }

  function textoOMarcion(valor) {
    const texto = String(valor ?? "").trim();
    return texto || "—";
  }

  function poblarEstudios(valores) {
    const lista = $("f-estudios-lista");
    const estudios = [...new Set((Array.isArray(valores) ? valores : [])
      .map((valor) => String(valor ?? "").trim())
      .filter(Boolean))];
    const opciones = document.createDocumentFragment();
    estudios.forEach((estudio) => {
      const opcion = document.createElement("option");
      opcion.value = estudio;
      opciones.append(opcion);
    });
    lista.replaceChildren(opciones);
  }

  function mostrarFeedback(mensaje, reintentar = false) {
    $("feedback-texto").textContent = mensaje;
    $("btn-reintentar").hidden = !reintentar;
    $("feedback").hidden = !mensaje;
  }

  function actualizarControles() {
    const paginas = Math.max(1, Math.ceil(estado.total / POR_PAGINA));
    $("btn-buscar").disabled = estado.cargando;
    $("btn-buscar").textContent = estado.cargando ? "Buscando…" : "Buscar";
    $("btn-anterior").disabled = estado.cargando || estado.pagina <= 1;
    $("btn-siguiente").disabled = estado.cargando || estado.pagina >= paginas || estado.total === 0;
    $("pagina-texto").textContent = estado.total === 0 ? "Página 1 de 1" : `Página ${estado.pagina} de ${paginas}`;
    $("tabla-resultados").setAttribute("aria-busy", String(estado.cargando));
  }

  function mensajeEnTabla(texto, cargando = false) {
    const tr = crear("tr");
    const td = crear("td", "table-message");
    td.colSpan = 8;
    if (cargando) {
      const spinner = crear("span", "spinner");
      spinner.setAttribute("aria-hidden", "true");
      td.append(spinner, document.createTextNode(texto));
    } else {
      td.textContent = texto;
    }
    tr.append(td);
    $("tbody-resultados").replaceChildren(tr);
  }

  function renderResultados(datos) {
    estado.total = Number(datos.total) || 0;
    estado.pagina = Number(datos.pagina) || estado.pagina;
    poblarEstudios(datos.estudios);
    const resultados = Array.isArray(datos.resultados) ? datos.resultados : [];
    const tbody = $("tbody-resultados");
    tbody.replaceChildren();

    if (resultados.length === 0) {
      mensajeEnTabla("No se encontraron reportes con los filtros seleccionados.");
      $("resumen").textContent = "Sin resultados";
      return;
    }

    const desde = (estado.pagina - 1) * POR_PAGINA + 1;
    const hasta = Math.min(estado.pagina * POR_PAGINA, estado.total);
    const numero = new Intl.NumberFormat("es-MX");

    resultados.forEach((reporte) => {
      const tr = crear("tr");
      const valores = [
        reporte.folio || "Sin folio",
        fmtFecha(reporte.fecha),
        textoOMarcion(reporte.nombre),
        textoOMarcion(reporte.nss),
        textoOMarcion(reporte.agregado_medico),
        textoOMarcion(reporte.modalidad),
        String(reporte.estudio ?? "").trim() || "Otros"
      ];
      valores.forEach((valor) => tr.append(crear("td", "", valor)));

      const tdAccion = crear("td");
      const boton = crear("button", "link-button", "Ver PDF");
      boton.type = "button";
      boton.setAttribute("aria-label", `Ver PDF del reporte ${reporte.folio || reporte.id}`);
      boton.addEventListener("click", () => abrirPdf(reporte));
      tdAccion.append(boton);
      tr.append(tdAccion);
      tbody.append(tr);
    });

    $("resumen").textContent = `Mostrando ${numero.format(desde)}–${numero.format(hasta)} de ${numero.format(estado.total)}`;
  }

  function filtrosActuales() {
    return {
      area: estado.area,
      q: $("f-q").value.trim(),
      modalidad: $("f-modalidad").value,
      estudio: $("f-estudio").value,
      desde: $("f-desde").value,
      hasta: $("f-hasta").value,
      pagina: estado.pagina,
      porPagina: POR_PAGINA
    };
  }

  async function leerJson(respuesta) {
    const tipo = (respuesta.headers.get("content-type") || "").toLowerCase();
    if (!tipo.includes("application/json")) return {};
    return respuesta.json().catch(() => ({}));
  }

  async function buscar(pagina = 1) {
    clearTimeout(temporizadorBusqueda);
    if (estado.controlador) estado.controlador.abort();

    const desde = $("f-desde").value;
    const hasta = $("f-hasta").value;
    if (desde && hasta && desde > hasta) {
      mostrarFeedback("La fecha “Desde” no puede ser posterior a la fecha “Hasta”.", false);
      return;
    }

    estado.pagina = Math.max(1, Number(pagina) || 1);
    estado.cargando = true;
    mostrarFeedback("");
    mensajeEnTabla("Cargando reportes…", true);
    $("resumen").textContent = "Cargando reportes…";
    actualizarControles();

    const controlador = new AbortController();
    estado.controlador = controlador;
    try {
      const respuesta = await fetch(`${API}/buscar`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "Accept": "application/json" },
        body: JSON.stringify(filtrosActuales()),
        credentials: "omit",
        cache: "no-store",
        signal: controlador.signal
      });
      const datos = await leerJson(respuesta);
      if (!respuesta.ok) {
        throw new Error(datos.error || `No se pudo consultar el repositorio (HTTP ${respuesta.status}).`);
      }
      if (estado.controlador !== controlador) return;
      renderResultados(datos);
    } catch (error) {
      if (error.name === "AbortError" || estado.controlador !== controlador) return;
      estado.total = 0;
      const mensaje = error instanceof TypeError
        ? "No fue posible conectar con el servidor. Revisa la conexión e inténtalo de nuevo."
        : error.message;
      mostrarFeedback(mensaje, true);
      mensajeEnTabla("No se pudieron cargar los reportes.");
      $("resumen").textContent = "Consulta no disponible";
    } finally {
      if (estado.controlador === controlador) {
        estado.controlador = null;
        estado.cargando = false;
        actualizarControles();
      }
    }
  }

  function programarBusqueda() {
    clearTimeout(temporizadorBusqueda);
    temporizadorBusqueda = setTimeout(() => buscar(1), 380);
  }

  function cambiarArea(area, buscarAhora = true) {
    estado.area = AREAS[area] ? area : "esp";
    $("f-area").value = estado.area;
    $("area-nombre").textContent = AREAS[estado.area];
    $("f-estudio").value = "";
    if (buscarAhora) buscar(1);
  }

  function liberarPdf() {
    if (estado.blobPdf) {
      URL.revokeObjectURL(estado.blobPdf);
      estado.blobPdf = "";
    }
  }

  function mostrarErrorPdf(mensaje) {
    $("visor-cargando").hidden = true;
    $("visor-error-texto").textContent = mensaje;
    $("visor-error").hidden = false;
    $("visor-frame").removeAttribute("src");
  }

  async function cargarPdf() {
    if (!estado.pdfActual) return;
    if (estado.controladorPdf) estado.controladorPdf.abort();
    liberarPdf();

    const actual = estado.pdfActual;
    const solicitud = ++estado.solicitudPdf;
    const controlador = new AbortController();
    estado.controladorPdf = controlador;
    $("visor-cargando").hidden = false;
    $("visor-error").hidden = true;
    $("visor-frame").removeAttribute("src");

    try {
      const respuesta = await fetch(actual.url, {
        credentials: "omit",
        cache: "no-store",
        signal: controlador.signal
      });
      if (!respuesta.ok) {
        const datos = await leerJson(respuesta);
        throw new Error(datos.error || `No se pudo abrir el PDF (HTTP ${respuesta.status}).`);
      }
      const tipo = (respuesta.headers.get("content-type") || "").toLowerCase();
      if (!tipo.includes("application/pdf")) throw new Error("El servidor no devolvió un archivo PDF.");
      const contenido = await respuesta.arrayBuffer();
      const firma = new TextDecoder("ascii").decode(contenido.slice(0, 5));
      if (firma !== "%PDF-") throw new Error("El archivo recibido no es un PDF válido.");
      if (solicitud !== estado.solicitudPdf) return;

      const blob = new Blob([contenido], { type: "application/pdf" });
      estado.blobPdf = URL.createObjectURL(blob);
      $("visor-frame").src = estado.blobPdf;
      $("visor-cargando").hidden = true;
    } catch (error) {
      if (error.name === "AbortError" || solicitud !== estado.solicitudPdf) return;
      mostrarErrorPdf(error instanceof TypeError
        ? "No fue posible conectar con el servidor para cargar el reporte."
        : error.message);
    } finally {
      if (estado.controladorPdf === controlador) estado.controladorPdf = null;
    }
  }

  function abrirPdf(reporte) {
    const id = Number(reporte.id);
    if (!Number.isSafeInteger(id) || id <= 0) return;
    estado.focoAnterior = document.activeElement;
    estado.pdfActual = { id, url: `${API}/reportes/${id}/pdf`, reporte };
    const folio = reporte.folio || `#${id}`;
    $("visor-titulo").textContent = `Reporte ${folio}`;
    $("visor-meta").textContent = [
      reporte.nombre,
      fmtFecha(reporte.fecha),
      reporte.modalidad,
      reporte.estudio || "Otros"
    ].filter(Boolean).join(" · ");

    $("visor-abrir").href = estado.pdfActual.url;
    $("visor-error-abrir").href = estado.pdfActual.url;
    $("visor-descargar").href = estado.pdfActual.url;
    $("visor-descargar").download = `reporte-${folio.replace(/[^a-zA-Z0-9._-]+/g, "_")}.pdf`;
    $("visor").hidden = false;
    document.body.classList.add("viewer-open");
    $("visor-cerrar").focus();
    cargarPdf();
  }

  function cerrarVisor() {
    if ($("visor").hidden) return;
    estado.solicitudPdf++;
    if (estado.controladorPdf) estado.controladorPdf.abort();
    estado.controladorPdf = null;
    liberarPdf();
    $("visor-frame").removeAttribute("src");
    $("visor").hidden = true;
    document.body.classList.remove("viewer-open");
    estado.pdfActual = null;
    if (estado.focoAnterior && typeof estado.focoAnterior.focus === "function") {
      estado.focoAnterior.focus();
    }
    estado.focoAnterior = null;
  }

  function mantenerFocoEnVisor(evento) {
    if (evento.key !== "Tab" || $("visor").hidden) return;
    const focuseables = [...$("visor").querySelectorAll("a[href], button:not([disabled]), iframe")]
      .filter((nodo) => !nodo.hidden && nodo.getClientRects().length > 0);
    if (focuseables.length === 0) return;
    const primero = focuseables[0];
    const ultimo = focuseables[focuseables.length - 1];
    if (evento.shiftKey && document.activeElement === primero) {
      evento.preventDefault();
      ultimo.focus();
    } else if (!evento.shiftKey && document.activeElement === ultimo) {
      evento.preventDefault();
      primero.focus();
    }
  }

  $("form-busqueda").addEventListener("submit", (evento) => {
    evento.preventDefault();
    buscar(1);
  });
  $("btn-limpiar").addEventListener("click", () => {
    clearTimeout(temporizadorBusqueda);
    $("form-busqueda").reset();
    cambiarArea($("f-area").value, false);
    buscar(1);
  });
  $("btn-reintentar").addEventListener("click", () => buscar(estado.pagina));
  $("btn-anterior").addEventListener("click", () => buscar(estado.pagina - 1));
  $("btn-siguiente").addEventListener("click", () => buscar(estado.pagina + 1));
  $("f-area").addEventListener("change", () => cambiarArea($("f-area").value));
  $("f-q").addEventListener("input", programarBusqueda);
  $("f-modalidad").addEventListener("change", () => buscar(1));
  $("f-estudio").addEventListener("input", programarBusqueda);
  $("f-desde").addEventListener("change", () => buscar(1));
  $("f-hasta").addEventListener("change", () => buscar(1));

  $("visor-cerrar").addEventListener("click", cerrarVisor);
  $("visor-reintentar").addEventListener("click", cargarPdf);
  $("visor").addEventListener("click", (evento) => {
    if (evento.target === $("visor")) cerrarVisor();
  });
  document.addEventListener("keydown", (evento) => {
    if (evento.key === "Escape") cerrarVisor();
    mantenerFocoEnVisor(evento);
  });
  window.addEventListener("beforeunload", liberarPdf);

  cambiarArea((location.hash || "").slice(1), false);
  if (location.hash) history.replaceState(null, "", location.pathname + location.search);
  buscar(1);
})();
