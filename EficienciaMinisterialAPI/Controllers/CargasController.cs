using System.IO.Compression;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;
using EficienciaMinisterialAPI.Repositories;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize(Roles = "PRIMER_RESPONDIENTE,SUPER_USUARIO,CONSULTA")]
[Route("api/primer-respondiente")]
public sealed class CargasController(ExcelCargaService excel, CargaRepository cargas) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;

    [HttpGet("plantilla")]
    public IActionResult Plantilla()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("PLANTILLA");
        for(var i = 0; i < ExcelCargaService.Encabezados.Length; i++)
        {
            hoja.Cell(1, i + 1).Value = ExcelCargaService.Encabezados[i];
        }
        hoja.Range("A1:G1").Style.Fill.BackgroundColor = XLColor.FromHtml("#691c32");
        hoja.Range("A1:G1").Style.Font.FontColor = XLColor.White;
        hoja.Range("A1:G1").Style.Font.Bold = true;
        hoja.Range("A1:G1").Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 45;
        hoja.Columns().Width = 28;
        hoja.Column(4).Width = 45;
        hoja.Column(5).Width = 40;
        hoja.Column(3).Style.DateFormat.Format = "yyyy-mm-dd";
        hoja.SheetView.FreezeRows(1);
        var diccionario = libro.AddWorksheet("Diccionario");
        diccionario.Cell(1, 1).Value = "Campo";
        diccionario.Cell(1, 2).Value = "Regla de esta entrega";
        string[] reglas = ["SEDENA, SEMAR, GN o SSPC. Debe corresponder al usuario.", "AA/FC/000/DDMMAAAA/0000 o AA/FF/000/DDMMAAAA/0000. Sin IA/AI.", "Fecha de Excel, no texto.", "Texto libre, máximo 500 caracteres.", "Texto libre, máximo 300 caracteres.", "Entidad del catálogo nacional.", "Fuero común o Fuero federal."];
        for(var i = 0; i < reglas.Length; i++)
        {
            diccionario.Cell(i + 2, 1).Value = ExcelCargaService.Encabezados[i];
            diccionario.Cell(i + 2, 2).Value = reglas[i];
        }
        diccionario.Column(1).Width = 48;
        diccionario.Column(2).Width = 90;
        diccionario.Columns().Style.Alignment.WrapText = true;
        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return File(memoria.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Plantilla_PrimerRespondiente.xlsx");
    }

    [Authorize(Roles = "PRIMER_RESPONDIENTE")]
    [HttpPost("cargas/validar")]
    [RequestSizeLimit(12_000_000)]
    public async Task<IActionResult> Validar(IFormFile archivo, [FromForm] DateTime periodoInicio, [FromForm] DateTime periodoFin)
    {
        if(archivo.Length is <= 0 or > 10_000_000 || !Path.GetExtension(archivo.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { mensaje = "Seleccione un archivo .xlsx de hasta 10 MB." });
        }
        if(periodoInicio.Year < 2000 || periodoFin.Year > 2100 || periodoFin.Date < periodoInicio.Date)
        {
            return BadRequest(new { mensaje = "Indique un periodo válido entre 2000 y 2100." });
        }
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if(zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 100_000_000)
            {
                return BadRequest(new { mensaje = "El contenido descomprimido supera el límite técnico." });
            }
        }
        catch(InvalidDataException)
        {
            return BadRequest(new { mensaje = "El archivo no es un libro XLSX válido." });
        }
        (List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos) resultado;
        try
        {
            resultado = await excel.LeerAsync(bytes, Usuario);
        }
        catch(Exception ex) when(ex is InvalidDataException or FormatException or System.Xml.XmlException or ClosedXML.Excel.Exceptions.ClosedXMLException)
        {
            return BadRequest(new { mensaje = "El libro está dañado, protegido o contiene valores que no se pueden leer." });
        }
        if(resultado.Errores.Count > 0)
        {
            return Ok(new { valido = false, idCarga = (Guid?)null, totalRegistros = resultado.Registros.Count, errores = resultado.Errores, avisos = resultado.Avisos });
        }
        var nombre = Path.GetFileName(archivo.FileName.Replace('\\', '/'));
        if(nombre.Length > 255)
        {
            return BadRequest(new { mensaje = "El nombre del archivo es demasiado largo." });
        }
        var id = await cargas.GuardarAsync(Usuario, nombre, bytes, periodoInicio.Date, periodoFin.Date, resultado.Registros);
        return Ok(new { valido = true, idCarga = id, totalRegistros = resultado.Registros.Count, errores = resultado.Errores, avisos = resultado.Avisos });
    }

    [Authorize(Roles = "PRIMER_RESPONDIENTE")]
    [HttpPost("cargas/confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarCargaRequest request)
    {
        var carga = await cargas.ConfirmarAsync(Usuario, request.IdCarga);
        return carga == null ? NotFound() : Ok(carga);
    }

    [HttpGet("cargas")]
    public async Task<IActionResult> Listar() => Ok(await cargas.ListarAsync(Usuario));

    [HttpGet("cargas/{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id)
    {
        var carga = await cargas.ObtenerAsync(Usuario, id);
        return carga == null ? NotFound() : Ok(carga);
    }

    [HttpGet("cargas/{id:guid}/archivo")]
    public async Task<IActionResult> Archivo(Guid id)
    {
        var carga = await cargas.ObtenerAsync(Usuario, id);
        if(carga == null)
        {
            return NotFound();
        }
        return File((await cargas.ArchivoAsync(Usuario, id))!, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", carga.NombreArchivo);
    }

    [HttpGet("cargas/{id:guid}/acuse")]
    public async Task<IActionResult> Acuse(Guid id)
    {
        var carga = await cargas.ObtenerAsync(Usuario, id);
        if(carga == null)
        {
            return NotFound();
        }
        string E(string? texto) => WebUtility.HtmlEncode(texto ?? "");
        var logo = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "LogoSEGURIDAD.png")));
        var html = $$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Acuse {{E(carga.Folio)}}</title>
            <style>body{font:16px Arial;margin:60px;color:#222}h1{color:#691c32}dl{line-height:1.8}dt{font-weight:bold}dd{margin:0 0 12px}@media print{button{display:none} }</style></head>
            <body><img src="data:image/png;base64,{{logo}}" alt="Seguridad · Secretariado Ejecutivo" style="width:430px;max-width:100%"><h1>Eficiencia Ministerial</h1><h2>Acuse de recepción · Primer Respondiente</h2>
            <dl><dt>Folio</dt><dd>{{E(carga.Folio)}}</dd><dt>Institución</dt><dd>{{E(carga.Institucion)}}</dd>
            <dt>Archivo</dt><dd>{{E(carga.NombreArchivo)}}</dd><dt>Periodo declarado</dt><dd>{{carga.PeriodoInicio:yyyy-MM-dd}} al {{carga.PeriodoFin:yyyy-MM-dd}}</dd>
            <dt>Registros aceptados</dt><dd>{{carga.TotalRegistros}}</dd><dt>Recepción (UTC)</dt><dd>{{carga.ConfirmadoUtc:yyyy-MM-dd HH:mm:ss}}</dd></dl>
            <p>La recepción acredita la entrega del archivo. El cruce con RND está pendiente.</p>
            <button onclick="window.print()">Imprimir o guardar PDF</button></body></html>
            """;
        return File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", $"Acuse_{carga.Folio}.html");
    }
}
