using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Repositories;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Authorize]
[Route("api/configuracion")]
public sealed class ConfiguracionController(ConfiguracionRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        var configuracion = await repository.ObtenerAsync();
        var usuario = (UsuarioInfo)HttpContext.Items["Usuario"]!;
        return Ok(new
        {
            configuracion.Roles, configuracion.Ambitos, configuracion.RolesAmbitos,
            configuracion.Instituciones, configuracion.Entidades, configuracion.Fueros,
            permisos = configuracion.Permisos.Where(p => p.Rol == usuario.Rol).Select(p => p.Permiso),
            parametros = configuracion.Parametros.Where(p => !p.Key.StartsWith("Seguridad."))
                .ToDictionary(p => p.Key, p => JsonSerializer.Deserialize<JsonElement>(p.Value))
        });
    }
}
