using Dapper;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CargaRepository(SqlConnectionFactory factory, ConfiguracionRepository configuracion)
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

    private const string Filtro = "(@AlcanceTotal = 1 OR ((@Institucion IS NULL OR c.institucion = @Institucion) AND NOT EXISTS(SELECT 1 FROM primer_respondiente.registro r WHERE r.id_carga = c.id AND ((@RequiereEntidad = 1 AND r.entidad <> @Entidad) OR (@Fuero IS NOT NULL AND r.tipo_fuero <> @Fuero)))))";

    private async Task<DynamicParameters> ParametrosAsync(UsuarioInfo usuario, Guid? id = null)
    {
        var config = await configuracion.ObtenerAsync();
        var rol = config.Roles.Single(r => r.Codigo == usuario.Rol);
        var ambito = config.Ambitos.Single(a => a.Codigo == usuario.Ambito);
        var parametros = new DynamicParameters(new { id, usuario.Institucion, usuario.Entidad, rol.AlcanceTotal, ambito.RequiereEntidad, ambito.Fuero });
        parametros.Add("Limite", config.Valor<int>("Carga.LimiteConsulta"));
        return parametros;
    }

    public async Task<IEnumerable<CargaInfo>> ListarAsync(UsuarioInfo usuario)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<CargaInfo>($"SELECT TOP (@Limite) {Columnas} FROM primer_respondiente.carga c WHERE c.estado = 'CONFIRMADA' AND {Filtro} ORDER BY creado_utc DESC", await ParametrosAsync(usuario));
    }

    public async Task<CargaInfo?> ObtenerAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga c WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro}", await ParametrosAsync(usuario, id));
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
        var parametros = await ParametrosAsync(usuario, id);
        var fuera = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM primer_respondiente.registro WHERE id_carga = @id
            AND ((@RequiereEntidad = 1 AND entidad <> @Entidad) OR (@Fuero IS NOT NULL AND tipo_fuero <> @Fuero))
            """, parametros, tx);
        if(fuera > 0)
        {
            throw new ArgumentException("La carga queda fuera del alcance actual del usuario.");
        }
        if(estado == "VALIDADA")
        {
            await db.ExecuteAsync("""
                UPDATE primer_respondiente.carga SET estado = 'CONFIRMADA', confirmado_utc = SYSUTCDATETIME(),
                    folio = CONCAT(@Prefijo, YEAR(SYSUTCDATETIME()), '-', NEXT VALUE FOR primer_respondiente.folio_seq)
                WHERE id = @id;
                INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'CONFIRMAR_CARGA', @id);
                """, new { id, UsuarioId = usuario.Id, Prefijo = (await configuracion.ObtenerAsync()).Valor<string>("Carga.PrefijoFolio") }, tx);
        }
        var carga = await db.QuerySingleAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
        return carga;
    }

    public async Task<byte[]?> ArchivoAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<byte[]>($"SELECT archivo FROM primer_respondiente.carga c WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro}", await ParametrosAsync(usuario, id));
    }
}
