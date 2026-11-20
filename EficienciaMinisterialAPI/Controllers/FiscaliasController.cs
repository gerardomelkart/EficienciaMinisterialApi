using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;

namespace EficienciaMinisterialAPI.Controllers;

[ApiController]
[Authorize(Roles = "SUPER_USUARIO,FISCALIA")]
[Route("api/fiscalias")]
public sealed class FiscaliasController(PlantillaFiscaliaService plantillas) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;

    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos()
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        return Ok(await plantillas.CatalogosAsync(Usuario));
    }

    [HttpGet("plantilla")]
    public async Task<IActionResult> Plantilla([FromQuery] DateTime inicio, [FromQuery] DateTime fin)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(inicio == default || fin < inicio || fin == default)
            return BadRequest(new { mensaje = "Seleccione un periodo válido." });
        if(await plantillas.EntregaPeriodoAsync(Usuario, inicio, fin) is { } anterior)
            return YaConfirmada(anterior);
        var resultado = await plantillas.DescargarAsync(Usuario, inicio, fin);
        if(resultado.Archivo == null) return BadRequest(new { mensaje = "No hay registros localizados en RND para este periodo." });
        return File(resultado.Archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Plantilla_Fiscalia_{inicio:dd-MM-yyyy}_al_{fin:dd-MM-yyyy}.xlsx");
    }

    [HttpPost("errores/excel")]
    public IActionResult DescargarErrores(List<IncidenciaFiscalia> errores)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(errores.Count is 0 or > 1048575) return BadRequest();
        using var libro = new ClosedXML.Excel.XLWorkbook();
        var hoja = libro.AddWorksheet("Errores");
        hoja.Cell(1, 1).InsertData(new[] { new[] { "Fila del Excel", "Campo", "Detalle" } });
        for(int i = 0; i < errores.Count; i++)
        {
            var error = errores[i];
            if(error.Fila is int fila) hoja.Cell(i + 2, 1).Value = fila;
            else hoja.Cell(i + 2, 1).Value = "General";
            hoja.Cell(i + 2, 2).Value = error.Campo;
            hoja.Cell(i + 2, 3).Value = error.Detalle;
        }
        hoja.Row(1).Style.Font.Bold = true;
        hoja.Columns(1, 3).AdjustToContents();
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return File(salida.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [HttpPost("validar")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public Task<IActionResult> Validar([FromQuery] DateTime inicio, [FromQuery] DateTime fin, IFormFile archivo)
        => Procesar(inicio, fin, archivo, false);

    [HttpPost("confirmar")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public Task<IActionResult> Confirmar([FromQuery] DateTime inicio, [FromQuery] DateTime fin, IFormFile archivo)
        => Procesar(inicio, fin, archivo, true);

    [HttpGet("entregas/{id:long}/acuse")]
    public async Task<IActionResult> Acuse(long id)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        var entrega = await plantillas.AcuseAsync(Usuario, id);
        if(entrega == null) return NotFound();
        return File(AcuseCargaService.CrearFiscalia(entrega), "application/pdf", $"Acuse_Fiscalia_{entrega.Inicio:dd-MM-yyyy}_al_{entrega.Fin:dd-MM-yyyy}.pdf");
    }

    private IActionResult YaConfirmada(PlantillaFiscaliaService.Entrega entrega) =>
        Conflict(new { mensaje = $"La quincena ya fue confirmada con el folio {entrega.Folio}. Puede consultar su acuse.", entrega });

    private async Task<IActionResult> Procesar(DateTime inicio, DateTime fin, IFormFile archivo, bool confirmar)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(inicio == default || fin < inicio || archivo.Length == 0 ||
            !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Seleccione un periodo y un archivo .xlsx." });
        if(await plantillas.EntregaPeriodoAsync(Usuario, inicio, fin) is { } anterior)
            return YaConfirmada(anterior);
        using var entrada = archivo.OpenReadStream();
        ClosedXML.Excel.XLWorkbook libro;
        try { libro = new ClosedXML.Excel.XLWorkbook(entrada); }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            return BadRequest(new { mensaje = "No se pudo leer el Excel. Use la plantilla descargada." });
        }
        using(libro)
        {
            if(confirmar)
            {
                var (entrega, revision) = await plantillas.ConfirmarAsync(Usuario, inicio, fin, libro);
                if(entrega != null) return Ok(entrega);
                if(revision == null && await plantillas.EntregaPeriodoAsync(Usuario, inicio, fin) is { } previa) return YaConfirmada(previa);
                return Conflict(new { mensaje = revision == null ? "Ya existe una entrega confirmada para este periodo y alcance." :
                    revision.Errores.FirstOrDefault()?.Detalle ?? "La precarga cambió. Revise el archivo antes de confirmar.", revision });
            }
            var (filas, opciones) = await plantillas.ObtenerDatosAsync(Usuario, inicio, fin);
            if(filas.Count == 0) return BadRequest(new { mensaje = "No hay una precarga vigente para este periodo." });
            var resultado = ValidacionFiscalia.Revisar(libro, filas, opciones);
            byte[]? previo = null;
            if(resultado.Errores.Count == 0)
            {
                var entidad = Usuario.Ambito == "ESTATAL" && Usuario.Rol != "SUPER_USUARIO" ? Usuario.Entidad : null;
                previo = AcuseCargaService.CrearFiscalia(new(0, inicio, fin, DateTime.UtcNow, resultado.Recibidos, resultado.NoRecibidos, entidad), previo: true);
            }
            return Ok(new { resultado.Omitidos, resultado.Esperados, resultado.Recibidos, resultado.NoRecibidos, resultado.Errores, previo });
        }
    }
}
