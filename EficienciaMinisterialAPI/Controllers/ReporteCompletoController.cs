using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EficienciaMinisterialAPI.Controllers;

[ApiController, Authorize(Roles="FISCALIA,SUPER_USUARIO,CONSULTA"), Route("api/informes/completo")]
public sealed class ReporteCompletoController(SqlConnectionFactory factory) : ControllerBase
{
    private UsuarioInfo Usuario => (UsuarioInfo)HttpContext.Items["Usuario"]!;
    private bool Nacional => Usuario.Rol == "SUPER_USUARIO" || Usuario.Ambito == "NACIONAL";
    private object Alcance(DateTime? inicio=null, byte? entidad=null) => new { nacional=Nacional, asignada=Usuario.Entidad,
        institucion=Usuario.Institucion, inicio=inicio?.Date, entidad };
    private const string Fuente = """
        FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON c.id=r.id_carga
        JOIN catalogo.entidad e ON e.id=r.entidad JOIN catalogo.institucion i ON i.codigo=c.institucion
        WHERE c.estado='CONFIRMADA' AND c.vigente=1 AND (@nacional=1 OR r.entidad=@asignada)
        AND (@institucion IS NULL OR c.institucion=@institucion)
        """;
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos()
    {
        await using var db=await factory.AbrirAsync();
        var periodos=await db.QueryAsync<PeriodoInfo>("SELECT DISTINCT c.periodo_inicio Inicio,c.periodo_fin Fin "+Fuente+" ORDER BY Inicio DESC",Alcance());
        var entidades=await db.QueryAsync("SELECT DISTINCT e.id,e.nombre "+Fuente+" ORDER BY e.nombre",Alcance());
        return Ok(new {periodos,entidades,nacional=Nacional});
    }

    [HttpGet]
    public async Task<IActionResult> Consultar(DateTime inicio, byte? entidad=null, bool excel=false)
    {
        if(inicio==default || (!Nacional && entidad.HasValue && entidad!=Usuario.Entidad)) return BadRequest();
        await using var db=await factory.AbrirAsync();
        var registros=await db.QueryAsync<Fila>("""
            WITH base AS (SELECT r.id,c.folio Entrega,r.folio_rnd Folio,r.nombre_detenido Nombre,r.fecha_detencion Fecha,
                e.nombre Entidad,i.nombre Corporacion,r.estado_cruce Cruce,r.fila
            """+" "+Fuente+" AND c.periodo_inicio=@inicio AND (@entidad IS NULL OR r.entidad=@entidad)) "+"""
            SELECT b.*, f.folio FolioFiscalia,f.datos Datos
            FROM base b OUTER APPLY (
                SELECT CONCAT('EM-FIS-',YEAR(t.inicio),'-',t.id) folio,s.datos
                FROM fiscalia.respuesta s JOIN fiscalia.entrega t ON s.entrega=t.id
                WHERE s.registro=b.id AND t.vigente=1 AND (@nacional=1 OR t.entidad=@asignada)
            ) f ORDER BY b.Entidad,b.Corporacion,b.Entrega,b.fila,f.folio
            """,Alcance(inicio,entidad),commandTimeout:180);
        string[] columnas=["Corporación","Entidad de detención","Entrega de primer respondiente","Folio RND","Nombre informado","Fecha de detención","Cruce RND","Entrega de Fiscalía",..PlantillaFiscaliaService.Respuesta];
        var filas=registros.Select(r=>{
            var respuestas=r.Datos==null ? null : JsonSerializer.Deserialize<Dictionary<string,string>>(r.Datos);
            return new string[] {r.Corporacion,r.Entidad,r.Entrega,r.Folio,r.Nombre,r.Fecha.ToString("yyyy-MM-dd"),r.Cruce,r.FolioFiscalia ?? (r.Cruce=="LOCALIZADO"?"Pendiente":"No aplica")}
                .Concat(PlantillaFiscaliaService.Respuesta.Select(c=>respuestas?.GetValueOrDefault(c) ?? "")).ToArray();
        }).ToList();
        if(!excel) return Ok(new {columnas,filas});
        if(filas.Count > XLHelper.MaxRowNumber - 2) return BadRequest(new {mensaje="Seleccione una entidad para reducir el número de registros."});
        using var libro=new XLWorkbook();
        var hoja=libro.AddWorksheet("Seguimiento ministerial");
        hoja.Cell(2,1).InsertData(new[]{columnas});
        if (filas.Count > 0) hoja.Cell(3,1).InsertData(filas);
        void Encabezado(int desde, int hasta, string titulo, string color)
        {
            var banda = hoja.Range(1, desde, 1, hasta);
            if (desde < hasta) banda.Merge();
            banda.FirstCell().Value = titulo;
            var estilo = hoja.Range(1, desde, 2, hasta).Style;
            estilo.Fill.BackgroundColor = XLColor.FromHtml(color);
            estilo.Font.FontColor = XLColor.White;
            estilo.Font.Bold = true;
            estilo.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        Encabezado(1, 6, "Información reportada por primer respondiente", "#621132");
        Encabezado(7, 7, "Registro Nacional de Detenciones", "#B38E5D");
        Encabezado(8, columnas.Length, "Seguimiento de Fiscalía", "#285C4D");
        hoja.Row(1).Height = 26;
        hoja.Row(2).Height = 32;
        hoja.Row(2).Style.Alignment.WrapText = true;
        hoja.SheetView.FreezeRows(2);
        hoja.Range(2, 1, filas.Count + 2, columnas.Length).SetAutoFilter();
        hoja.Columns(1, columnas.Length).Width = 26;
        hoja.Column(7).Width = 36;
        using var salida=new MemoryStream();
        libro.SaveAs(salida);
        return File(salida.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private sealed class Fila
    {
        public string Corporacion {get;set;}="";
        public string Entidad {get;set;}="";
        public string Entrega {get;set;}="";
        public string Folio {get;set;}="";
        public string Nombre {get;set;}="";
        public DateTime Fecha {get;set;}
        public string Cruce {get;set;}="";
        public string? FolioFiscalia {get;set;}
        public string? Datos {get;set;}
    }
}
