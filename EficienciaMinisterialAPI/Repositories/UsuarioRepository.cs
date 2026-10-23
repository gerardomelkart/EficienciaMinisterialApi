using Dapper;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class UsuarioRepository(SqlConnectionFactory factory)
{
    public async Task<UsuarioInfo?> ObtenerAsync(int id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<UsuarioInfo>("""
            SELECT id, usuario, nombre, rol, institucion, ambito, entidad
            FROM seguridad.usuario WHERE id = @id AND activo = 1
            """, new { id });
    }

    public async Task<IEnumerable<UsuarioInfo>> ListarAsync()
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<UsuarioInfo>("SELECT id, usuario, nombre, rol, institucion, ambito, entidad FROM seguridad.usuario WHERE activo = 1 ORDER BY nombre");
    }

    public async Task<UsuarioAutenticacion?> ObtenerCredencialesAsync(string usuario)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<UsuarioAutenticacion>("""
            SELECT id, password_hash AS PasswordHash FROM seguridad.usuario
            WHERE usuario = @usuario AND activo = 1
            """, new { usuario });
    }

    public async Task RegistrarLoginAsync(int id)
    {
        await using var db = await factory.AbrirAsync();
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion) VALUES(@id, 'LOGIN')", new { id });
    }

    public async Task<bool> InstitucionValidaAsync(string? institucion, string tipo)
    {
        await using var db = await factory.AbrirAsync();
        return await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM catalogo.institucion WHERE codigo = @institucion AND tipo = @tipo AND activo = 1) THEN 1 ELSE 0 END AS bit)", new { institucion, tipo });
    }

    public async Task<int> CrearAsync(CrearUsuarioRequest request, string hash, int? actor)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var id = await db.ExecuteScalarAsync<int>("""
            INSERT seguridad.usuario(usuario, nombre, password_hash, rol, institucion, ambito, entidad)
            OUTPUT INSERTED.id VALUES(@Usuario, @Nombre, @Hash, @Rol, @Institucion, @Ambito, @Entidad)
            """, new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), Hash = hash, request.Rol, request.Institucion, request.Ambito, request.Entidad }, tx);
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion) VALUES(@Actor, 'CREAR_USUARIO')", new { Actor = actor ?? id }, tx);
        await tx.CommitAsync();
        return id;
    }
}
