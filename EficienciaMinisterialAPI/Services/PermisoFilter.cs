using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Repositories;

namespace EficienciaMinisterialAPI.Services;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PermisoAttribute(string permiso) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if(context.HttpContext.Items["Usuario"] is not UsuarioInfo usuario)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        var config = await context.HttpContext.RequestServices.GetRequiredService<ConfiguracionRepository>().ObtenerAsync();
        if(!config.Roles.Any(r => r.Codigo == usuario.Rol) || !config.Permite(usuario.Rol, permiso))
        {
            context.Result = new ForbidResult();
        }
    }
}
