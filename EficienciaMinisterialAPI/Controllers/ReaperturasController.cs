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

    [HttpPost("{folio}")]
    public async Task<IActionResult> Reabrir(string folio)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = await db.BeginTransactionAsync();
        bool fiscalia = folio.StartsWith("EM-FIS-", StringComparison.Ordinal);
        var sql = fiscalia ? "UPDATE fiscalia.entrega SET reabierta=1 WHERE CONCAT('EM-FIS-',YEAR(inicio),'-',id)=@folio AND vigente=1 AND reabierta=0"
            : "UPDATE primer_respondiente.carga SET reabierta=1 WHERE folio=@folio AND estado='CONFIRMADA' AND vigente=1 AND reabierta=0";
        if(await db.ExecuteAsync(sql,new { folio },tx)!=1) return Conflict(new { mensaje="La entrega ya fue reabierta o dejó de estar vigente. Actualice el listado." });
        var usuario = (UsuarioInfo)HttpContext.Items["Usuario"]!;
        await db.ExecuteAsync("INSERT seguridad.reapertura(modulo,folio,usuario) VALUES(@modulo,@folio,@Id)", new { modulo=fiscalia?"FISCALIA":"PRIMER_RESPONDIENTE", folio,usuario.Id },tx);
        await tx.CommitAsync();
        return NoContent();
    }
}
