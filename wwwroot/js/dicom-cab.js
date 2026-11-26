/*
 * dicom-cab.js — Lee las etiquetas de cabecera de un archivo DICOM en el navegador.
 *
 * Sirve para agrupar por estudio los archivos que el usuario arrastra o elige de una carpeta:
 * con el StudyInstanceUID, el nombre del paciente, su ID, la descripción, la modalidad y el
 * SOPInstanceUID alcanza para pintar la tabla y contar las imágenes, sin subir nada todavía.
 *
 * No hace falta leer el archivo completo: se piden solo los primeros bytes (los píxeles van al
 * final y pueden ser megas). Se soportan los tres modos que salen en un PACS de rutina:
 * Explícito VR little endian (el normal), Implícito VR little endian (equipos viejos) y
 * Explícito VR big endian (raro, pero existe).
 */
(function (window) {
  "use strict";

  // Primera ventana de lectura; si la cabecera es más larga de lo previsto se prueba con la ancha.
  const MAX_CABECERA = 96 * 1024;
  const MAX_AMPLIA = 1024 * 1024;
  const MAX_PROFUNDIDAD = 8;

  // Etiquetas que interesan, como (grupo << 16) | elemento.
  const T = {
    TransferSyntaxUID: 0x00020010,
    GroupLengthMeta: 0x00020000,
    Modality: 0x00080060,
    ModalitiesInStudy: 0x00080061,
    SOPClassUID: 0x00080016,
    SOPInstanceUID: 0x00080018,
    StudyDescription: 0x00081030,
    SeriesDescription: 0x0008103E,
    PatientName: 0x00100010,
    PatientID: 0x00100020,
    StudyInstanceUID: 0x0020000D,
    SeriesInstanceUID: 0x0020000E,
    PixelData: 0x7FE00010
  };

  // VRs que en modo explícito llevan 2 bytes reservados antes de la longitud de 4 bytes.
  const VR_LARGA = new Set(["OB", "OW", "OF", "OD", "OL", "OV", "UN", "SQ", "UT", "UC", "UR"]);

  const decodificador = new TextDecoder("utf-8", { fatal: false });

  function texto(bytes) {
    return decodificador.decode(bytes).replace(/\0+$/, "").trim();
  }

  // Convierte "APELLIDO^NOMBRE" al formato legible de la pantalla.
  function nombreLegible(v) {
    if (!v) return "";
    const partes = v.split("^").map(p => p.trim()).filter(p => p.length);
    if (partes.length === 0) return "";
    if (partes.length === 1) return partes[0];
    const apellido = partes[0];
    const resto = partes.slice(1).join(" ");
    return apellido ? `${apellido}, ${resto}` : resto;
  }

  function modalidadLegible(v, modality) {
    const lista = (v || "").split("\\").map(m => m.trim()).filter(Boolean);
    if (lista.length) return Array.from(new Set(lista)).join("/");
    return modality || "";
  }

  function capturar(clave, bytes, out) {
    switch (clave) {
      case T.TransferSyntaxUID: out.ts = texto(bytes); break;
      case T.PatientName: out.patientName = texto(bytes); break;
      case T.PatientID: out.patientId = texto(bytes); break;
      case T.StudyDescription: out.studyDescription = texto(bytes); break;
      case T.SeriesDescription: out.seriesDescription = texto(bytes); break;
      case T.ModalitiesInStudy: out.modalities = texto(bytes); break;
      case T.Modality: out.modality = texto(bytes); break;
      case T.StudyInstanceUID: out.studyUid = texto(bytes); break;
      case T.SeriesInstanceUID: out.seriesUid = texto(bytes); break;
      case T.SOPInstanceUID: out.sopUid = texto(bytes); break;
      case T.SOPClassUID: out.sopClass = texto(bytes); break;
      default: break;
    }
  }

  // Lo que ya basta para llenar una fila de la tabla.
  function completo(out) {
    return !!(out.studyUid && out.patientId !== undefined && out.sopUid &&
      (out.modalities || out.modality) && (out.studyDescription || out.seriesDescription));
  }

  /*
   * Recorre un dataset desde `p` y devuelve la posición en que se cortó.
   *
   *   explicit / be : VR explícito y orden de bytes; los ítems anidados heredan los del padre.
   *   modo          : "meta" — grupo 0002, siempre explícito LE, termina al cambiar de grupo.
   *                   "data" — dataset principal, termina al llegar a los píxeles.
   *                   "seq"  — dentro de una secuencia de longitud indefinida; sale en (FFFE,E0DD).
   *                   "item" — dentro de un ítem de longitud indefinida; sale en (FFFE,E00D).
   *   prof          : qué tan anidado se va; corta por seguridad si el archivo está corrupto.
   *
   * Dentro de secuencias e ítems las etiquetas no son del estudio, así que se recorren en un
   * cuaderno aparte (`destino`) y solo se usan para avanzar. Antes se "saltaban a ciegas": un
   * ítem de longitud indefinida se leía como si fuera un valor normal, el puntero salía
   * disparado y se perdían el nombre del paciente y el StudyInstanceUID que vienen después.
   * Eso era lo que mandaba a la basura los estudios descargados del PACS (Philips guarda así
   * sus ProcedureCodeSequence / ReferencedSeriesSequence).
   */
  function recorrer(buf, dv, p, explicit, be, out, modo, prof) {
    const fin = buf.length;
    const destino = (modo === "meta" || modo === "data") ? out : {};
    prof = prof || 0;

    while (p + 8 <= fin) {
      const inicio = p;
      const group = dv.getUint16(p, !be);
      const elem = dv.getUint16(p + 2, !be);
      p += 4;

      // El grupo 0002 (metadatos de archivo) termina donde empieza cualquier otro grupo.
      if (modo === "meta" && group !== 0x0002) return inicio;

      // Delimitadores de ítem y de secuencia: van en los tres niveles y no llevan VR.
      if (group === 0xFFFE) {
        if (p + 4 > fin) return fin;
        const l = dv.getUint32(p, !be);
        p += 4;
        if (elem === 0xE0DD) return modo === "seq" ? p : fin;
        if (elem === 0xE00D) return modo === "item" ? p : fin;
        if (elem === 0xE000) {
          if (l === 0xFFFFFFFF) {
            if (prof >= MAX_PROFUNDIDAD) return fin;
            p = recorrer(buf, dv, p, explicit, be, destino, "item", prof + 1);
          } else {
            p += l;
          }
        }
        if (p <= inicio) return fin;                 // no avanzó: archivo torcido, se corta
        continue;
      }

      let vr = "";
      let len;
      if (explicit) {
        if (p + 2 > fin) return fin;
        vr = String.fromCharCode(buf[p], buf[p + 1]);
        p += 2;
        if (VR_LARGA.has(vr)) {
          p += 2;                                   // reservados
          if (p + 4 > fin) return fin;
          len = dv.getUint32(p, !be);
          p += 4;
        } else {
          if (p + 2 > fin) return fin;
          len = dv.getUint16(p, !be);
          p += 2;
        }
      } else {
        if (p + 4 > fin) return fin;
        len = dv.getUint32(p, !be);
        p += 4;
      }

      const clave = ((group << 16) | elem) >>> 0;

      if (len === 0xFFFFFFFF) {
        // Longitud indefinida: una secuencia o los píxeles encapsulados. Los píxeles cortan aquí,
        // después de ellos ya no va ninguna etiqueta del estudio.
        if (clave === T.PixelData) return p;
        if (prof >= MAX_PROFUNDIDAD) return fin;
        p = recorrer(buf, dv, p, explicit, be, destino, "seq", prof + 1);
        if (p <= inicio) return fin;
        continue;
      }
      if (len < 0 || p + len > fin) return fin;     // el corte del slice cayó en medio del valor

      if (clave === T.PixelData) return p;          // ya no hay etiquetas de estudio después

      capturar(clave, buf.subarray(p, p + len), destino);
      p += len;
      if (destino === out && completo(out)) return p;
    }
    return p;
  }

  function arrancaComoDataset(buf) {
    // Un dataset sin preámbulo empieza en un grupo par conocido: 0002 (raro), 0008, 0010, 0020…
    if (buf.length < 8) return false;
    const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    const group = dv.getUint16(0, true);
    return group === 0x0008 || group === 0x0010 || group === 0x0020 || group === 0x0002;
  }

  // Analiza una ventana de bytes del archivo. Devuelve las etiquetas y si el corte quedó corto.
  function analiza(bytes) {
    const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const out = {};

    const magic = bytes[128] === 0x44 && bytes[129] === 0x49 && bytes[130] === 0x43 && bytes[131] === 0x4D;
    let p;
    let explicit = true;
    let be = false;

    if (magic) {
      p = 132;
      p = recorrer(bytes, dv, p, true, false, out, "meta", 0);   // grupo 0002: siempre explícito LE
      const ts = out.ts || "";
      if (ts === "1.2.840.10008.1.2") explicit = false;
      else if (ts === "1.2.840.10008.1.2.2") { explicit = true; be = true; }
      else explicit = true;                                      // 1.2.840.10008.1.2.1 y demás
    } else if (arrancaComoDataset(bytes)) {
      p = 0;                                                     // sin preámbulo: dataset pelado
    } else {
      return { out: out, descarta: true, corto: false };
    }

    const inicioDataset = p;
    let donde = recorrer(bytes, dv, inicioDataset, explicit, be, out, "data", 0);

    if (!out.studyUid && !magic) {
      // Sin preámbulo no hay sintaxis de transferencia que lo diga: se prueba con el VR contrario.
      donde = recorrer(bytes, dv, inicioDataset, !explicit, be, out, "data", 0);
    }

    return { out: out, descarta: false, corto: donde + 8 >= bytes.length };
  }

  async function recorte(file, limite) {
    return new Uint8Array(await file.slice(0, Math.min(file.size, limite)).arrayBuffer());
  }

  /**
   * Lee la cabecera de un File de DICOM.
   * Resuelve null si el archivo no parece DICOM (así la pantalla lo lista como descartado).
   */
  async function leer(file) {
    if (!file || file.size < 8) return null;

    let bytes = await recorte(file, MAX_CABECERA);
    let r = analiza(bytes);
    // Cabecera más larga que la ventana (secuencias de más peso, privadas): una segunda leída ancha.
    if (!r.descarta && !r.out.studyUid && r.corto && file.size > bytes.length) {
      bytes = await recorte(file, MAX_AMPLIA);
      r = analiza(bytes);
    }
    if (r.descarta) return null;

    const out = r.out;
    if (!out.studyUid) return null;

    const descripcion = out.studyDescription || out.seriesDescription || "";
    return {
      studyUid: out.studyUid,
      sopUid: out.sopUid || "",
      nombre: nombreLegible(out.patientName),
      pacienteBruto: out.patientName || "",
      patientId: out.patientId || "",
      descripcion: descripcion,
      modalidad: modalidadLegible(out.modalities, out.modality),
      seriesUid: out.seriesUid || ""
    };
  }

  window.DicomCab = { leer: leer, nombreLegible: nombreLegible };
})(window);
