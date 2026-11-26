/*
 * desplegable-nodos.js — Lista desplegable de nodos PACS para las pantallas DICOM.
 *
 * Sustituye visualmente un <select> por un botón que despliega un panel con casillas.
 * El <select> original sigue en el DOM, oculto, y es la fuente de verdad: el código de las
 * páginas lee `sel.options` / `opt.selected` igual que antes, así que no hay que reescribir
 * las consultas ni el envío. La lógica de abajo solo pinta, sincroniza y refresca.
 *
 * Uso:
 *   const dep = DesplegableNodos($("aeTitleList"), { multiple: true });
 *   ... llenar las opciones ...
 *   dep.refrescar();
 */
(function (window) {
  "use strict";

  const CSS_ID = "dicom-desplegable-css";

  const CSS = `
  .dep { position: relative; margin-top: 6px; }
  .dep-boton {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 10px 12px;
    border-radius: 6px;
    border: 1px solid var(--theme-control-border, #9e9e9e);
    background: var(--panel, #ffffff);
    color: var(--texto, #111);
    font: inherit;
    font-size: 14px;
    text-align: left;
    cursor: pointer;
  }
  .dep-boton:hover { border-color: var(--azul, #0d47a1); }
  .dep-boton:focus-visible { outline: 2px solid var(--dorado, #c9a227); outline-offset: 1px; }
  .dep-boton[aria-expanded="true"] { border-color: var(--azul, #0d47a1); }
  .dep-texto { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .dep-texto.vacio { color: var(--muted, #555); font-style: italic; }
  .dep-pip {
    flex: none;
    min-width: 22px;
    padding: 1px 7px;
    border-radius: 10px;
    background: var(--azul, #0d47a1);
    color: #fff;
    font-size: 11.5px;
    font-weight: 700;
    text-align: center;
  }
  .dep-flecha { flex: none; color: var(--muted, #555); font-size: 11px; }
  .dep-panel {
    position: absolute;
    top: calc(100% + 4px);
    left: 0;
    z-index: 40;
    min-width: 100%;
    max-width: min(420px, 92vw);
    max-height: 268px;
    overflow-y: auto;
    /* --panel (no --card): la lista tiene que quedar opaca. Con el fondo translúcido de las
       tarjetas las filas se leen encima del emblema y del texto de la propia pantalla.
       En modo noche --panel es azul marino y --texto blanco humo, así que la pareja cambia sola. */
    background: var(--panel, #fff);
    color: var(--texto, #111);
    border: 1px solid var(--theme-control-border, #d9d9d9);
    border-radius: 8px;
    box-shadow: 0 8px 22px rgba(0, 0, 0, .28);
    padding: 4px;
  }
  .dep-fila {
    display: flex;
    flex-direction: row;
    align-items: center;
    gap: 9px;
    padding: 8px 10px;
    border-radius: 6px;
    font-size: 14px;
    font-weight: 500;
    color: var(--texto, #111);
    letter-spacing: normal;
    text-transform: none;
    cursor: pointer;
    margin: 0;
  }
  .dep-fila:hover { background: var(--row-hover, rgba(13, 71, 161, .06)); }
  /* Las casillas viven dentro de un <label>, y las hojas de la intranet ponen el input a ancho
     completo: hay que deshacer ese estilo o la casilla sale enorme dentro del panel. */
  .dep-fila input {
    width: 15px;
    height: 15px;
    flex: none;
    margin: 0;
    padding: 0;
    border: 0;
    border-radius: 0;
    background: none;
    accent-color: var(--azul, #0d47a1);
  }
  .dep-fila.on { background: var(--selected, rgba(13, 71, 161, .1)); font-weight: 600; }
  .dep-fila .dep-dir { color: var(--muted, #555); font-weight: 400; font-size: 12px; margin-left: auto; }
  .dep-pie {
    display: flex;
    gap: 8px;
    align-items: center;
    margin-top: 4px;
    padding-top: 6px;
    border-top: 1px solid var(--borde, #d9d9d9);
  }
  .dep-pie button {
    padding: 5px 12px;
    font-size: 12px;
    border-radius: 5px;
    border: 1px solid var(--theme-control-border, #d9d9d9);
    background: var(--panel, #fff);
    color: var(--texto, #111);
    cursor: pointer;
    font: inherit;
  }
  .dep-pie button:hover { background: var(--theme-hover, var(--panel-2, #f7f4ea)); }
  .dep-nota { font-size: 11.5px; color: var(--muted, #555); padding: 2px 6px 4px; }
  `;

  function inyectarEstilos() {
    if (document.getElementById(CSS_ID)) return;
    const s = document.createElement("style");
    s.id = CSS_ID;
    s.textContent = CSS;
    document.head.appendChild(s);
  }

  function DesplegableNodos(select, opciones) {
    if (!select) return null;
    opciones = opciones || {};
    const multiple = opciones.multiple !== false;
    const vacioTexto = opciones.vacio || "Sin nodos configurados";
    inyectarEstilos();

    // El <select> pasa a ser solo datos: sigue respondiendo a .options y .selected.
    select.hidden = true;
    select.tabIndex = -1;

    const caja = document.createElement("div");
    caja.className = "dep";

    const boton = document.createElement("button");
    boton.type = "button";
    boton.className = "dep-boton";
    boton.id = select.id + "-dep";
    boton.setAttribute("aria-haspopup", multiple ? "true" : "listbox");
    boton.setAttribute("aria-expanded", "false");

    const texto = document.createElement("span");
    texto.className = "dep-texto";
    const pip = document.createElement("span");
    pip.className = "dep-pip";
    pip.hidden = true;
    const flecha = document.createElement("span");
    flecha.className = "dep-flecha";
    flecha.textContent = "▾";
    if (multiple) boton.append(texto, pip, flecha);
    else boton.append(texto, flecha);

    const panel = document.createElement("div");
    panel.className = "dep-panel";
    panel.hidden = true;

    caja.append(boton, panel);
    select.after(caja);

    // La etiqueta de la fila ahora manda sobre el botón: al pulsarla se abre la lista.
    const etiqueta = document.querySelector('label[for="' + select.id + '"]');
    if (etiqueta) etiqueta.htmlFor = boton.id;

    const filaPorOpcion = new Map();

    function relleno() {
      return Array.from(select.options).filter(function (o) { return o.value; });
    }

    function nombre(opcion) {
      return (opcion.textContent || opcion.value || "").trim();
    }

    function pintaTexto() {
      const todos = relleno();
      const elegidas = todos.filter(function (o) { return o.selected; });

      if (todos.length === 0) {
        texto.textContent = vacioTexto;
        texto.classList.add("vacio");
        pip.hidden = true;
        return;
      }
      texto.classList.remove("vacio");

      if (!multiple) {
        texto.textContent = elegidas.length ? nombre(elegidas[0]) : "Elige un servidor";
        if (!elegidas.length) texto.classList.add("vacio");
        pip.hidden = true;
        return;
      }

      pip.hidden = false;
      pip.textContent = String(elegidas.length);
      if (elegidas.length === 0) {
        texto.textContent = opciones.ninguno || "Ninguno";
        texto.classList.add("vacio");
      } else if (elegidas.length === todos.length) {
        texto.textContent = "Todos los servidores (" + todos.length + ")";
      } else if (elegidas.length <= 2) {
        texto.textContent = elegidas.map(nombre).join(", ");
      } else {
        texto.textContent = elegidas.slice(0, 2).map(nombre).join(", ") +
          " +" + (elegidas.length - 2) + " más";
      }
    }

    function construirFilas() {
      panel.innerHTML = "";
      filaPorOpcion.clear();

      const todos = relleno();
      if (todos.length === 0) {
        const nota = document.createElement("div");
        nota.className = "dep-nota";
        nota.textContent = "No hay nodos PACS en appsettings.json → PacsNodes.";
        panel.appendChild(nota);
        return;
      }

      for (const opcion of todos) {
        const fila = document.createElement("label");
        fila.className = "dep-fila";

        const inp = document.createElement("input");
        inp.type = multiple ? "checkbox" : "radio";
        inp.name = select.id + "-dep";
        inp.checked = !!opcion.selected;

        const nombreSpan = document.createElement("span");
        nombreSpan.textContent = nombre(opcion);

        fila.append(inp, nombreSpan);
        if (opcion.title) {
          const dir = document.createElement("span");
          dir.className = "dep-dir";
          dir.textContent = opcion.title;
          fila.appendChild(dir);
        }
        panel.appendChild(fila);

        fila.addEventListener("change", function () {
          if (multiple) {
            opcion.selected = inp.checked;
          } else {
            for (const o of todos) o.selected = o === opcion;
          }
          sincronizar();
          pintaTexto();
          if (opciones.onChange) opciones.onChange(valores());
          if (!multiple) cerrar();
        });

        filaPorOpcion.set(opcion, { fila: fila, input: inp });
      }

      if (multiple) {
        const pie = document.createElement("div");
        pie.className = "dep-pie";
        const bTodos = document.createElement("button");
        bTodos.type = "button";
        bTodos.textContent = "Todos";
        const bNinguno = document.createElement("button");
        bNinguno.type = "button";
        bNinguno.textContent = "Ninguno";
        bTodos.addEventListener("click", function () { elegirTodos(true); });
        bNinguno.addEventListener("click", function () { elegirTodos(false); });
        pie.append(bTodos, bNinguno);
        panel.appendChild(pie);
      }
    }

    function sincronizar() {
      for (const [opcion, ref] of filaPorOpcion) ref.input.checked = !!opcion.selected;
      for (const [opcion, ref] of filaPorOpcion) ref.fila.classList.toggle("on", !!opcion.selected);
    }

    function elegirTodos(activo) {
      const v = !!activo;
      for (const o of relleno()) o.selected = v;
      sincronizar();
      pintaTexto();
      if (opciones.onChange) opciones.onChange(valores());
    }

    /* Las tarjetas de la intranet son contextos de apilamiento (position:relative + z-index), así
       que un panel desplegado dentro de una queda tapado por la tarjeta siguiente. Mientras la
       lista está abierta se sube el z-index de los ancestros posicionados y al cerrarla se
       devuelve cada uno a su valor anterior. */
    let elevados = [];

    function eleva() {
      if (elevados.length) return;
      let n = caja.parentElement;
      while (n && n !== document.documentElement) {
        const cs = window.getComputedStyle(n);
        if (cs.position !== "static") {
          const z = parseInt(cs.zIndex, 10);
          if (isNaN(z) || z < 60) {
            elevados.push([n, n.style.zIndex]);
            n.style.zIndex = "60";
          }
        }
        n = n.parentElement;
      }
    }

    function restaura() {
      for (const [n, valor] of elevados) n.style.zIndex = valor;
      elevados = [];
    }

    function abrir() {
      eleva();
      panel.hidden = false;
      boton.setAttribute("aria-expanded", "true");
    }
    function cerrar() {
      panel.hidden = true;
      boton.setAttribute("aria-expanded", "false");
      restaura();
    }
    function alternar() {
      if (panel.hidden) abrir(); else cerrar();
    }

    boton.addEventListener("click", alternar);
    boton.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { cerrar(); return; }
      if (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        alternar();
        if (!panel.hidden) {
          const primera = panel.querySelector(".dep-fila input");
          if (primera) primera.focus();
        }
      }
    });
    document.addEventListener("click", function (e) {
      if (!panel.hidden && !caja.contains(e.target)) cerrar();
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && !panel.hidden) { cerrar(); boton.focus(); }
    });

    const api = {
      select: select,
      // Se llama tras (re)llenar las opciones del <select>.
      refrescar: function () {
        construirFilas();
        sincronizar();
        pintaTexto();
        return api;
      },
      valores: valores,
      todos: function () { elegirTodos(true); },
      ninguno: function () { elegirTodos(false); },
      alternar: alternar,
      cerrar: cerrar
    };

    function valores() {
      return relleno().filter(function (o) { return o.selected; }).map(function (o) { return o.value; });
    }

    // Un código que mute las opciones a mano puede refrescar así: select.desplegable.refrescar().
    select.desplegable = api;
    api.refrescar();
    return api;
  }

  window.DesplegableNodos = DesplegableNodos;
})(window);
