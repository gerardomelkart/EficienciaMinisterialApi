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
    public async Task<IActionResult> Validar([FromQuery] DateTime inicio, [FromQuery] DateTime fin, IFormFile archivo)
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(inicio == default || fin < inicio || archivo.Length == 0 ||
            !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Seleccione un periodo y un archivo .xlsx." });
        var (filas, opciones) = await plantillas.ObtenerDatosAsync(Usuario, inicio, fin);
        if(filas.Count == 0) return BadRequest(new { mensaje = "No hay una precarga vigente para este periodo." });
        using var entrada = archivo.OpenReadStream();
        ClosedXML.Excel.XLWorkbook libro;
        try { libro = new ClosedXML.Excel.XLWorkbook(entrada); }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            return BadRequest(new { mensaje = "No se pudo leer el Excel. Use la plantilla descargada." });
        }
        using(libro) return Ok(ValidacionFiscalia.Revisar(libro, filas, opciones));
    }
}
