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
[Authorize]
[Route("api/primer-respondiente")]
public sealed class CargasController(ExcelCargaService excel, CargaRepository cargas, ConfiguracionRepository configuracion) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;

    [Permiso("consultar_cargas")]
    [HttpGet("plantilla")]
    public async Task<IActionResult> Plantilla()
    {
        var config = await configuracion.ObtenerAsync();
        var campos = config.Campos;
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("PLANTILLA");
        for(var i = 0; i < campos.Count; i++)
        {
            hoja.Cell(1, i + 1).Value = campos[i].Encabezado;
        }
        hoja.Range(1, 1, 1, campos.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#691c32");
        hoja.Range(1, 1, 1, campos.Count).Style.Font.FontColor = XLColor.White;
        hoja.Range(1, 1, 1, campos.Count).Style.Font.Bold = true;
        hoja.Range(1, 1, 1, campos.Count).Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 45;
        hoja.Columns().Width = 28;
        hoja.Column(4).Width = 45;
        hoja.Column(5).Width = 40;
        hoja.Column(campos.Single(c => c.Codigo == "FECHA_DETENCION").Posicion).Style.DateFormat.Format = "yyyy-mm-dd";
        hoja.SheetView.FreezeRows(1);
        var diccionario = libro.AddWorksheet("Diccionario");
        diccionario.Cell(1, 1).Value = "Campo";
        diccionario.Cell(1, 2).Value = "Regla de esta entrega";
        for(var i = 0; i < campos.Count; i++)
        {
            diccionario.Cell(i + 2, 1).Value = campos[i].Encabezado;
            diccionario.Cell(i + 2, 2).Value = campos[i].Regla;
        }
        diccionario.Column(1).Width = 48;
        diccionario.Column(2).Width = 90;
        diccionario.Columns().Style.Alignment.WrapText = true;
        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return File(memoria.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Plantilla_PrimerRespondiente.xlsx");
    }

    [Permiso("cargar")]
    [HttpPost("cargas/validar")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Validar(IFormFile archivo, [FromForm] DateTime periodoInicio, [FromForm] DateTime periodoFin)
    {
        var config = await configuracion.ObtenerAsync();
        if(archivo.Length <= 0 || archivo.Length > config.Valor<long>("Carga.MaxBytes") || !Path.GetExtension(archivo.FileName).Equals(config.Valor<string>("Carga.Extension"), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { mensaje = $"Seleccione un archivo {config.Valor<string>("Carga.Extension")} de hasta {config.Valor<long>("Carga.MaxBytes")} bytes." });
        }
        if(periodoInicio.Year < config.Valor<int>("Carga.AnioMinimo") || periodoFin.Year > config.Valor<int>("Carga.AnioMaximo") || periodoFin.Date < periodoInicio.Date)
        {
            return BadRequest(new { mensaje = "Indique un periodo válido dentro de los años configurados." });
        }
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if(zip.Entries.Count > config.Valor<int>("Carga.MaxEntradasZip") || zip.Entries.Sum(e => e.Length) > config.Valor<long>("Carga.MaxDescomprimido"))
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

    [Permiso("cargar")]
    [HttpPost("cargas/confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarCargaRequest request)
    {
        var carga = await cargas.ConfirmarAsync(Usuario, request.IdCarga);
        return carga == null ? NotFound() : Ok(carga);
    }

    [Permiso("consultar_cargas")]
    [HttpGet("cargas")]
    public async Task<IActionResult> Listar() => Ok(await cargas.ListarAsync(Usuario));

    [Permiso("consultar_cargas")]
    [HttpGet("cargas/{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id)
    {
        var carga = await cargas.ObtenerAsync(Usuario, id);
        return carga == null ? NotFound() : Ok(carga);
    }

    [Permiso("consultar_cargas")]
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

    [Permiso("consultar_cargas")]
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
