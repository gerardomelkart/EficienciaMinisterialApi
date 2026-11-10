using System.IO.Compression;
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
public sealed class CargasController(ExcelCargaService excel, CargaRepository cargas, UsuarioRepository usuarios, LimitesCarga limites, CatalogoRepository catalogos, CruceRndService rnd) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;

    [HttpGet("plantilla")]
    public async Task<IActionResult> Plantilla()
    {
        var catalogo = await catalogos.ObtenerCapturaAsync();
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
        string[] reglas = ["Corporación obligatoria. Debe corresponder al acceso elegido.", "Opcional si hay nombre. Sin IA/AI. La nomenclatura inválida requiere búsqueda por nombre en RND.", "Obligatoria. Fecha de Excel, no texto. Puede ser anterior al periodo de entrega.", "Opcional, máximo 500 caracteres.", "Opcional si hay folio; máximo 300 caracteres.", "Obligatoria, dentro del alcance del usuario.", "Opcional. Consulte la hoja Catálogos para los valores vigentes."];
        for(var i = 0; i < reglas.Length; i++)
        {
            diccionario.Cell(i + 2, 1).Value = ExcelCargaService.Encabezados[i];
            diccionario.Cell(i + 2, 2).Value = reglas[i];
        }
        diccionario.Column(1).Width = 48;
        diccionario.Column(2).Width = 90;
        diccionario.Columns().Style.Alignment.WrapText = true;
        var referencia = libro.AddWorksheet("Catálogos");
        referencia.Cell(1, 1).Value = "Campo";
        referencia.Cell(1, 2).Value = "Valor reconocido";
        referencia.Cell(1, 3).Value = "Código";
        var filaCatalogo = 2;
        void AgregarCatalogo<T>(string campo, Dictionary<string, T> valores)
        {
            foreach(var valor in valores.OrderBy(v => v.Key))
            {
                referencia.Cell(filaCatalogo, 1).Value = campo;
                referencia.Cell(filaCatalogo, 2).Value = valor.Key;
                referencia.Cell(filaCatalogo, 3).Value = Convert.ToString(valor.Value) ?? "";
                filaCatalogo++;
            }
        }
        AgregarCatalogo("CORPORACION", catalogo.Corporaciones);
        AgregarCatalogo("ENTIDAD_DETENCION", catalogo.Entidades);
        AgregarCatalogo("TIPO_FUERO", catalogo.Fueros);
        referencia.Columns().Width = 38;
        referencia.SheetView.FreezeRows(1);
        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return File(memoria.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Plantilla_PrimerRespondiente.xlsx");
    }

    [Authorize(Roles = "PRIMER_RESPONDIENTE,SUPER_USUARIO")]
    [HttpPost("cargas/validacion/excel")]
    public IActionResult ExportarValidacion(ExportarValidacionRequest request)
    {
        if(request.Filas == null || request.Filas.Count == 0 || request.Filas.Count >= XLHelper.MaxRowNumber || request.Filas.Any(f => f == null))
        {
            return BadRequest(new { mensaje = "No hay resultados para exportar o exceden la capacidad de una hoja de Excel." });
        }
        return File(ExcelCargaService.ExportarResultados(request.Filas), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Resultado_validacion.xlsx");
    }

    [Authorize(Roles = "PRIMER_RESPONDIENTE,SUPER_USUARIO")]
    [HttpPost("cargas/validar")]
    public async Task<IActionResult> Validar(IFormFile archivo, [FromForm] int idPeriodo, [FromForm] string? institucion = null)
    {
        if(Usuario.Rol == "SUPER_USUARIO")
        {
            if(!await usuarios.InstitucionValidaAsync(institucion, "CORPORACION"))
            {
                return BadRequest(new { mensaje = "Seleccione una corporación activa para la carga." });
            }
            Usuario.Institucion = institucion;
        }
        if(archivo.Length <= 0 || archivo.Length > limites.MaximoBytes || !Path.GetExtension(archivo.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { mensaje = $"Seleccione un archivo .xlsx de hasta {limites.MaximoBytes:N0} bytes." });
        }
        if(idPeriodo <= 0)
        {
            return BadRequest(new { mensaje = "Seleccione un periodo quincenal habilitado." });
        }
        var periodosDisponibles = await catalogos.ObtenerPeriodosAsync(Usuario);
        if(!periodosDisponibles.Any(p => p.Id == idPeriodo))
        {
            return BadRequest(new { mensaje = "La quincena no está disponible o la corporación ya tiene una entrega confirmada. Actualice los periodos." });
        }
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if(zip.Entries.Count > limites.MaximoEntradasZip || zip.Entries.Sum(e => e.Length) > limites.MaximoDescomprimidoBytes)
            {
                return BadRequest(new { mensaje = "El contenido descomprimido supera el límite técnico." });
            }
        }
        catch(InvalidDataException)
        {
            return BadRequest(new { mensaje = "El archivo no es un libro XLSX válido." });
        }
        (List<RegistroCarga> Registros, List<ErrorFila> Errores, List<ErrorFila> Avisos) resultado;
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
        var cruce = await rnd.CruzarAsync(resultado.Registros);
        foreach(var administrativa in cruce.Administrativas)
        {
            resultado.Errores.Add(new(administrativa.Key, "RND_FA",
                $"Se localizó en la carga diaria de faltas administrativas {cruce.TablaFa}. No es posible reportar esta detención en eficiencia ministerial.",
                resultado.Registros.First(r => r.Fila == administrativa.Key).FolioRnd));
        }
        if(resultado.Errores.Count > 0)
        {
            return Ok(new { valido = false, idCarga = (Guid?)null, totalRegistros = resultado.Registros.Count,
                errores = resultado.Errores, avisos = resultado.Avisos });
        }
        foreach(var registro in resultado.Registros)
        {
            var coincidencias = cruce.Filas.GetValueOrDefault(registro.Fila) ?? [];
            if(coincidencias.Count == 0)
            {
                resultado.Avisos.Add(new(registro.Fila, "RND", "No localizado por folio ni nombre del primer respondiente y fecha. Requiere consentimiento; no se enviará a fiscalías.", registro.FolioRnd));
            }
            else if(coincidencias.Select(c => (c.IdDetenido, c.IdDetencion)).Distinct().Count() > 1)
            {
                resultado.Avisos.Add(new(registro.Fila, "RND", "El RND tiene varias personas o detenciones para este folio; se conservan las coincidencias para seguimiento.", registro.FolioRnd));
            }
        }
        var id = await cargas.GuardarAsync(Usuario, nombre, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), idPeriodo, resultado.Registros, cruce);
        return Ok(new { valido = true, idCarga = id, totalRegistros = resultado.Registros.Count, errores = resultado.Errores, avisos = resultado.Avisos });
    }

    [Authorize(Roles = "PRIMER_RESPONDIENTE,SUPER_USUARIO")]
    [HttpPost("cargas/confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarCargaRequest request)
    {
        var carga = await cargas.ConfirmarAsync(Usuario, request.IdCarga, request.AceptaNoLocalizados);
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
        return File(ExcelCargaService.GenerarArchivo(await cargas.RegistrosAsync(Usuario, id)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", AcuseCargaService.NombreArchivo(carga, "Detenciones", "xlsx"));
    }

    [HttpGet("cargas/{id:guid}/acuse")]
    public async Task<IActionResult> Acuse(Guid id)
    {
        var carga = await cargas.ObtenerParaAcuseAsync(Usuario, id);
        if(carga == null)
        {
            return NotFound();
        }
        Response.Headers.CacheControl = "no-store";
        return File(AcuseCargaService.Crear(carga), "application/pdf", AcuseCargaService.NombreArchivo(carga, "Acuse", "pdf"));
    }
}
