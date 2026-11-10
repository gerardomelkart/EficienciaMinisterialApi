using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Services;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize(Roles = "SUPER_USUARIO,PRIMER_RESPONDIENTE,CONSULTA")]
[Route("api/informes/primer-respondiente")]
public sealed class InformesController(InformePrimerRespondienteRepository informes) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;

    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos() => Ok(await informes.CatalogosAsync(Usuario));

    [HttpGet]
    public async Task<IActionResult> Consultar([FromQuery] FiltrosInformePr filtros) => Ok(await informes.ConsultarAsync(Usuario, filtros));

    [HttpGet("excel")]
    public async Task<IActionResult> Excel([FromQuery] FiltrosInformePr filtros, [FromQuery] string tabla = "ambas")
    {
        var informe = await informes.ConsultarAsync(Usuario, filtros, true);
        if(informe.Totales.Registros == 0) return BadRequest(new { mensaje = "No hay registros para descargar con estos filtros." });
        return File(InformePrimerRespondienteExcel.Crear(informe, tabla),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            InformePrimerRespondienteExcel.NombreArchivo(informe));
    }
}
