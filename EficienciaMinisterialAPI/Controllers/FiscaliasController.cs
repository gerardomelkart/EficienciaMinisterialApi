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
        var resultado = await plantillas.DescargarAsync(Usuario, inicio, fin);
        if(resultado.Archivo == null) return BadRequest(new { mensaje = "No hay registros localizados en RND para este periodo." });
        return File(resultado.Archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Plantilla_Fiscalia_{inicio:dd-MM-yyyy}_al_{fin:dd-MM-yyyy}.xlsx");
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

    private async Task<IActionResult> Procesar(DateTime inicio, DateTime fin, IFormFile archivo, bool confirmar)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(inicio == default || fin < inicio || archivo.Length == 0 ||
            !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Seleccione un periodo y un archivo .xlsx." });
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
                return Conflict(new { mensaje = revision == null ? "Ya existe una entrega confirmada para este periodo y alcance." :
                    "La precarga cambió o hay respuestas por corregir. Revise el archivo antes de confirmar.", revision });
            }
            var (filas, opciones) = await plantillas.ObtenerDatosAsync(Usuario, inicio, fin);
            if(filas.Count == 0) return BadRequest(new { mensaje = "No hay una precarga vigente para este periodo." });
            return Ok(ValidacionFiscalia.Revisar(libro, filas, opciones));
        }
    }
}
