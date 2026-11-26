using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using System.Globalization;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Patrón técnico sintético: los píxeles se generan aquí, nunca vienen de un estudio real.
/// La **serie y la instancia son nuevas en cada envío**; el **estudio** es el programado cuando
/// la prueba se manda sobre una entrada de la lista elegida y el operador marcó esa casilla (así el
/// destino no abre un segundo registro bajo el mismo acceso). Qué dato de la entrada llega a escribirse
/// en la imagen lo decide el menú de casillas de la pantalla: lo no marcado se queda en el valor técnico
/// por defecto (identidad sintética, Modality OT, descripción de prueba, fecha de ahora, estudio nuevo).
/// El método devuelve, además del archivo, la lista de lo que realmente viajó, para el log de la pantalla.
/// </summary>
public static class ContrastTestImage
{
    public const int Size = 512;

    // Valores técnicos por defecto: lo que lleva la imagen cuando un dato no viaja.
    public const string DescripcionPrueba = "PRUEBA TECNICA - NO DIAGNOSTICO";
    public const string ModalidadPrueba = "OT";

    public static byte[] Pixels()
    {
        var pixels = new byte[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var distance = Math.Sqrt(Math.Pow(x - 255.5, 2) + Math.Pow(y - 255.5, 2));
            pixels[y * Size + x] = distance <= 92 ? (byte)240
                : distance <= 128 ? (byte)180 : distance <= 164 ? (byte)112
                : distance <= 196 ? (byte)48 : (byte)0;
        }
        return pixels;
    }

    // Identidad de la imagen cuando la prueba no usa datos de nadie.
    private static readonly WorklistPruebaDatos Anonima = new()
    {
        Origen = WorklistPruebaDatos.Anonima,
        Paciente = "PRUEBA^CIRCULO",
        IdPaciente = "PRUEBA-NO-CLINICA",
        Acceso = ""
    };

    public static PruebaImagen Create(WorklistPruebaDatos? datos = null)
    {
        var identidad = datos is null || datos.EsAnonima ? Anonima : datos;
        var ahora = DateTime.Now;
        var date = ahora.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = ahora.ToString("HHmmss", CultureInfo.InvariantCulture);
        var campos = new List<PruebaCampo>();

        // Una casilla marcada = el dato de la entrada va a la imagen tal cual llegó del nodo.
        // Desmarcada (o la entrada no lo trajo) = se queda el valor técnico por defecto, que es lo que
        // llevaría una prueba anónima. `Elige` deja apuntado en el log lo que pasó y por qué.
        string Elige(string clave, string etiqueta, string? valor, string defecto, string motivoAusente,
            string notaViaje = "")
        {
            var texto = SoloTexto(valor);
            if (!identidad.Incluye(clave))
            {
                campos.Add(new(clave, etiqueta, defecto, false, "No marcaste esta casilla: viaja el valor por defecto de la prueba."));
                return defecto;
            }
            if (texto.Length == 0)
            {
                campos.Add(new(clave, etiqueta, defecto, false, motivoAusente));
                return defecto;
            }
            campos.Add(new(clave, etiqueta, texto, true, notaViaje));
            return texto;
        }

        var paciente = Elige("nombre", "Nombre", identidad.Paciente, Anonima.Paciente!,
            identidad.EsSeleccion ? "La entrada no trajo nombre." : "No hay nombre que regresar.");
        var idPaciente = Elige("id", "ID del paciente", identidad.IdPaciente, Anonima.IdPaciente!,
            identidad.EsSeleccion ? "La entrada no trajo ID de paciente." : "No hay ID que regresar.");
        var acceso = Elige("acceso", "Número de acceso", identidad.Acceso, "",
            identidad.EsSeleccion ? "La entrada no trajo número de acceso." : "La prueba va sin número de acceso.");

        // VR LO admite hasta 64 caracteres: si el procedimiento del nodo es más largo, se recorta y se avisa
        // en el log en lugar de rechazar el envío o mandar un tag inválido.
        var procedimiento = SoloTexto(identidad.Procedimiento);
        var recortado = procedimiento.Length > 64;
        if (recortado) procedimiento = procedimiento[..64].TrimEnd();
        var descripcion = Elige("procedimiento", "Procedimiento", procedimiento, DescripcionPrueba,
            identidad.EsSeleccion ? "La entrada no trajo procedimiento." : "No hay procedimiento que regresar.",
            recortado ? "El procedimiento mide más de 64 caracteres (VR LO): viaja recortado." : "");
        var modalidad = Elige("modalidad", "Modalidad", identidad.Modalidad, ModalidadPrueba,
            identidad.EsSeleccion ? "La entrada no trajo modalidad." : "No hay modalidad que regresar.");

        // Fecha y hora programadas: una sola casilla para las dos. La fecha de **toma de la imagen**
        // (ContentDate/ContentTime) sigue siendo ahora: eso no se disfraza.
        var cita = identidad.EsSeleccion && identidad.Incluye("programado");
        var fechaProgramada = cita && EsFecha(identidad.FechaProgramada) ? identidad.FechaProgramada!.Trim() : "";
        var horaProgramada = cita && EsHora(identidad.HoraProgramada) ? identidad.HoraProgramada!.Trim() : "";
        var estudioDate = fechaProgramada.Length > 0 ? fechaProgramada : date;
        var estudioTime = horaProgramada.Length > 0 ? PaddedTime(horaProgramada) : time;
        if (!cita || (fechaProgramada.Length == 0 && horaProgramada.Length == 0))
            campos.Add(new("programado", "Fecha y hora programadas", $"{dicomDate(date)} {dicomTime(time)}", false,
                !cita ? "No marcaste esta casilla: el estudio lleva la fecha y la hora de ahora."
                    : "La entrada no trajo una fecha ni una hora con formato DICOM."));
        else
            campos.Add(new("programado", "Fecha y hora programadas",
                $"{dicomDate(estudioDate)} {dicomTime(estudioTime)}", true,
                fechaProgramada.Length == 0 ? "La entrada no trajo fecha: el estudio lleva la de ahora."
                : horaProgramada.Length == 0 ? "La entrada no trajo hora: el estudio lleva la de ahora."
                : "La fecha y la hora de toma de la imagen siguen siendo las de ahora."));

        // Con una entrada de la lista elegida y la casilla marcada, el estudio **es el programado**: el destino
        // no abre un segundo registro bajo el mismo acceso, igual que haría el equipo real. La **serie y la
        // instancia siempre son nuevas**, así que la prueba nunca reemplaza una imagen que ya estuviera dentro.
        var programado = identidad.EsSeleccion && identidad.Incluye("estudioUid") ? SoloTexto(identidad.EstudioUid) : "";
        var estudioNuevo = !EsUidValido(programado);
        var estudioUid = estudioNuevo ? DicomUIDGenerator.GenerateDerivedFromUUID().UID : programado;
        var motivoUid = !identidad.EsSeleccion ? "El origen de la prueba no es una entrada de la lista."
            : !identidad.Incluye("estudioUid") ? "No marcaste esta casilla: se crea un estudio nuevo."
            : programado.Length == 0 ? "El nodo no envió el UID del estudio: se crea uno nuevo."
            : !EsUidValido(programado) ? "El UID que envió el nodo no tiene formato DICOM: se crea un estudio nuevo."
            : "";
        campos.Add(new("estudioUid", "UID del estudio", estudioUid, !estudioNuevo, motivoUid));

        var ds = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.StudyInstanceUID, estudioUid },
            { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.PatientName, paciente },
            { DicomTag.PatientID, idPaciente },
            { DicomTag.PatientBirthDate, "" },
            { DicomTag.PatientSex, "" },
            { DicomTag.AccessionNumber, acceso },
            { DicomTag.ReferringPhysicianName, "" },
            { DicomTag.StudyID, "PRUEBA" },
            // Sin casilla marcada, la descripción sigue diciendo en grande que esto no es un estudio clínico.
            { DicomTag.StudyDescription, descripcion },
            { DicomTag.SeriesDescription, "CIRCULO DE CONTRASTE SINTETICO" },
            { DicomTag.StudyDate, estudioDate },
            { DicomTag.StudyTime, estudioTime },
            { DicomTag.SeriesDate, date },
            { DicomTag.SeriesTime, time },
            { DicomTag.ContentDate, date },
            { DicomTag.ContentTime, time },
            { DicomTag.InstanceCreationDate, date },
            { DicomTag.InstanceCreationTime, time },
            { DicomTag.Modality, modalidad },
            { DicomTag.ConversionType, "SYN" },
            { DicomTag.Manufacturer, "Meditechnosys" },
            { DicomTag.ImageType, new[] { "DERIVED", "SECONDARY" } },
            { DicomTag.PatientOrientation, "" },
            { DicomTag.SeriesNumber, "1" },
            { DicomTag.InstanceNumber, "1" },
            { DicomTag.BurnedInAnnotation, "NO" },
            { DicomTag.LossyImageCompression, "00" },
            { DicomTag.Rows, (ushort)Size },
            { DicomTag.Columns, (ushort)Size },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.BitsAllocated, (ushort)8 },
            { DicomTag.BitsStored, (ushort)8 },
            { DicomTag.HighBit, (ushort)7 },
            { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.WindowCenter, "128" },
            { DicomTag.WindowWidth, "256" }
        };
        DicomPixelData.Create(ds, true).AddFrame(new MemoryByteBuffer(Pixels()));
        return new PruebaImagen(new DicomFile(ds), campos);
    }

    // Lo que el operador ve en el log: el archivo y la lista de datos que realmente fueron a la imagen.
    public sealed record PruebaImagen(DicomFile Archivo, IReadOnlyList<PruebaCampo> Campos);

    private static string SoloTexto(string? valor) => (valor ?? "").Trim();

    private static bool EsFecha(string? valor)
    {
        if (valor is not { Length: 8 } || !valor.All(char.IsDigit)) return false;
        return int.Parse(valor.AsSpan(0, 4)) > 0
            && int.Parse(valor.AsSpan(4, 2)) is >= 1 and <= 12
            && int.Parse(valor.AsSpan(6, 2)) is >= 1 and <= 31;
    }

    // VR TM: HH, HHMM, HHMMSS y fracción de segundo opcional. Se acepta la precisión reducida tal como
    // la mandó el nodo; lo que no sea eso, no viaja.
    private static bool EsHora(string? valor)
    {
        if (valor is not { Length: >= 2 and <= 15 }) return false;
        var partes = valor.Split('.', 2);
        var digitos = partes[0];
        if (digitos.Length % 2 != 0 || digitos.Length > 6 || !digitos.All(char.IsDigit)) return false;
        if (int.Parse(digitos.AsSpan(0, 2)) > 23) return false;
        if (digitos.Length >= 4 && int.Parse(digitos.AsSpan(2, 2)) > 59) return false;
        if (digitos.Length == 6 && int.Parse(digitos.AsSpan(4, 2)) > 60) return false;
        return partes.Length == 1
            || (partes[1].Length is >= 1 and <= 6 && partes[1].All(char.IsDigit));
    }

    // TM con precisión reducida: '0930' se escribe '093000' para que el tag sea aprovechable en el
    // destino, sin inventar minutos ni segundos que el nodo no mandó (quedan en 00 y el log lo dice).
    private static string PaddedTime(string valor)
    {
        var baseHora = valor.Contains('.') ? valor.Split('.', 2)[0] : valor;
        var fraccion = valor.Contains('.') ? valor[baseHora.Length..] : "";
        return baseHora.Length switch
        {
            2 => baseHora + "0000" + fraccion,
            4 => baseHora + "00" + fraccion,
            _ => baseHora + fraccion
        };
    }

    private static string dicomDate(string valor) => valor.Length == 8
        ? $"{valor.Substring(6, 2)}/{valor.Substring(4, 2)}/{valor.Substring(0, 4)}" : valor;

    private static string dicomTime(string valor) => valor.Length switch
    {
        2 => $"{valor} h",
        4 => $"{valor.Substring(0, 2)}:{valor.Substring(2, 2)}",
        _ when valor.Length >= 6 => $"{valor.Substring(0, 2)}:{valor.Substring(2, 2)}:{valor.Substring(4, 2)}",
        _ => valor
    };

    // VR UI: dígitos separados por puntos, hasta 64, con algún dígito y sin grupos vacíos. Se acepta
    // tal como el nodo lo devolvió —los ceros a la izquierda y los arcos largos existen en la calle—;
    // si aun así no es un UID, la prueba se manda en un estudio nuevo en lugar de fallar.
    private static bool EsUidValido(string valor) =>
        valor.Length is > 0 and <= 64
        && valor.Any(char.IsDigit)
        && valor.All(c => char.IsDigit(c) || c == '.')
        && !valor.StartsWith('.') && !valor.EndsWith('.') && !valor.Contains("..");

    // BMP sin códecs ni System.Drawing: la vista previa usa exactamente los mismos píxeles DICOM.
    public static byte[] PreviewBmp()
    {
        var pixels = Pixels();
        var rowSize = (Size * 3 + 3) & ~3;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(54 + rowSize * Size);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(Size);
        writer.Write(Size);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(rowSize * Size);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (var y = Size - 1; y >= 0; y--)
        {
            for (var x = 0; x < Size; x++)
            {
                var value = pixels[y * Size + x];
                writer.Write(value); writer.Write(value); writer.Write(value);
            }
            for (var p = Size * 3; p < rowSize; p++) writer.Write((byte)0);
        }
        return stream.ToArray();
    }
}
