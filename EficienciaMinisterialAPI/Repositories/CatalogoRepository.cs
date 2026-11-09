using Dapper;
using System.Diagnostics;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CatalogoRepository(SqlConnectionFactory factory, ILogger<CatalogoRepository> logger)
{
    public async Task<object> ObtenerUsuariosAsync()
    {
        await using var db = await factory.AbrirAsync();
        var entidades = await db.QueryAsync("SELECT id, nombre FROM catalogo.entidad WHERE activo = 1 AND permite_captura = 1 ORDER BY nombre");
        var instituciones = await db.QueryAsync("SELECT codigo, nombre, tipo FROM catalogo.institucion WHERE activo = 1 ORDER BY nombre");
        var perfiles = await db.QueryAsync<PerfilInfo>("SELECT codigo, nombre, tipo_institucion AS TipoInstitucion, requiere_institucion AS RequiereInstitucion, permite_estatal AS PermiteEstatal FROM catalogo.perfil WHERE activo = 1 ORDER BY nombre");
        var ambitos = await db.QueryAsync<AmbitoInfo>("SELECT codigo, nombre, requiere_entidad AS RequiereEntidad FROM catalogo.ambito WHERE activo = 1 ORDER BY requiere_entidad");
        return new { entidades, instituciones, perfiles, ambitos };
    }

    public async Task<(List<PerfilInfo> Perfiles, List<AmbitoInfo> Ambitos)> ObtenerPerfilesAsync()
    {
        await using var db = await factory.AbrirAsync();
        var perfiles = await db.QueryAsync<PerfilInfo>("SELECT codigo, nombre, tipo_institucion AS TipoInstitucion, requiere_institucion AS RequiereInstitucion, permite_estatal AS PermiteEstatal FROM catalogo.perfil WHERE activo = 1");
        var ambitos = await db.QueryAsync<AmbitoInfo>("SELECT codigo, nombre, requiere_entidad AS RequiereEntidad FROM catalogo.ambito WHERE activo = 1");
        return (perfiles.ToList(), ambitos.ToList());
    }

    public async Task<CatalogoCaptura> ObtenerCapturaAsync()
    {
        await using var db = await factory.AbrirAsync();
        var entidades = await db.QueryAsync<(string Valor, byte Codigo)>("""
            SELECT nombre, id FROM catalogo.entidad WHERE activo = 1 AND permite_captura = 1
            UNION SELECT a.valor, e.id FROM catalogo.entidad_alias a JOIN catalogo.entidad e ON e.id = a.id_entidad WHERE e.activo = 1 AND e.permite_captura = 1
            """);
        var corporaciones = await db.QueryAsync<(string Valor, string Codigo)>("""
            SELECT codigo, codigo FROM catalogo.institucion WHERE activo = 1 AND tipo = 'CORPORACION'
            UNION SELECT nombre, codigo FROM catalogo.institucion WHERE activo = 1 AND tipo = 'CORPORACION'
            UNION SELECT a.valor, i.codigo FROM catalogo.institucion_alias a JOIN catalogo.institucion i ON i.codigo = a.codigo WHERE i.activo = 1 AND i.tipo = 'CORPORACION'
            """);
        var fueros = await db.QueryAsync<(string Valor, string Codigo)>("""
            SELECT codigo, codigo FROM catalogo.fuero WHERE activo = 1
            UNION SELECT nombre, codigo FROM catalogo.fuero WHERE activo = 1
            """);
        return new CatalogoCaptura
        {
            Entidades = entidades.ToDictionary(e => e.Valor, e => e.Codigo),
            Corporaciones = corporaciones.ToDictionary(e => e.Valor, e => e.Codigo),
            Fueros = fueros.ToDictionary(e => e.Valor, e => e.Codigo)
        };
    }

    public async Task<IEnumerable<PeriodoInfo>> ObtenerPeriodosAsync()
    {
        var reloj = Stopwatch.StartNew();
        await using var db = await factory.AbrirAsync();
        var conexionMs = reloj.ElapsedMilliseconds;
        var periodos = await db.QueryAsync<PeriodoInfo>("""
            DECLARE @hoy date = CONVERT(date, SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Central Standard Time (Mexico)');
            IF NOT EXISTS(SELECT 1 FROM primer_respondiente.periodo WHERE @hoy BETWEEN inicio AND fin)
                EXEC primer_respondiente.GenerarPeriodos;
            SELECT id, inicio, fin FROM primer_respondiente.periodo WHERE habilitado = 1 AND cerrado = 0 ORDER BY inicio DESC;
            """);
        logger.LogInformation("Catálogo de periodos: conexión {ConexionMs} ms; consulta {ConsultaMs} ms; total {TotalMs} ms", conexionMs, reloj.ElapsedMilliseconds - conexionMs, reloj.ElapsedMilliseconds);
        return periodos;
    }
}
