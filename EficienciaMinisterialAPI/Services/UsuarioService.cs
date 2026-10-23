using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class UsuarioService(UsuarioRepository repository)
{
    public Task<UsuarioInfo?> ObtenerAsync(int id) => repository.ObtenerAsync(id);

    public Task<IEnumerable<UsuarioInfo>> ListarAsync() => repository.ListarAsync();

    public async Task<UsuarioInfo?> AutenticarAsync(LoginRequest request)
    {
        var usuario = await repository.ObtenerCredencialesAsync(request.Usuario.Trim());
        // Hash fijo de una contraseña aleatoria; también se verifica cuando la cuenta no existe.
        var hash = usuario?.PasswordHash ?? "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
        if(!BCrypt.Net.BCrypt.Verify(request.Password, hash) || usuario == null)
        {
            return null;
        }
        await repository.RegistrarLoginAsync(usuario.Id);
        return await ObtenerAsync(usuario.Id);
    }

    public async Task<int> CrearAsync(CrearUsuarioRequest request, int? actor)
    {
        string[] roles = ["SUPER_USUARIO", "PRIMER_RESPONDIENTE", "FISCALIA", "CONSULTA"];
        string[] ambitos = ["NACIONAL", "ESTATAL", "FEDERAL"];
        if(!roles.Contains(request.Rol) || !ambitos.Contains(request.Ambito)
            || string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Nombre)
            || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72
            || (request.Ambito == "ESTATAL" && request.Entidad == null)
            || (request.Ambito != "ESTATAL" && request.Entidad != null)
            || (request.Rol == "SUPER_USUARIO" && request.Ambito != "NACIONAL"))
        {
            throw new ArgumentException("Perfil o alcance inválido. La contraseña admite hasta 72 bytes UTF-8.");
        }
        if(request.Rol is "PRIMER_RESPONDIENTE" or "FISCALIA")
        {
            var tipo = request.Rol == "PRIMER_RESPONDIENTE" ? "CORPORACION" : "FISCALIA";
            if(!await repository.InstitucionValidaAsync(request.Institucion, tipo))
            {
                throw new ArgumentException("La institución no corresponde al perfil.");
            }
        }
        return await repository.CrearAsync(request, BCrypt.Net.BCrypt.HashPassword(request.Password, 12), actor);
    }
}
