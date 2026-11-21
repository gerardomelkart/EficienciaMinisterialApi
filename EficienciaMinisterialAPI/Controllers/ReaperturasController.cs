using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EficienciaMinisterialAPI.Controllers;

[ApiController, Authorize(Roles="SUPER_USUARIO"), Route("api/reaperturas")]
public sealed class ReaperturasController(SqlConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        await using var db = await factory.AbrirAsync();
        return Ok(await db.QueryAsync("""
            SELECT 'Primer respondiente' modulo,c.folio,i.nombre alcance,c.periodo_inicio inicio,c.periodo_fin fin,c.reabierta
            FROM primer_respondiente.carga c JOIN catalogo.institucion i ON i.codigo=c.institucion WHERE c.estado='CONFIRMADA' AND c.vigente=1
            UNION ALL SELECT 'Fiscalía',CONCAT('EM-FIS-',YEAR(f.inicio),'-',f.id),COALESCE(e.nombre,'Nacional'),f.inicio,f.fin,f.reabierta
            FROM fiscalia.entrega f LEFT JOIN catalogo.entidad e ON e.id=f.entidad WHERE f.vigente=1 ORDER BY inicio DESC, alcance, folio
            """));
    }

    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos()
    {
        await using var db = await factory.AbrirAsync();
        using var lector = await db.QueryMultipleAsync("""
            SELECT YEAR(periodo_inicio) anio FROM primer_respondiente.carga WHERE estado='CONFIRMADA' AND vigente=1
            UNION SELECT YEAR(inicio) FROM fiscalia.entrega WHERE vigente=1 ORDER BY anio DESC;
            SELECT id,nombre FROM catalogo.entidad WHERE id BETWEEN 1 AND 32 ORDER BY nombre;
            SELECT DISTINCT i.codigo,i.nombre FROM catalogo.institucion i JOIN primer_respondiente.carga c
            ON c.institucion=i.codigo WHERE c.estado='CONFIRMADA' AND c.vigente=1 ORDER BY i.nombre;
            """);
        return Ok(new { anios = await lector.ReadAsync<int>(), entidades = await lector.ReadAsync(), corporaciones = await lector.ReadAsync() });
    }

    [HttpGet("consulta")]
    public async Task<IActionResult> Consultar(int? anio, int? mes, int? quincena, int? entidad, string? corporacion,
        string orden = "inicio", bool descendente = true, int pagina = 1, int tamano = 20)
    {
        if(anio is < 1 or > 9999 || mes is < 1 or > 12 || quincena is < 1 or > 2 || entidad is < 1 or > 32 ||
            pagina is < 1 or > 1000000 || tamano is not (20 or 50 or 100)) return BadRequest();
        var columna = orden switch {
            "inicio" => "inicio", "modulo" => "modulo", "alcance" => "alcance", "folio" => "folio",
            "reabierta" => "reabierta", _ => null
        };
        if(columna == null) return BadRequest();
        await using var db = await factory.AbrirAsync();
        using var resultado = await db.QueryMultipleAsync($"""
            SELECT modulo,folio,alcance,inicio,fin,reabierta INTO #filtradas FROM (
                SELECT N'Primer respondiente' modulo,c.folio,i.nombre alcance,c.periodo_inicio inicio,c.periodo_fin fin,c.reabierta
                FROM primer_respondiente.carga c JOIN catalogo.institucion i ON i.codigo=c.institucion
                WHERE c.estado='CONFIRMADA' AND c.vigente=1 AND (@corporacion IS NULL OR c.institucion=@corporacion)
                AND (@entidad IS NULL OR EXISTS(SELECT 1 FROM primer_respondiente.registro r WHERE r.id_carga=c.id AND r.entidad=@entidad))
                UNION ALL SELECT N'Fiscalía',CONCAT('EM-FIS-',YEAR(f.inicio),'-',f.id),COALESCE(e.nombre,'Nacional'),f.inicio,f.fin,f.reabierta
                FROM fiscalia.entrega f LEFT JOIN catalogo.entidad e ON e.id=f.entidad
                WHERE f.vigente=1 AND @corporacion IS NULL AND (@entidad IS NULL OR f.entidad=@entidad OR
                    (f.entidad IS NULL AND EXISTS(SELECT 1 FROM fiscalia.respuesta s JOIN primer_respondiente.registro r ON r.id=s.registro
                     WHERE s.entrega=f.id AND r.entidad=@entidad)))
            ) entregas WHERE (@anio IS NULL OR YEAR(inicio)=@anio) AND (@mes IS NULL OR MONTH(inicio)=@mes)
                AND (@quincena IS NULL OR CASE WHEN DAY(inicio)<=15 THEN 1 ELSE 2 END=@quincena) OPTION(RECOMPILE);
            SELECT COUNT(*) FROM #filtradas;
            SELECT * FROM #filtradas ORDER BY {columna} {(descendente ? "DESC" : "ASC")}, {(columna == "folio" ? "inicio" : "folio")} ASC
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY;
            """, new { anio, mes, quincena, entidad, corporacion, salto=(pagina-1)*tamano, tamano });
        return Ok(new { total=await resultado.ReadSingleAsync<int>(), filas=await resultado.ReadAsync() });
    }

    private const string Impacto = """
        SELECT f.id, CONCAT('EM-FIS-',YEAR(f.inicio),'-',f.id) folio, COALESCE(e.nombre,'Nacional') alcance
        FROM fiscalia.entrega f LEFT JOIN catalogo.entidad e ON e.id=f.entidad
        WHERE f.vigente=1 AND EXISTS (
            SELECT 1 FROM fiscalia.respuesta s JOIN primer_respondiente.registro r ON r.id=s.registro
            JOIN primer_respondiente.carga c ON c.id=r.id_carga
            WHERE s.entrega=f.id AND c.folio=@folio AND c.vigente=1 AND c.estado='CONFIRMADA')
        ORDER BY f.id
        """;

    public sealed record Afectada(long Id, string Folio, string Alcance);
    public sealed record Autorizacion(long[]? Entregas);

    [HttpGet("{folio}/impacto")]
    public async Task<IActionResult> ConsultarImpacto(string folio)
    {
        await using var db = await factory.AbrirAsync();
        return Ok(await db.QueryAsync<Afectada>(Impacto, new { folio }));
    }

    [HttpPost("{folio}")]
    public async Task<IActionResult> Reabrir(string folio, [FromBody] Autorizacion autorizacion)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        bool fiscalia = folio.StartsWith("EM-FIS-", StringComparison.Ordinal);
        var sql = fiscalia ? "UPDATE fiscalia.entrega SET reabierta=1 WHERE CONCAT('EM-FIS-',YEAR(inicio),'-',id)=@folio AND vigente=1 AND reabierta=0"
            : "UPDATE primer_respondiente.carga SET reabierta=1 WHERE folio=@folio AND estado='CONFIRMADA' AND vigente=1 AND reabierta=0";
        var usuario = (UsuarioInfo)HttpContext.Items["Usuario"]!;
        if(!fiscalia)
        {
            var periodo = await db.QuerySingleOrDefaultAsync<PeriodoInfo>("SELECT periodo_inicio Inicio, periodo_fin Fin FROM primer_respondiente.carga WHERE folio=@folio", new { folio }, tx);
            if(periodo == null) return NotFound();
            var bloqueo = await db.ExecuteScalarAsync<int>("""
                DECLARE @r int;
                EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                SELECT @r;
                """, new { recurso=$"EM-FISCALIA-{periodo.Inicio:yyyyMMdd}-{periodo.Fin:yyyyMMdd}" }, tx);
            if(bloqueo < 0) return Conflict(new { mensaje="Hay una entrega en curso. Vuelva a revisar el impacto." });
            var afectadas = (await db.QueryAsync<Afectada>(Impacto, new { folio }, tx)).ToList();
            if(!afectadas.Select(f => f.Id).SequenceEqual((autorizacion.Entregas ?? []).Order()))
                return Conflict(new { mensaje="Cambiaron las fiscalías afectadas. Abra nuevamente el detalle antes de autorizar." });
            foreach(var f in afectadas)
                await db.ExecuteAsync("""
                    UPDATE fiscalia.entrega SET reabierta=1 WHERE id=@entrega;
                    INSERT seguridad.reapertura(modulo,folio,usuario) VALUES('FISCALIA_ORIGEN',@folio,@usuarioId);
                    """, new { entrega=f.Id, folio=f.Folio, usuarioId=usuario.Id }, tx);
        }
        if(await db.ExecuteAsync(sql,new { folio },tx)!=1) return Conflict(new { mensaje="La entrega ya fue reabierta o dejó de estar vigente. Actualice el listado." });
        await db.ExecuteAsync("INSERT seguridad.reapertura(modulo,folio,usuario) VALUES(@modulo,@folio,@Id)", new { modulo=fiscalia?"FISCALIA":"PRIMER_RESPONDIENTE", folio,usuario.Id },tx);
        await tx.CommitAsync();
        return NoContent();
    }
}
