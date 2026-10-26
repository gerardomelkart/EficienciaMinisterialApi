using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize]
[Permiso("usuarios")]
[Route("api/usuarios")]
public sealed class UsuariosController(UsuarioService usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        return Ok(await usuarios.ListarAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearUsuarioRequest request)
    {
        var id = await usuarios.CrearAsync(request, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return Ok(new { id });
    }
}
