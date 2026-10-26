using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class UsuarioService(UsuarioRepository repository, ConfiguracionRepository configuracion)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> HashInexistentes = new();

    public async Task<UsuarioInfo?> ObtenerAsync(int id)
    {
        var usuario = await repository.ObtenerAsync(id);
        var config = await configuracion.ObtenerAsync();
        if(usuario == null)
        {
            return null;
        }
        var rol = config.Roles.SingleOrDefault(r => r.Codigo == usuario.Rol);
        var ambito = config.Ambitos.SingleOrDefault(a => a.Codigo == usuario.Ambito);
        return rol != null && ambito != null
            && config.RolesAmbitos.Any(a => a.Rol == usuario.Rol && a.Ambito == usuario.Ambito)
            && (!ambito.RequiereEntidad || config.Entidades.Any(e => e.Id == usuario.Entidad && e.Asignable))
            && (usuario.Institucion == null || config.Instituciones.Any(i => i.Codigo == usuario.Institucion))
            && (rol.TipoInstitucion == null || config.Instituciones.Any(i => i.Codigo == usuario.Institucion && i.Tipo == rol.TipoInstitucion))
            ? usuario : null;
    }

    public Task<IEnumerable<UsuarioInfo>> ListarAsync() => repository.ListarAsync();

    public async Task<UsuarioInfo?> AutenticarAsync(LoginRequest request)
    {
        var usuario = await repository.ObtenerCredencialesAsync(request.Usuario.Trim());
        // Mantener el trabajo BCrypt cuando la cuenta no existe, sin una contraseña de respaldo.
        var costo = (await configuracion.ObtenerAsync()).Valor<int>("Seguridad.CostoHash");
        var hash = usuario?.PasswordHash ?? HashInexistentes.GetOrAdd(costo, valor => BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), valor));
        if(!BCrypt.Net.BCrypt.Verify(request.Password, hash) || usuario == null)
        {
            return null;
        }
        await repository.RegistrarLoginAsync(usuario.Id);
        return await ObtenerAsync(usuario.Id);
    }

    public async Task<int> CrearAsync(CrearUsuarioRequest request, int? actor)
    {
        var config = await configuracion.ObtenerAsync();
        var rol = config.Roles.SingleOrDefault(r => r.Codigo == request.Rol);
        var ambito = config.Ambitos.SingleOrDefault(a => a.Codigo == request.Ambito);
        if(rol == null || ambito == null || !config.RolesAmbitos.Any(a => a.Rol == request.Rol && a.Ambito == request.Ambito)
            || string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Nombre)
            || request.Password.Length < config.Valor<int>("Usuarios.PasswordMinimo")
            || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72
            || (ambito.RequiereEntidad && !config.Entidades.Any(e => e.Id == request.Entidad && e.Asignable))
            || (!ambito.RequiereEntidad && request.Entidad != null)
            || (request.Institucion != null && !config.Instituciones.Any(i => i.Codigo == request.Institucion))
            || (rol.TipoInstitucion != null && !config.Instituciones.Any(i => i.Codigo == request.Institucion && i.Tipo == rol.TipoInstitucion)))
        {
            throw new ArgumentException("Los datos no corresponden al perfil, alcance o política de contraseña configurados.");
        }
        return await repository.CrearAsync(request, BCrypt.Net.BCrypt.HashPassword(request.Password, config.Valor<int>("Seguridad.CostoHash")), actor);
    }
}
