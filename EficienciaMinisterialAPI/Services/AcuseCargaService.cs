using System.Globalization;
using System.Text;

using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;

// PDF generado en memoria siguiendo el comprobante institucional de FESE.
public sealed class AcuseCargaService
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-MX");
    private readonly List<StringBuilder> paginas = [];
    private StringBuilder contenido = new();
    private double y;
    private bool previo;
    private const double Izquierda = 54;
    private const double Derecha = 558;

    public static string NombreArchivo(CargaInfo carga, string prefijo, string extension)
    {
        var estado = carga.Estado == "CONFIRMADA" ? "" : "_Vista_previa";
        return $"{prefijo}_PrimerRespondiente_{carga.PeriodoInicio:dd-MM-yyyy}_al_{carga.PeriodoFin:dd-MM-yyyy}{estado}.{extension}";
    }

    public static byte[] Crear(CargaInfo carga)
    {
        var doc = new AcuseCargaService { previo = carga.Estado != "CONFIRMADA" };
        var fechaUtc = DateTime.SpecifyKind(carga.ConfirmadoUtc ?? carga.CreadoUtc, DateTimeKind.Utc);
        var fecha = TimeZoneInfo.ConvertTimeFromUtc(fechaUtc, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)"));
        doc.NuevaPagina();
        doc.Texto(fecha.ToString("dd 'de' MMMM 'de' yyyy, HH:mm", Cultura) + " h, Ciudad de México", Derecha, 698, 10, derecha: true);
        doc.y = 658;
        doc.Parrafo(doc.previo ? "VISTA PREVIA DEL ACUSE" : "ACUSE DE RECEPCIÓN DE INFORMACIÓN", 15, 22);
        doc.y -= 12;
        doc.Parrafo("Primer Respondiente - Eficiencia Ministerial", 12, 18);
        doc.y -= 16;
        doc.Parrafo(doc.previo
            ? "El archivo fue validado. Revise las observaciones antes de confirmar la entrega. Este documento previo no acredita su recepción definitiva ni asigna un folio."
            : "Se hace constar la recepción de la información de detenciones presentada por la institución indicada, correspondiente al periodo de entrega que se detalla a continuación.", 11, 16);
        doc.y -= 20;
        foreach(var dato in new[]
        {
            ("Folio", carga.Folio ?? "Sin asignar - pendiente de confirmación"),
            ("Institución", carga.Institucion),
            ("Periodo de entrega", $"{carga.PeriodoInicio:dd/MM/yyyy} al {carga.PeriodoFin:dd/MM/yyyy}"),
            ("Archivo presentado", carga.NombreArchivo),
            ("Registros", carga.TotalRegistros.ToString("N0", Cultura)),
            (doc.previo ? "Validación (hora de México)" : "Recepción (hora de México)", fecha.ToString("dd/MM/yyyy HH:mm:ss", Cultura))
        })
        {
            doc.Texto(dato.Item1, Izquierda, doc.y, 10, negrita: true);
            doc.y -= 18;
            doc.Parrafo(dato.Item2, 11, 16);
            doc.y -= 16;
        }
        doc.Linea(doc.y + 5);
        doc.y -= 16;
        doc.Parrafo("El cruce con RND está pendiente. Este comprobante no acredita la localización de los registros en RND ni su envío a fiscalías.", 10, 15);
        return doc.Generar();
    }

    private void NuevaPagina()
    {
        if (contenido.Length > 0) paginas.Add(contenido);
        contenido = new StringBuilder();
        if (previo)
        {
            // Mismo criterio de SIIID: PREVIO, rojo tenue, 72 pt y giro de 35°.
            var centroY = paginas.Count == 0 ? 316 : 341;
            var inicio = -Ancho("PREVIO", 72) / 2;
            contenido.Append(FormattableString.Invariant($"q 0.81915 0.57358 -0.57358 0.81915 306 {centroY} cm 1 0.8 0.8 rg BT /F2 72 Tf {inicio:0.##} 0 Td (PREVIO) Tj ET Q\n"));
        }
        contenido.Append("q 308 0 0 49.49 250 738 cm /Logo Do Q\n");
        Texto("Eficiencia Ministerial", Izquierda, 735, 12);
        Linea(728);
        y = 695;
    }
    private void Linea(double alto)
    {
        contenido.Append(FormattableString.Invariant($"0 0 0 RG 0.6 w 54 {alto} m 558 {alto} l S\n"));
    }
    private static double Ancho(string texto, double tamano)
    {
        // Métricas Helvetica estándar, compartidas por el cálculo de ajuste y PDF.
        return texto.Sum(c => Anchos[Math.Min((int)c, 255)]) * tamano / 1000;
    }
    private void Texto(string texto, double x, double alto, double tamano, bool derecha = false, bool negrita = false, string color = "0 0 0")
    {
        if (derecha) x -= Ancho(texto, tamano);
        var seguro = texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
        contenido.Append(FormattableString.Invariant($"{color} rg BT /{(negrita ? "F2" : "F1")} {tamano} Tf {x:0.##} {alto:0.##} Td ({seguro}) Tj ET\n"));
    }
    private void Parrafo(string texto, double tamano, double interlineado)
    {
        var restante = texto.Trim();
        while(restante.Length > 0)
        {
            var corte = 0;
            while(corte < restante.Length && Ancho(restante[..(corte + 1)], tamano) <= Derecha - Izquierda)
            {
                corte++;
            }
            if(corte < restante.Length && corte > 0)
            {
                var espacio = restante.LastIndexOf(' ', corte - 1, corte);
                if(espacio > 0)
                {
                    corte = espacio;
                }
            }
            corte = Math.Max(1, corte);
            EscribirLinea(restante[..corte].TrimEnd(), tamano, interlineado);
            restante = restante[corte..].TrimStart();
        }
    }
    private void EscribirLinea(string linea, double tamano, double interlineado)
    {
        if (y < 64) NuevaPagina();
        Texto(linea, Izquierda, y, tamano);
        y -= interlineado;
    }
    private byte[] Generar()
    {
        paginas.Add(contenido);
        using var recurso = typeof(AcuseCargaService).Assembly.GetManifestResourceStream("EficienciaMinisterialAPI.Assets.encabezado.base64")
            ?? throw new InvalidOperationException("Falta el encabezado institucional del comprobante.");
        using var lector = new StreamReader(recurso);
        var logo = Convert.FromBase64String(lector.ReadToEnd());
        var encoding = Encoding.Latin1;
        var objetos = new List<byte[]>();
        void Agregar(string objeto) => objetos.Add(encoding.GetBytes(objeto));
        Agregar("<< /Type /Catalog /Pages 2 0 R >>");
        Agregar($"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, paginas.Count).Select(i => $"{6 + i * 2} 0 R"))}] /Count {paginas.Count} >>");
        Agregar("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Agregar("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        using (var imagen = new MemoryStream())
        {
            imagen.Write(encoding.GetBytes($"<< /Type /XObject /Subtype /Image /Width 1500 /Height 241 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logo.Length} >>\nstream\n"));
            imagen.Write(logo);
            imagen.Write(encoding.GetBytes("\nendstream"));
            objetos.Add(imagen.ToArray());
        }
        for (var i = 0; i < paginas.Count; i++)
        {
            Agregar($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Logo 5 0 R >> >> /Contents {7 + i * 2} 0 R >>");
            var cuerpo = paginas[i].ToString() + $"0 0 0 rg BT /F1 9 Tf 303 28 Td ({i + 1}) Tj ET\n";
            Agregar($"<< /Length {encoding.GetByteCount(cuerpo)} >>\nstream\n{cuerpo}endstream");
        }
        using var pdf = new MemoryStream();
        void Escribir(string texto) => pdf.Write(encoding.GetBytes(texto));
        Escribir("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objetos.Count; i++)
        {
            offsets.Add(pdf.Position);
            Escribir($"{i + 1} 0 obj\n"); pdf.Write(objetos[i]); Escribir("\nendobj\n");
        }
        var xref = pdf.Position;
        Escribir($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Escribir($"{offset:0000000000} 00000 n \n");
        Escribir($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
    private static readonly int[] Anchos =
    [
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,
    ];
}
