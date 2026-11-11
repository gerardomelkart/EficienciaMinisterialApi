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
        return Ok(await plantillas.CatalogosAsync());
    }

    [HttpGet("plantilla")]
    public async Task<IActionResult> Plantilla([FromQuery] DateTime inicio, [FromQuery] DateTime fin, [FromQuery] string fiscalia = "FGR")
    {
        if(!PlantillaFiscaliaService.TieneAcceso(Usuario)) return Forbid();
        if(fiscalia != "FGR" || inicio == default || fin < inicio || fin == default)
            return BadRequest(new { mensaje = "Seleccione la fiscalía y un periodo válido." });
        var resultado = await plantillas.DescargarAsync(Usuario, inicio, fin);
        if(resultado.Archivo == null) return BadRequest(new { mensaje = "No hay registros de fuero federal localizados en RND para este periodo." });
        return File(resultado.Archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Plantilla_Fiscalia_{inicio:dd-MM-yyyy}_al_{fin:dd-MM-yyyy}.xlsx");
    }
}
