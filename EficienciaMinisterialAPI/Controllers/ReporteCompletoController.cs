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
    private object Alcance(DateTime? inicio=null, byte? entidad=null, string? corporacion=null) => new { nacional=Nacional, asignada=Usuario.Entidad,
        institucion=Usuario.Institucion, inicio=inicio?.Date, entidad, corporacion };
    private const string Fuente = """
        FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON c.id=r.id_carga
        JOIN catalogo.entidad e ON e.id=r.entidad JOIN catalogo.institucion i ON i.codigo=c.institucion
        WHERE c.estado='CONFIRMADA' AND c.vigente=1 AND c.reabierta=0 AND (@nacional=1 OR r.entidad=@asignada)
        AND (@institucion IS NULL OR c.institucion=@institucion)
        """;
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos()
    {
        await using var db=await factory.AbrirAsync();
        var periodos=await db.QueryAsync<PeriodoInfo>("SELECT DISTINCT c.periodo_inicio Inicio,c.periodo_fin Fin "+Fuente+" ORDER BY Inicio DESC",Alcance());
        var entidades=await db.QueryAsync("SELECT DISTINCT e.id,e.nombre "+Fuente+" ORDER BY e.nombre",Alcance());
        var corporaciones=await db.QueryAsync("SELECT DISTINCT i.codigo,i.nombre "+Fuente+" ORDER BY i.nombre",Alcance());
        return Ok(new {periodos,entidades,corporaciones,nacional=Nacional});
    }

    [HttpGet]
    public async Task<IActionResult> Consultar(DateTime inicio, byte? entidad=null, bool excel=false, string? corporacion=null, string? texto=null)
    {
        if(inicio==default || (!Nacional && entidad.HasValue && entidad!=Usuario.Entidad)) return BadRequest();
        await using var db=await factory.AbrirAsync();
        var registros=await db.QueryAsync<Fila>("""
            WITH base AS (SELECT r.id,c.folio Entrega,r.folio_rnd Folio,r.nombre_detenido Nombre,r.fecha_detencion Fecha,
                e.nombre Entidad,i.nombre Corporacion,r.estado_cruce Cruce,r.rnd_coincidencias Coincidencias,r.fila
            """+" "+Fuente+" AND c.periodo_inicio=@inicio AND (@entidad IS NULL OR r.entidad=@entidad) AND (@corporacion IS NULL OR c.institucion=@corporacion)) "+"""
            SELECT b.*, f.folio FolioFiscalia,f.datos Datos
            FROM base b OUTER APPLY (
                SELECT CONCAT('EM-FIS-',YEAR(t.inicio),'-',t.id) folio,s.datos
                FROM fiscalia.respuesta s JOIN fiscalia.entrega t ON s.entrega=t.id
                WHERE s.registro=b.id AND t.vigente=1 AND t.reabierta=0 AND (@nacional=1 OR t.entidad=@asignada)
            ) f ORDER BY b.Entidad,b.Corporacion,b.Entrega,b.fila,f.folio
            """,Alcance(inicio,entidad,corporacion),commandTimeout:180);
        string[] columnas=["ENTIDAD", "num", "id_key", "corporacion_reporta", "nombre_detenido_corporacion", "fecha_detencion_corporacion", "folio_RND", "num_detenido", "num_detencion", "nombre_detenido", "fecha_detencion", "municipio_detencion", "tipo_fuero", "id_puesta", "autoridad_puesta", "motivo_detencion", "tipo detención", "recibido_por_fiscalia", "libertad_sede_ministerial", "razon_libertad", "audiencia_inicial", "legal_detencion", "motivo_no_legal", "vinculacion_proceso", "motivo_no_vinculacion", "prision_preventiva", "medida_cautelar", "MASC", "procedimiento_abreviado", "inicia_juicio", "descripcion_conclusion", "comentarios", "Entrega de primer respondiente", "Folio RND informado", "Cruce RND", "Entrega de Fiscalía"];
        var busqueda=texto?.Trim();
        var comparador=System.Globalization.CultureInfo.GetCultureInfo("es-MX").CompareInfo;
        var filas=registros.Select((r, indice)=>{
            var respuestas=r.Datos==null ? null : JsonSerializer.Deserialize<Dictionary<string,string>>(r.Datos);
            var cruces = JsonSerializer.Deserialize<List<CoincidenciaRnd>>(r.Coincidencias ?? "[]") ?? [];
            string Campo(Func<CoincidenciaRnd,string?> campo) => string.Join("\n", cruces.Select(campo));
            return new string[] {r.Entidad,(indice+1).ToString(),Campo(c=>string.IsNullOrEmpty(c.Folio) || string.IsNullOrEmpty(c.IdDetenido) || string.IsNullOrEmpty(c.IdDetencion) ? "" : $"{c.Folio}|&/{c.IdDetenido}|&/{c.IdDetencion}"),
                r.Corporacion,r.Nombre,r.Fecha.ToString("yyyy-MM-dd"),Campo(c=>c.Folio),Campo(c=>c.IdDetenido),
                Campo(c=>c.IdDetencion),Campo(c=>c.NombrePr),Campo(c=>c.FechaDetencion?.ToString("yyyy-MM-dd")),
                Campo(c=>c.Municipio),Campo(c=>c.Fuero),Campo(c=>c.IdPuesta),Campo(c=>c.Autoridad),Campo(c=>c.Motivo),Campo(c=>c.Tipo)}
                .Concat(PlantillaFiscaliaService.Respuesta.Select(c=>respuestas?.GetValueOrDefault(c) ?? ""))
                .Concat(new[]{r.Entrega,r.Folio,r.Cruce,r.FolioFiscalia ?? (r.Cruce=="LOCALIZADO"?"Pendiente":"No aplica")}).ToArray();
        }).Where(fila=>string.IsNullOrEmpty(busqueda) || fila.Any(valor=>
            comparador.IndexOf(valor ?? "",busqueda,System.Globalization.CompareOptions.IgnoreCase |
                System.Globalization.CompareOptions.IgnoreNonSpace)>=0)).ToList();
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
        Encabezado(7, 17, "Registro Nacional de Detenciones", "#B38E5D");
        Encabezado(18, 32, "Seguimiento de Fiscalía", "#285C4D");
        Encabezado(33, columnas.Length, "Control de entregas", "#454545");
        hoja.Range(3,3, Math.Max(3, filas.Count+2),17).Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 26;
        hoja.Row(2).Height = 32;
        hoja.Row(2).Style.Alignment.WrapText = true;
        hoja.SheetView.FreezeRows(2);
        hoja.Range(2, 1, filas.Count + 2, columnas.Length).SetAutoFilter();
        hoja.Columns(1, columnas.Length).Width = 26;
        hoja.Column(7).Width = 36;
        var diccionario = libro.AddWorksheet("Diccionario de datos");
        string[][] definiciones = [
            ["Variable", "Campo", "Tipo", "Descripción"],
            ["Entidad federativa", "Entidad", "Texto", "Nombre de la entidad federativa"],
            ["Número ", "num", "Numérico", "Número consecutivo asignado por el CNI-SESNSP"],
            ["Llave única de registro", "id_key", "Alfanumérico", "Llave única de registro asignado por el CNI-SESNSP"],
            ["Corporación federal ", "corporación_reporta", "Texto", "Corporación federal que reporta la detección:\n1.  Guardia Nacional,\n2.  Secretaría de la Defensa Nacional (SEDENA), \n3.  Secretaría de Marina (SEMAR), y\n4. Secretaría de Seguridad y Protección Ciudadana (SSPC)"],
            ["Nombre de la persona detenida", "nombre_detenido_corporación", "Texto", "Nombre de la persona detenida reportada por la corporación federal"],
            ["Fecha de la detención", "fecha_detencion_corporacion", "Fecha", "Fecha (AAAA/MM/DD) de la detención reportada por la corporación federal"],
            ["Folio del Registro Nacional de Detenciones", "folio_RND", "Numérico", "Folio asignado por el Registro Nacional de Detenciones (RND)"],
            ["Número de la detección", "num_detencion", "Numérico", "Se refiere al número que se asignó a la detección en el RND"],
            ["Nombre del detenido", "nombre_detenido", "Texto", "Nombre de la persona detenida registrado en el RND"],
            ["Fecha de la detención", "fecha_detencion", "Alfanumérico", "Fecha (AAAA/MM/DD) de la detención registrada en el RND"],
            ["Municipio de detencion", "municipio_detencion", "Texto", "Demarcación territorial donde se llevó a cabo la detención reportada en el RND"],
            ["Tipo de fuero", "tipo_fuero", "Texto", "Se refiere al fuero competente registrado en el RND: \n1.     Fuero común  \n2.     Fuero federal"],
            ["Número único (ID) de la persona detenida a puesta a disposición", "id_puesta", "Numérico", "Número único registrado en RND de la persona detenida a puesta a disposición"],
            ["Autoridad que tiene a su disposición la persona detenida ", "autoridad_puesta", "Texto", "Se refiere a la autoridad que recibió a la persona detenida. En caso de venir en blanco, implica que la Fiscalía/Procuraduría General de Justicia Estatal no actualizó este registro en el RND"],
            ["Motivo de la detención", "motivo_detencion", "Texto", "Motivo por el cual la persona registrada en el RND fue detenida "],
            ["Tipo de detención", "tipo detención", "Texto", "Se refiere a la forma de la detención: Flagrancia, Caso urgente, y  Orden de aprehensión"],
            ["¿La persona detenida fue recibida por la Fiscalía/Procuraduría General de Justicia Estatal?", "recibido_por_fiscalia", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí se encontró o no, a la persona registrada en el RND"],
            ["¿Se le otorgó la libertad ministerial a la persona detenida? ", "libertad_sede_ministerial", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí la persona registrada en el RND se le otorgó o no la libertad ministerial"],
            ["Razón de la libertad ministerial", "razon_libertad", "Texto", "Detalle de los motivos por los cuales se le otorgó la libertad ministerial a la persona registrada en el RND"],
            ["¿La persona detenida fue presentada en audiencia inicial?", "audiencia_inicial", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí la persona registrada en el RND fue o no presentada en audiencia inicial"],
            ["¿Fue legal la detención?", "legal_detencion", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí el juez calificó como legal o no la detención de la persona registrada en el RND"],
            ["Motivo de la detención ilegal", "motivo_no_legal", "Texto", "Descripción del motivo por el cual el juez calificó que la detención de la persona registrada en el RND fue ilegal"],
            ["¿La persona detenida se vinculó a proceso?", "vinculacion_proceso", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí el juez determinó vincular o no a proceso a la persona registrada en el RND"],
            ["Motivo de la no vinculación a proceso", "motivo_no_vinculacion", "Texto", "Descripción de la razón por la cual el juez determinó la no vinculación a proceso de la persona registrada en el RND"],
            ["¿El juez otorgó medida cautelar de prisión preventiva a la persona detenida?", "prision_preventiva", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí el juez otorgó o no la prisión preventiva como medida cautelar a la persona registrada en el RND"],
            ["Medida cautela distinta a la prisión preventiva", "medida_cautelar", "Texto", "Descripción de las medidas cautelares otorgadas por el juez a la persona registrada en el RND, distintas a la prisión preventiva "],
            ["¿Se aplicó algún Mecanismo Alternativo de Solución de Controversias (MASC)?", "MASC", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí se aplicó o no algún Mecanismo Alternativo de Solución de Controversias (MASC)"],
            ["¿La persona detenida tuvo un procedimiento abreviado?  ", "procedimiento_abreviado", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí la persona registrada en el RND tuvo o no un procedimiento abreviado como parte de su proceso"],
            ["¿Se inició proceso de juicio oral?", "inicia_juicio", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, responder sí el juicio oral se encuentra abierto o no; o bien se concluyó el proceso de juicio oral"],
            ["Motivo de la conclusión del proceso penal", "descripcion_conclusion", "Texto", "Conforme a las bases de datos internas de la Fiscalía/Procuraduría General de Justicia Estatal, describir la razón por la que se dio por concluido el proceso penal de la persona registrada en el RND. Ejemplo: MASC, Criterio de Oportunidad, Incompetencia, Sentencia, Suspención Condicional, Sobreseimiento."],
            ["Comentarios", "Comentarios", "Texto", "Comentarios adicionales o aclaraciones de la Fiscalía/Procuraduría General de Justicia Estatal a la búsqueda de la persona registrada en el RND en sus bases de datos internas."]
        ];
        diccionario.Cell(1,1).InsertData(definiciones);
        diccionario.Row(1).Style.Font.Bold = true;
        diccionario.Columns(1,3).Width = 32;
        diccionario.Column(4).Width = 90;
        diccionario.RangeUsed()!.Style.Alignment.WrapText = true;
        diccionario.SheetView.FreezeRows(1);
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
        public string? Coincidencias {get;set;}
        public string? Datos {get;set;}
    }
}
