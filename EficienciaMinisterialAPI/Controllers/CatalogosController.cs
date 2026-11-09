using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Repositories;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/catalogos")]
public sealed class CatalogosController(CatalogoRepository catalogos) : ControllerBase
{
    [HttpGet("usuarios")]
    public async Task<IActionResult> Usuarios() => Ok(await catalogos.ObtenerUsuariosAsync());
}
