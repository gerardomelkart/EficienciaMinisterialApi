using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/usuarios")]
public sealed class UsuariosController(UsuarioService usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        return Ok(await usuarios.ListarAsync());
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, ActualizarUsuarioRequest request)
    {
        var actualizado = await usuarios.ActualizarAsync(id, request, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return actualizado ? Ok(new { id }) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearUsuarioRequest request)
    {
        var id = await usuarios.CrearAsync(request, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return Ok(new { id });
    }
}
