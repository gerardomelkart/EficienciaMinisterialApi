using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize]
[Route("api/catalogos")]
public sealed class CatalogosController(CatalogoRepository catalogos, LimitesCarga limites) : ControllerBase
{
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("usuarios")]
    public async Task<IActionResult> Usuarios() => Ok(await catalogos.ObtenerUsuariosAsync());

    [Authorize(Roles = "PRIMER_RESPONDIENTE,SUPER_USUARIO")]
    [HttpGet("carga")]
    public async Task<IActionResult> Carga() => Ok(new { periodos = await catalogos.ObtenerPeriodosAsync(), limites });
}
