using Dapper;
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CargaRepository(SqlConnectionFactory factory, ILogger<CargaRepository> logger)
{
    private const string Columnas = "id, folio, institucion, nombre_archivo AS NombreArchivo, periodo_inicio AS PeriodoInicio, periodo_fin AS PeriodoFin, estado, total_registros AS TotalRegistros, creado_utc AS CreadoUtc, confirmado_utc AS ConfirmadoUtc";

    public async Task<Guid> GuardarAsync(UsuarioInfo usuario, string nombre, string hash, int idPeriodo, List<RegistroCarga> registros)
    {
        var reloj = Stopwatch.StartNew();
        await using var db = await factory.AbrirAsync();
        var conexionMs = reloj.ElapsedMilliseconds;
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var periodo = await db.QuerySingleOrDefaultAsync<PeriodoInfo>("""
            SELECT id, inicio, fin FROM primer_respondiente.periodo WITH(HOLDLOCK)
            WHERE id = @idPeriodo AND habilitado = 1 AND cerrado = 0
            """, new { idPeriodo }, tx);
        if(periodo == null)
        {
            throw new ArgumentException("El periodo no existe, está deshabilitado o ya fue cerrado. Actualice la lista.");
        }
        var id = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT primer_respondiente.carga(id, id_usuario, institucion, nombre_archivo, sha256, id_periodo, periodo_inicio, periodo_fin, estado, total_registros)
            VALUES(@id, @UsuarioId, @Institucion, @nombre, @hash, @idPeriodo, @inicio, @fin, 'VALIDADA', @total)
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, nombre, hash, idPeriodo, inicio = periodo.Inicio, fin = periodo.Fin, total = registros.Count }, tx);
        var preparacionMs = reloj.ElapsedMilliseconds - conexionMs;
        using var datos = PrepararRegistros(id, registros);
        using var copia = new SqlBulkCopy(db, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, tx)
        {
            DestinationTableName = "primer_respondiente.registro"
        };
        foreach(DataColumn columna in datos.Columns)
        {
            copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
        }
        await copia.WriteToServerAsync(datos);
        var loteMs = reloj.ElapsedMilliseconds - conexionMs - preparacionMs;
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'VALIDAR_CARGA', @id)", new { UsuarioId = usuario.Id, id }, tx);
        await tx.CommitAsync();
        logger.LogInformation("Guardado de carga {IdCarga}: {Registros} registros; conexión {ConexionMs} ms; preparación {PreparacionMs} ms; lote {LoteMs} ms; total {TotalMs} ms", id, registros.Count, conexionMs, preparacionMs, loteMs, reloj.ElapsedMilliseconds);
        return id;
    }

    public static DataTable PrepararRegistros(Guid idCarga, IReadOnlyList<RegistroCarga> registros)
    {
        var datos = new DataTable();
        datos.Columns.Add("id_carga", typeof(Guid));
        datos.Columns.Add("fila", typeof(int));
        datos.Columns.Add("corporacion", typeof(string));
        datos.Columns.Add("folio_rnd", typeof(string));
        datos.Columns.Add("fecha_detencion", typeof(DateTime));
        datos.Columns.Add("autoridad_puesta", typeof(string));
        datos.Columns.Add("nombre_detenido", typeof(string));
        datos.Columns.Add("entidad", typeof(byte));
        datos.Columns.Add("tipo_fuero", typeof(string));
        foreach(var registro in registros)
        {
            datos.Rows.Add(idCarga, registro.Fila, registro.Corporacion, registro.FolioRnd, registro.FechaDetencion,
                registro.AutoridadPuesta, registro.NombreDetenido, registro.Entidad, registro.TipoFuero);
        }
        return datos;
    }

    // Se aplica únicamente el acceso elegido; no se mezclan instituciones ni entidades de otras asignaciones.
    private static string Filtro(UsuarioInfo usuario) => usuario.Rol == "SUPER_USUARIO"
        ? "1 = 1"
        : "(@Institucion IS NULL OR c.institucion = @Institucion) AND (@Ambito = 'NACIONAL' OR NOT EXISTS(SELECT 1 FROM primer_respondiente.registro r WHERE r.id_carga = c.id AND ((@Ambito = 'ESTATAL' AND r.entidad <> @Entidad))))";

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
            WHERE id = @id AND id_usuario = @UsuarioId AND (@EsSuper = 1 OR institucion = @Institucion)
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, EsSuper = usuario.Rol == "SUPER_USUARIO" }, tx);
        if(estado == null)
        {
            return null;
        }
        // Revalidar el alcance actual: pudo cambiar desde la carga.
        var fuera = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM primer_respondiente.registro WHERE id_carga = @id
            AND ((@Ambito = 'ESTATAL' AND entidad <> @Entidad))
            """, new { id, usuario.Ambito, usuario.Entidad }, tx);
        if(fuera > 0)
        {
            throw new ArgumentException("La carga queda fuera del alcance actual del usuario.");
        }
        if(estado == "VALIDADA")
        {
            var habilitado = await db.ExecuteScalarAsync<bool>("""
                SELECT CAST(CASE WHEN EXISTS(
                    SELECT 1 FROM primer_respondiente.periodo p WITH(UPDLOCK, HOLDLOCK)
                    JOIN primer_respondiente.carga c ON c.id_periodo = p.id
                    WHERE c.id = @id AND p.habilitado = 1 AND p.cerrado = 0
                ) THEN 1 ELSE 0 END AS bit)
                """, new { id }, tx);
            if(!habilitado)
            {
                throw new ArgumentException("El periodo no está habilitado. Vuelva a validar el archivo con un periodo disponible.");
            }
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

    public async Task<CargaInfo?> ObtenerParaAcuseAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<CargaInfo>($"""
            SELECT {Columnas} FROM primer_respondiente.carga c WHERE c.id = @id AND {Filtro(usuario)}
            AND (c.estado = 'CONFIRMADA' OR (c.estado = 'VALIDADA' AND c.id_usuario = @UsuarioId AND @PuedeCargar = 1))
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, usuario.Ambito, usuario.Entidad, PuedeCargar = usuario.Rol is "SUPER_USUARIO" or "PRIMER_RESPONDIENTE" });
    }

    public async Task<IEnumerable<RegistroDescarga>> RegistrosAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<RegistroDescarga>($"""
            SELECT r.corporacion, r.folio_rnd AS FolioRnd, r.fecha_detencion AS FechaDetencion,
                r.autoridad_puesta AS AutoridadPuesta, r.nombre_detenido AS NombreDetenido,
                e.nombre AS Entidad, COALESCE(f.nombre, r.tipo_fuero) AS TipoFuero
            FROM primer_respondiente.registro r
            JOIN primer_respondiente.carga c ON c.id = r.id_carga
            JOIN catalogo.entidad e ON e.id = r.entidad
            LEFT JOIN catalogo.fuero f ON f.codigo = r.tipo_fuero
            WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro(usuario)} ORDER BY r.fila
            """, new { id, usuario.Institucion, usuario.Ambito, usuario.Entidad });
    }
}
