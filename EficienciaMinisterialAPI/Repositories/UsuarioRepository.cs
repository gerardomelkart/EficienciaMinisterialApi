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
        using var datos = await db.QueryMultipleAsync("""
            SELECT id, usuario, nombre, activo FROM seguridad.usuario WHERE id = @id AND activo = 1;
            SELECT id, rol, institucion, ambito, entidad FROM seguridad.usuario_acceso WHERE id_usuario = @id ORDER BY id;
            """, new { id });
        var usuario = await datos.ReadSingleOrDefaultAsync<UsuarioInfo>();
        var accesos = (await datos.ReadAsync<AccesoUsuario>()).ToList();
        if(usuario != null)
        {
            usuario.Accesos = accesos;
            if(!usuario.SeleccionarAcceso(null))
            {
                return null;
            }
        }
        return usuario;
    }

    public async Task<IEnumerable<UsuarioInfo>> ListarAsync()
    {
        await using var db = await factory.AbrirAsync();
        using var datos = await db.QueryMultipleAsync("""
            SELECT id, usuario, nombre, activo FROM seguridad.usuario ORDER BY nombre;
            SELECT id, id_usuario AS IdUsuario, rol, institucion, ambito, entidad FROM seguridad.usuario_acceso ORDER BY id;
            """);
        var usuarios = (await datos.ReadAsync<UsuarioInfo>()).ToList();
        var accesos = (await datos.ReadAsync<AccesoConUsuario>()).ToLookup(a => a.IdUsuario);
        foreach(var usuario in usuarios)
        {
            usuario.Accesos = accesos[usuario.Id].Select(a => new AccesoUsuario
            {
                Id = a.Id, Rol = a.Rol, Institucion = a.Institucion, Ambito = a.Ambito, Entidad = a.Entidad
            }).ToList();
            usuario.SeleccionarAcceso(null);
        }
        return usuarios;
    }

    private sealed class AccesoConUsuario
    {
        public int Id { get; set; }
        public int IdUsuario { get; set; }
        public string Rol { get; set; } = "";
        public string? Institucion { get; set; }
        public string Ambito { get; set; } = "";
        public byte? Entidad { get; set; }
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

    public async Task<bool> InstitucionValidaAsync(string? institucion, string? tipo)
    {
        await using var db = await factory.AbrirAsync();
        return await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM catalogo.institucion WHERE codigo = @institucion AND (@tipo IS NULL OR tipo = @tipo) AND activo = 1) THEN 1 ELSE 0 END AS bit)", new { institucion, tipo });
    }

    public async Task<bool> EntidadValidaAsync(byte? entidad)
    {
        await using var db = await factory.AbrirAsync();
        return await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM catalogo.entidad WHERE id = @entidad AND permite_captura = 1 AND activo = 1) THEN 1 ELSE 0 END AS bit)", new { entidad });
    }

    public async Task<int> CrearAsync(CrearUsuarioRequest request, List<AccesoUsuario> accesos, string hash, int? actor)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        await BloquearAdministracionAsync(db, tx);
        var principal = accesos[0];
        var id = await db.ExecuteScalarAsync<int>("""
            INSERT seguridad.usuario(usuario, nombre, password_hash, rol, institucion, ambito, entidad)
            OUTPUT INSERTED.id VALUES(@Usuario, @Nombre, @Hash, @Rol, @Institucion, @Ambito, @Entidad)
            """, new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), Hash = hash, principal.Rol, principal.Institucion, principal.Ambito, principal.Entidad }, tx);
        await GuardarAccesosAsync(db, tx, id, accesos);
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion) VALUES(@Actor, 'CREAR_USUARIO')", new { Actor = actor ?? id }, tx);
        await tx.CommitAsync();
        return id;
    }

    public async Task<bool> ActualizarAsync(int id, ActualizarUsuarioRequest request, int actor)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        await BloquearAdministracionAsync(db, tx);
        if(!await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM seguridad.usuario WHERE id = @id) THEN 1 ELSE 0 END AS bit)", new { id }, tx))
        {
            return false;
        }
        var eliminaSuper = !request.Activo || !request.Accesos.Any(a => a.Rol == "SUPER_USUARIO");
        if(eliminaSuper)
        {
            var ultimo = await db.ExecuteScalarAsync<bool>("""
                SELECT CAST(CASE WHEN EXISTS(
                    SELECT 1 FROM seguridad.usuario u JOIN seguridad.usuario_acceso a ON a.id_usuario = u.id
                    WHERE u.id = @id AND u.activo = 1 AND a.rol = 'SUPER_USUARIO'
                ) AND NOT EXISTS(
                    SELECT 1 FROM seguridad.usuario u JOIN seguridad.usuario_acceso a ON a.id_usuario = u.id
                    WHERE u.id <> @id AND u.activo = 1 AND a.rol = 'SUPER_USUARIO'
                ) THEN 1 ELSE 0 END AS bit)
                """, new { id }, tx);
            if(ultimo)
            {
                throw new ArgumentException("Debe conservar al menos un superusuario activo.");
            }
        }
        var principal = request.Accesos[0];
        await db.ExecuteAsync("""
            UPDATE seguridad.usuario SET usuario = @Usuario, nombre = @Nombre, activo = @Activo,
                rol = @Rol, institucion = @Institucion, ambito = @Ambito, entidad = @Entidad WHERE id = @id;
            DELETE seguridad.usuario_acceso WHERE id_usuario = @id;
            """, new { id, Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), request.Activo, principal.Rol, principal.Institucion, principal.Ambito, principal.Entidad }, tx);
        await GuardarAccesosAsync(db, tx, id, request.Accesos);
        // Solo identificador interno del objetivo; no se registra contraseña ni nombre.
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion, id_usuario_objetivo) VALUES(@actor, 'ACTUALIZAR_USUARIO', @id)", new { actor, id }, tx);
        await tx.CommitAsync();
        return true;
    }

    private static async Task BloquearAdministracionAsync(SqlConnection db, SqlTransaction tx)
    {
        var resultado = await db.ExecuteScalarAsync<int>("""
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock @Resource = 'EM_ADMIN_USUARIOS', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @resultado;
            """, transaction: tx);
        if(resultado < 0)
        {
            throw new InvalidOperationException("No se pudo obtener el bloqueo de administración de usuarios.");
        }
    }

    private static async Task GuardarAccesosAsync(SqlConnection db, SqlTransaction tx, int id, List<AccesoUsuario> accesos)
    {
        foreach(var acceso in accesos)
        {
            await db.ExecuteAsync("""
                INSERT seguridad.usuario_acceso(id_usuario, rol, institucion, ambito, entidad)
                VALUES(@id, @Rol, @Institucion, @Ambito, @Entidad)
                """, new { id, acceso.Rol, acceso.Institucion, acceso.Ambito, acceso.Entidad }, tx);
        }
    }
}
