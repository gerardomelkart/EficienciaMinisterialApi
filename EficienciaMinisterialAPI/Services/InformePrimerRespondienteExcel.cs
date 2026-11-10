using ClosedXML.Excel;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public static class InformePrimerRespondienteExcel
{
    public static string NombreArchivo(InformePr informe)
    {
        var inicio = informe.Resumen.Min(r => r.PeriodoInicio);
        var fin = informe.Resumen.Max(r => r.PeriodoFin);
        return $"Reporte_PrimerRespondiente_{inicio:dd-MM-yyyy}_al_{fin:dd-MM-yyyy}.xlsx";
    }

    public static byte[] Crear(InformePr informe)
    {
        if(informe.Totales.Registros > XLHelper.MaxRowNumber - 1)
            throw new ArgumentException("El reporte supera las filas permitidas por Excel. Seleccione una quincena o corporación.");
        using var libro = new XLWorkbook();
        var resumen = libro.AddWorksheet("Resumen");
        string[] cabeceras = ["INICIO ENTREGA", "FIN ENTREGA", "CORPORACION", "ENTREGAS", "REGISTROS REPORTADOS", "FOLIOS INFORMADOS DISTINTOS", "LOCALIZADOS RND", "NO LOCALIZADOS", "PENDIENTES DE CRUCE", "REGISTROS MARCADOS DUPLICADOS"];
        for(var i = 0; i < cabeceras.Length; i++) resumen.Cell(1, i + 1).Value = cabeceras[i];
        var fila = 2;
        foreach(var r in informe.Resumen)
        {
            resumen.Cell(fila, 1).Value = r.PeriodoInicio; resumen.Cell(fila, 2).Value = r.PeriodoFin;
            resumen.Cell(fila, 3).Value = r.Corporacion; resumen.Cell(fila, 4).Value = r.Entregas;
            resumen.Cell(fila, 5).Value = r.Registros; resumen.Cell(fila, 6).Value = r.Folios;
            resumen.Cell(fila, 7).Value = r.Localizados; resumen.Cell(fila, 8).Value = r.NoLocalizados;
            resumen.Cell(fila, 9).Value = r.Pendientes; resumen.Cell(fila, 10).Value = r.Duplicados;
            fila++;
        }
        resumen.Columns(1, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        var detalle = libro.AddWorksheet("Registros");
        string[] campos = ["INICIO ENTREGA", "FIN ENTREGA", "FOLIO ENTREGA", "CORPORACION", "FOLIO RND INFORMADO", "FECHA DETENCION", "NOMBRE INFORMADO", "ENTIDAD DETENCION", "AUTORIDAD PUESTA", "FUERO", "ESTADO CRUCE", "MARCADO DUPLICADO", "FECHA FUENTE RND"];
        for(var i = 0; i < campos.Length; i++) detalle.Cell(1, i + 1).Value = campos[i];
        fila = 2;
        foreach(var r in informe.Registros)
        {
            detalle.Cell(fila, 1).Value = r.PeriodoInicio; detalle.Cell(fila, 2).Value = r.PeriodoFin;
            detalle.Cell(fila, 3).Value = r.FolioEntrega; detalle.Cell(fila, 4).Value = r.Corporacion;
            detalle.Cell(fila, 5).Value = r.FolioRnd; detalle.Cell(fila, 6).Value = r.FechaDetencion;
            detalle.Cell(fila, 7).Value = r.NombreDetenido; detalle.Cell(fila, 8).Value = r.Entidad;
            detalle.Cell(fila, 9).Value = r.AutoridadPuesta; detalle.Cell(fila, 10).Value = r.Fuero;
            detalle.Cell(fila, 11).Value = r.EstadoCruce switch
            {
                "LOCALIZADO" => "Localizado en RND", "NO_LOCALIZADO" => "No localizado", _ => "Pendiente de cruce"
            }; detalle.Cell(fila, 12).Value = r.Duplicado ? "SI" : "NO";
            if(r.FechaFuente.HasValue) detalle.Cell(fila, 13).Value = r.FechaFuente.Value;
            fila++;
        }
        foreach(var col in new[] { 1, 2, 6, 13 }) detalle.Column(col).Style.DateFormat.Format = "dd/MM/yyyy";
        foreach(var hoja in libro.Worksheets)
        {
            hoja.SheetView.FreezeRows(1);
            hoja.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#691c32");
            hoja.Row(1).Style.Font.FontColor = XLColor.White;
            hoja.Row(1).Style.Font.Bold = true;
            hoja.Row(1).Height = 45;
            hoja.Row(1).Style.Alignment.WrapText = true;
            hoja.Columns().Width = 26;
            if(hoja.LastRowUsed()!.RowNumber() > 1) hoja.RangeUsed()!.CreateTable();
        }
        detalle.Column(7).Width = 44; detalle.Column(9).Width = 45;
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }
}
