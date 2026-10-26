using Dapper;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CargaRepository(SqlConnectionFactory factory)
{
    private const string Columnas = "id, folio, institucion, nombre_archivo AS NombreArchivo, periodo_inicio AS PeriodoInicio, periodo_fin AS PeriodoFin, estado, total_registros AS TotalRegistros, creado_utc AS CreadoUtc, confirmado_utc AS ConfirmadoUtc";

    public async Task<Guid> GuardarAsync(UsuarioInfo usuario, string nombre, byte[] archivo, DateTime inicio, DateTime fin, List<RegistroCarga> registros)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var id = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT primer_respondiente.carga(id, id_usuario, institucion, nombre_archivo, archivo, sha256, periodo_inicio, periodo_fin, estado, total_registros)
            VALUES(@id, @UsuarioId, @Institucion, @nombre, @archivo, @hash, @inicio, @fin, 'VALIDADA', @total)
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, nombre, archivo, hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archivo)), inicio, fin, total = registros.Count }, tx);
        foreach(var registro in registros)
        {
            await db.ExecuteAsync("""
                INSERT primer_respondiente.registro(id_carga, fila, corporacion, folio_rnd, fecha_detencion, autoridad_puesta, nombre_detenido, entidad, tipo_fuero)
                VALUES(@id, @Fila, @Corporacion, @FolioRnd, @FechaDetencion, @AutoridadPuesta, @NombreDetenido, @Entidad, @TipoFuero)
                """, new { id, registro.Fila, registro.Corporacion, registro.FolioRnd, registro.FechaDetencion, registro.AutoridadPuesta, registro.NombreDetenido, registro.Entidad, registro.TipoFuero }, tx);
        }
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'VALIDAR_CARGA', @id)", new { UsuarioId = usuario.Id, id }, tx);
        await tx.CommitAsync();
        return id;
    }

    // Consulta sin institución asignada: solo SUPER_USUARIO obtiene alcance nacional en esta entrega.
    private static string Filtro(UsuarioInfo usuario) => usuario.Rol == "SUPER_USUARIO"
        ? "1 = 1"
        : "(@Institucion IS NULL OR c.institucion = @Institucion) AND (@Ambito = 'NACIONAL' OR NOT EXISTS(SELECT 1 FROM primer_respondiente.registro r WHERE r.id_carga = c.id AND ((@Ambito = 'ESTATAL' AND r.entidad <> @Entidad) OR (@Ambito = 'FEDERAL' AND r.tipo_fuero <> 'FF'))))";

    public async Task<IEnumerable<CargaInfo>> ListarAsync(UsuarioInfo usuario)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<CargaInfo>($"SELECT TOP (200) {Columnas} FROM primer_respondiente.carga c WHERE c.estado = 'CONFIRMADA' AND {Filtro(usuario)} ORDER BY creado_utc DESC", usuario);
    }

    public async Task<CargaInfo?> ObtenerAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga c WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro(usuario)}", new { id, usuario.Institucion, usuario.Ambito, usuario.Entidad });
    }

    public async Task<CargaInfo?> ConfirmarAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var estado = await db.QuerySingleOrDefaultAsync<string>("""
            SELECT estado FROM primer_respondiente.carga WITH (UPDLOCK, HOLDLOCK)
            WHERE id = @id AND id_usuario = @UsuarioId AND institucion = @Institucion
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion }, tx);
        if(estado == null)
        {
            return null;
        }
        // Revalidar el alcance actual: pudo cambiar desde la carga.
        var fuera = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM primer_respondiente.registro WHERE id_carga = @id
            AND ((@Ambito = 'ESTATAL' AND entidad <> @Entidad) OR (@Ambito = 'FEDERAL' AND tipo_fuero <> 'FF'))
            """, new { id, usuario.Ambito, usuario.Entidad }, tx);
        if(fuera > 0)
        {
            throw new ArgumentException("La carga queda fuera del alcance actual del usuario.");
        }
        if(estado == "VALIDADA")
        {
            await db.ExecuteAsync("""
                UPDATE primer_respondiente.carga SET estado = 'CONFIRMADA', confirmado_utc = SYSUTCDATETIME(),
                    folio = CONCAT('EM-PR-', YEAR(SYSUTCDATETIME()), '-', NEXT VALUE FOR primer_respondiente.folio_seq)
                WHERE id = @id;
                INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'CONFIRMAR_CARGA', @id);
                """, new { id, UsuarioId = usuario.Id }, tx);
        }
        var carga = await db.QuerySingleAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
        return carga;
    }

    public async Task<byte[]?> ArchivoAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<byte[]>($"SELECT archivo FROM primer_respondiente.carga c WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro(usuario)}", new { id, usuario.Institucion, usuario.Ambito, usuario.Entidad });
    }
}
