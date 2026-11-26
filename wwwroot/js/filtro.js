// Utilidad de búsqueda de texto para toda la intranet.
//
// Para que "NUNEZ" encuentre "NUÑEZ" y "CRANEO" encuentre "CRÁNEO" hay que comparar los textos
// sin acentos y sin distinguir mayúsculas. Cada página lo hacía a su manera (o no lo hacía), así
// que la regla vive aquí y las páginas solo la llaman.
//
// Se carga en todas las páginas junto con theme.js. Si una página se abre sin este archivo, las
// páginas usan "FiltroLocal" como respaldo: funciona igual salvo que vuelve a distinguir la Ñ.
(function (global) {
  'use strict';

  // Quita tildes y diéresis separando la letra de su marca (NFD) y tirando las marcas;
  // la ñ y la ç no se descomponen, así que se cambian a mano.
  function norm(texto) {
    var s = (texto === null || texto === undefined) ? '' : String(texto);
    s = s.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    s = s.replace(/[Ññ]/g, 'N').replace(/[Çç]/g, 'C');
    return s.toLowerCase().replace(/\s+/g, ' ').trim();
  }

  // ¿"donde" contiene a "que"?, sin acentos ni mayúsculas. Un texto vacío siempre cabe.
  function contiene(donde, que) {
    var q = norm(que);
    if (!q) return true;
    return norm(donde).indexOf(q) > -1;
  }

  // ¿"donde" empieza por "que"? (para listas grandes donde importa el prefijo).
  function empieza(donde, que) {
    var q = norm(que);
    if (!q) return true;
    return norm(donde).slice(0, q.length) === q;
  }

  // Varias cajas de texto contra una sola consulta: basta con que una coincida.
  function coincideAlguno(casillas, que) {
    for (var i = 0; i < casillas.length; i++) {
      if (contiene(casillas[i], que)) return true;
    }
    return false;
  }

  global.Filtro = { norm: norm, contiene: contiene, empieza: empieza, coincideAlguno: coincideAlguno };
})(window);
