using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>SCU MWL; no abre receptores, descarga estudios ni modifica la lista remota.</summary>
public sealed class WorklistService : IWorklistService
{
    public const int MaxResults = 5000;
    // También limita respuestas descartadas de servidores que ignoren los filtros.
    private const int MaxResponses = 20000;

    private static IDicomClient Client(WorklistConnection connection)
    {
        var client = DicomClientFactory.Create(connection.Host.Trim(), connection.Puerto, false,
            connection.AeLocal.Trim(), connection.AeRemoto.Trim());
        client.ClientOptions.AssociationRequestTimeoutInMs = 10000;
        client.ClientOptions.AssociationReleaseTimeoutInMs = 2000;
        client.ClientOptions.MaximumNumberOfRequestsPerAssociation = 1;
        return client;
    }

    public static DicomCFindRequest CreateQuery(WorklistSearchRequest request)
    {
        // Sin QueryRetrieveLevel: esta SOP Class es MWL, nunca Study Root/PACS.
        var step = new DicomDataset
        {
            { DicomTag.ScheduledStationAETitle, "" }, // No restringir a MEDI: es solo el calling AE.
            // Rango DA del periodo elegido; vacío = todas las fechas programadas.
            { DicomTag.ScheduledProcedureStepStartDate, RangoFechas(request) },
            { DicomTag.ScheduledProcedureStepStartTime, "" },
            { DicomTag.Modality, (request.Modalidad ?? "").Trim() },
            { DicomTag.ScheduledProcedureStepDescription, "" },
            { DicomTag.ScheduledProcedureStepID, "" },
            { DicomTag.ScheduledProcedureStepStatus, "SCHEDULED" }
        };
        return new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind)
        {
            Dataset = new DicomDataset
            {
                { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
                { DicomTag.PatientName, "" },
                { DicomTag.PatientID, "" },
                { DicomTag.AccessionNumber, "" },
                { DicomTag.RequestedProcedureDescription, "" },
                { DicomTag.RequestedProcedureID, "" },
                { DicomTag.StudyInstanceUID, "" },
                new DicomSequence(DicomTag.ScheduledProcedureStepSequence, step)
            }
        };
    }

    private static string Text(DicomDataset dataset, DicomTag tag) =>
        dataset.GetSingleValueOrDefault(tag, "").Trim();

    private static string Stations(DicomDataset step) =>
        step.TryGetValues<string>(DicomTag.ScheduledStationAETitle, out var values)
            ? string.Join(", ", values.Select(v => v.Trim()).Where(v => v.Length > 0)) : "";

    // Rango DA del tipo AAAAMMDD-AAAAMMDD; un solo extremo admite la forma abierta AAAAMMDD-.
    internal static string RangoFechas(WorklistSearchRequest request) => (request.Desde, request.Hasta) switch
    {
        (null, null) => "",
        (string desde, null) => desde + "-",
        (null, string hasta) => "-" + hasta,
        (string desde, string hasta) => desde + "-" + hasta
    };

    public async Task<WorklistSearchResult> SearchAsync(WorklistSearchRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var client = Client(request.Conexion);
        var query = CreateQuery(request);
        var items = new List<WorklistItem>();
        var missing = 0;
        var other = 0;
        var responses = 0;
        var limited = false;
        DicomStatus? finalStatus = null;
        var modality = (request.Modalidad ?? "").Trim();
        query.OnResponseReceived = (_, response) =>
        {
            if (response.Status.State != DicomState.Pending)
            {
                finalStatus = response.Status;
                return;
            }
            if (limited) return;
            responses++;
            var ds = response.Dataset;
            if (ds is null || !ds.TryGetSequence(DicomTag.ScheduledProcedureStepSequence, out var sequence)
                || sequence.Items.Count == 0)
            {
                missing++;
            }
            else foreach (var step in sequence.Items)
            {
                var stepModality = Text(step, DicomTag.Modality);
                if (modality.Length > 0 && !string.Equals(modality, stepModality, StringComparison.OrdinalIgnoreCase))
                    continue; // Algunos SCP ignoran claves opcionales; se vuelve a filtrar localmente.
                var status = Text(step, DicomTag.ScheduledProcedureStepStatus);
                if (status.Length == 0) { missing++; continue; }
                if (!string.Equals(status, "SCHEDULED", StringComparison.OrdinalIgnoreCase)) { other++; continue; }
                if (items.Count == MaxResults) { limited = true; break; }
                var procedure = Text(step, DicomTag.ScheduledProcedureStepDescription);
                if (procedure.Length == 0) procedure = Text(ds, DicomTag.RequestedProcedureDescription);
                items.Add(new WorklistItem(Text(ds, DicomTag.PatientName), Text(ds, DicomTag.PatientID),
                    Text(ds, DicomTag.AccessionNumber), procedure, stepModality,
                    Text(step, DicomTag.ScheduledProcedureStepStartDate),
                    Text(step, DicomTag.ScheduledProcedureStepStartTime),
                    Stations(step), "SCHEDULED", Text(ds, DicomTag.StudyInstanceUID)));
            }
            if (responses >= MaxResponses) limited = true;
            if (limited) deadline.Cancel();
        };
        await client.AddRequestAsync(query);
        try
        {
            await client.SendAsync(deadline.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
        }
        catch (Exception) when (limited && !ct.IsCancellationRequested) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new WorklistException("El servidor no terminó la consulta en 60 segundos. No se muestra una lista incompleta como si fuera completa.", true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            throw new WorklistException("No se pudo consultar la lista de trabajo. Revisa la conexión, el AE Title y que el servidor admita MWL.");
        }
        ct.ThrowIfCancellationRequested();
        if (!limited && deadline.IsCancellationRequested)
            throw new WorklistException("Se agotó el tiempo de consulta de la lista de trabajo.", true);
        if (!limited && finalStatus != DicomStatus.Success)
            throw new WorklistException("El servidor no confirmó una consulta MWL completa. Revisa que admita la lista de trabajo y el filtro de estado SCHEDULED.");
        return new WorklistSearchResult(items.OrderBy(i => i.Fecha).ThenBy(i => i.Hora).ToList(), missing, other, limited);
    }

    public async Task<bool> EchoAsync(WorklistConnection connection, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var client = Client(connection);
            DicomStatus? status = null;
            await client.AddRequestAsync(new DicomCEchoRequest { OnResponseReceived = (_, r) => status = r.Status });
            await client.SendAsync(deadline.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
            ct.ThrowIfCancellationRequested();
            return status == DicomStatus.Success;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    public async Task<WorklistTestResult> SendTestAsync(WorklistTestRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var connection = request.Conexion;
        DicomStatus? status = null;
        IReadOnlyList<PruebaCampo> campos = Array.Empty<PruebaCampo>();
        var tags = new PruebaTags("", "", "", "");
        try
        {
            var client = Client(connection);
            // La imagen y el recuento de lo que realmente viajó escrito en ella: eso es lo que el registro
            // guarda y lo que después se le pregunta al destino. Con una entrada marcada y su casilla puesta,
            // el estudio es el programado; serie e instancia son siempre nuevas.
            var imagen = ContrastTestImage.Create(request.Datos);
            campos = imagen.Campos;
            var file = imagen.Archivo;
            tags = TagsDe(file.Dataset);
            await client.AddRequestAsync(new DicomCStoreRequest(file) { OnResponseReceived = (_, r) => status = r.Status });
            // Una sola petición, sin reenvíos automáticos. Cada clic confirmado crea serie e instancia nuevas.
            await client.SendAsync(deadline.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Si llegó respuesta, conserva su significado aunque falle el cierre de asociación.
        }
        ct.ThrowIfCancellationRequested();
        // Interceptar lo que de verdad quedó en el PACS: se le pregunta al mismo destino, con su propio
        // tiempo, y su respuesta acompaña al resultado del envío (y al registro permanente).
        var verificacion = await ConsultarPacsAsync(connection, tags, ct);
        if (status == DicomStatus.Success)
            return new("enviada", "Imagen de prueba enviada y aceptada por el destino.", campos, tags, verificacion);
        if (status?.State == DicomState.Warning)
            return new("advertencia", "El destino aceptó la imagen con una advertencia DICOM. Comprueba el estudio de prueba antes de volver a enviar.", campos, tags, verificacion);
        if (status is not null)
            return new("fallida", "El destino rechazó la imagen de prueba (DICOM " + status.Code.ToString("X4") + ").", campos, tags, verificacion);
        return new("incierta", "No se recibió confirmación del destino. La imagen podría haber llegado; la consulta de abajo dice qué tiene el PACS.", campos, tags, verificacion);
    }

    /// <summary>
    /// Después de cada prueba se consulta al **mismo destino** qué guarda de ese estudio. Es un C-FIND de
    /// Study Root a nivel STUDY (SOP Class 1.2.840.10008.5.1.4.1.2.2.1), buscado por el StudyInstanceUID que
    /// quedó escrito en la imagen: no es la lista de trabajo, aquí sí se pregunta al archivo del PACS.
    /// **Nunca lanza**: si el nodo no admite esa consulta, la verificación lo dice en palabras y el registro
    /// guarda esa frase, en lugar de inventar un contenido o romper el resultado del envío.
    /// </summary>
    internal static async Task<PruebaVerificacion> ConsultarPacsAsync(
        WorklistConnection connection, PruebaTags tags, CancellationToken ct)
    {
        const string sinUid = "La imagen no salió con un UID de estudio aprovechable, así que no hay nada que preguntarle al destino.";
        if (tags.EstudioUid.Length == 0) return new(false, sinUid, Array.Empty<PruebaEstudioPacs>());
        var estudios = new List<PruebaEstudioPacs>();
        DicomStatus? final = null;
        var plazoVencido = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var client = Client(connection);
            // Solo el UID como clave de búsqueda: si además se exigiera el acceso y el PACS lo escribe
            // distinto, la respuesta «no hay nada» sería mentira.
            var query = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
            query.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, tags.EstudioUid);
            foreach (var retorno in new[]
                     {
                         DicomTag.AccessionNumber, DicomTag.StudyDate, DicomTag.StudyTime,
                         DicomTag.ModalitiesInStudy, DicomTag.StudyDescription,
                         DicomTag.NumberOfStudyRelatedInstances
                     })
                query.Dataset.AddOrUpdate(retorno, "");
            query.OnResponseReceived = (_, response) =>
            {
                if (response.Status.State != DicomState.Pending || !response.HasDataset)
                {
                    final = response.Status;
                    return;
                }
                var ds = response.Dataset;
                estudios.Add(new PruebaEstudioPacs(Text(ds, DicomTag.StudyInstanceUID),
                    Text(ds, DicomTag.AccessionNumber), Text(ds, DicomTag.StudyDate),
                    Text(ds, DicomTag.StudyTime), Text(ds, DicomTag.ModalitiesInStudy),
                    Text(ds, DicomTag.StudyDescription),
                    int.TryParse(Text(ds, DicomTag.NumberOfStudyRelatedInstances), out var n) ? n : 0));
            };
            await client.AddRequestAsync(query);
            await client.SendAsync(deadline.Token, DicomClientCancellationMode.ImmediatelyAbortAssociation);
            // Al cumplirse el plazo, fo-dicom aborta la asociación y SendAsync vuelve sin lanzar: la
            // consulta simplemente se quedó a medias. Se anota aquí, mientras el token sigue a mano.
            plazoVencido = deadline.IsCancellationRequested;
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "El destino no terminó la consulta de verificación en 15 segundos; no se sabe qué guarda.",
                Array.Empty<PruebaEstudioPacs>());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return new(false, "El destino no admitió la consulta de verificación (Study Root). El envío anterior no cambia: "
                              + "lo que sigue sin comprobarse es qué guarda el PACS.", Array.Empty<PruebaEstudioPacs>());
        }
        if (plazoVencido)
            // El nodo no contestó dentro del plazo: no se puede decir que «no tiene nada guardado», porque
            // tampoco se le oyó decir nada. Es la misma frase que cuando la cancelación lanza la excepción.
            return new(false, "El destino no terminó la consulta de verificación en 15 segundos; no se sabe qué guarda.",
                Array.Empty<PruebaEstudioPacs>());
        if (final is not null && final.State == DicomState.Failure)
        {
            // 0122 es el código DICOM estándar para «esa clase de servicio no existe aquí»: el nodo no ofrece
            // consulta de estudios archivados. No es un rechazo del estudio enviado, así que se dice distinto.
            if (final.Code == 0x0122)
                return new(false, "El destino no admitió la consulta de verificación (Study Root): respondió DICOM 0122, "
                                  + "SOP class not supported. El envío anterior no cambia: lo que sigue sin comprobarse "
                                  + "es qué guarda el PACS.", Array.Empty<PruebaEstudioPacs>());
            return new(false, $"El destino rechazó la consulta de verificación (DICOM {final.Code.ToString("X4")}: {final}). "
                              + "Aquí no se puede saber qué guardó.", Array.Empty<PruebaEstudioPacs>());
        }
        if (estudios.Count == 0)
            return new(true, "El destino respondió a la verificación y no devuelve ninguna entrada con ese UID de estudio.",
                Array.Empty<PruebaEstudioPacs>());
        return new(true, $"El destino dice tener {estudios.Count} "
            + (estudios.Count == 1 ? "entrada" : "entradas") + " con ese UID de estudio.", estudios);
    }

    private static PruebaTags TagsDe(DicomDataset dataset) => new(Text(dataset, DicomTag.StudyInstanceUID),
        Text(dataset, DicomTag.SeriesInstanceUID), Text(dataset, DicomTag.SOPInstanceUID),
        Text(dataset, DicomTag.AccessionNumber));

}
