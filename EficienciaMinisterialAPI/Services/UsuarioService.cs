using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class UsuarioService(UsuarioRepository repository, CatalogoRepository catalogos)
{
    private static readonly string HashInexistente = BCrypt.Net.BCrypt.HashPassword(
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), 12);

    public Task<UsuarioInfo?> ObtenerAsync(int id) => repository.ObtenerAsync(id);
    public Task<IEnumerable<UsuarioInfo>> ListarAsync() => repository.ListarAsync();

    public async Task<UsuarioInfo?> AutenticarAsync(LoginRequest request)
    {
        var usuario = await repository.ObtenerCredencialesAsync(request.Usuario.Trim());
        var hash = usuario?.PasswordHash ?? HashInexistente;
        if(!BCrypt.Net.BCrypt.Verify(request.Password, hash) || usuario == null)
        {
            return null;
        }
        await repository.RegistrarLoginAsync(usuario.Id);
        return await ObtenerAsync(usuario.Id);
    }

    public async Task<int> CrearAsync(CrearUsuarioRequest request, int? actor)
    {
        if(string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Nombre)
            || request.Password.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            throw new ArgumentException("Indique nombre, usuario y contraseña de al menos 12 caracteres y hasta 72 bytes UTF-8.");
        }
        var accesos = request.Accesos ?? [new AccesoUsuario
        {
            Rol = request.Rol, Institucion = request.Institucion, Ambito = request.Ambito, Entidad = request.Entidad
        }];
        await ValidarAccesosAsync(accesos);
        return await repository.CrearAsync(request, accesos, BCrypt.Net.BCrypt.HashPassword(request.Password, 12), actor);
    }

    public async Task<bool> ActualizarAsync(int id, ActualizarUsuarioRequest request, int actor)
    {
        if(string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Nombre))
        {
            throw new ArgumentException("Indique nombre y usuario.");
        }
        await ValidarAccesosAsync(request.Accesos);
        return await repository.ActualizarAsync(id, request, actor);
    }

    public static void ValidarEstructuraAccesos(List<AccesoUsuario> accesos, List<PerfilInfo> perfiles, List<AmbitoInfo> ambitos)
    {
        if(accesos.Count is < 1 or > 100 || accesos.Any(a => a == null))
        {
            throw new ArgumentException("Asigne entre 1 y 100 accesos válidos.");
        }
        foreach(var acceso in accesos)
        {
            var perfil = perfiles.Find(p => p.Codigo == acceso.Rol);
            var ambito = ambitos.Find(a => a.Codigo == acceso.Ambito);
            if(perfil == null || ambito == null
                || (ambito.RequiereEntidad && (acceso.Entidad == null || !perfil.PermiteEstatal))
                || (!ambito.RequiereEntidad && acceso.Entidad != null)
                || (acceso.Rol == "SUPER_USUARIO" && (acceso.Ambito != "NACIONAL" || acceso.Institucion != null))
                || (perfil.RequiereInstitucion && string.IsNullOrWhiteSpace(acceso.Institucion)))
            {
                throw new ArgumentException("Perfil o alcance inválido. Asigne una institución y una entidad cuando correspondan.");
            }
        }
        if(accesos.Select(a => (a.Rol, a.Institucion, a.Ambito, a.Entidad)).Distinct().Count() != accesos.Count)
        {
            throw new ArgumentException("Hay asignaciones de acceso repetidas.");
        }
    }

    private async Task ValidarAccesosAsync(List<AccesoUsuario> accesos)
    {
        var opciones = await catalogos.ObtenerPerfilesAsync();
        ValidarEstructuraAccesos(accesos, opciones.Perfiles, opciones.Ambitos);
        foreach(var acceso in accesos)
        {
            acceso.Institucion = acceso.Institucion?.Trim().ToUpperInvariant();
            if(acceso.Ambito == "ESTATAL" && !await repository.EntidadValidaAsync(acceso.Entidad))
            {
                throw new ArgumentException("La entidad no está activa en el catálogo.");
            }
            var tipo = opciones.Perfiles.Single(p => p.Codigo == acceso.Rol).TipoInstitucion;
            if(acceso.Institucion != null && !await repository.InstitucionValidaAsync(acceso.Institucion, tipo))
            {
                throw new ArgumentException("La institución no corresponde al perfil o no está activa.");
            }
        }
    }
}
