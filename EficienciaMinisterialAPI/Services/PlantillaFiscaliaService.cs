using ClosedXML.Excel;
using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;

public sealed class PlantillaFiscaliaService(SqlConnectionFactory factory)
{
    public static bool TieneAcceso(UsuarioInfo usuario) => usuario.Rol == "SUPER_USUARIO"
        || (usuario.Rol == "FISCALIA" && (usuario.Ambito == "NACIONAL"
            || (usuario.Ambito == "ESTATAL" && usuario.Entidad is >= 1 and <= 32)));

    private static object Alcance(UsuarioInfo usuario, DateTime? inicio = null, DateTime? fin = null) => new
    {
        Nacional = usuario.Rol == "SUPER_USUARIO" || usuario.Ambito == "NACIONAL",
        usuario.Entidad, inicio = inicio?.Date, fin = fin?.Date
    };

    private const string Elegibles = """
        c.estado = 'CONFIRMADA' AND c.vigente = 1
        AND r.estado_cruce = 'LOCALIZADO'
        AND (@Nacional = 1 OR r.entidad = @Entidad)
        """;

    public async Task<object> CatalogosAsync(UsuarioInfo usuario)
    {
        if(!TieneAcceso(usuario)) throw new UnauthorizedAccessException();
        await using var db = await factory.AbrirAsync();
        var periodos = (await db.QueryAsync<PeriodoInfo>($"""
            SELECT DISTINCT c.periodo_inicio AS Inicio, c.periodo_fin AS Fin
            FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON r.id_carga = c.id
            WHERE {Elegibles} ORDER BY Inicio DESC;
            """, Alcance(usuario))).ToList();
        return new { periodos };
    }

    public async Task<(byte[]? Archivo, int Total)> DescargarAsync(UsuarioInfo usuario, DateTime inicio, DateTime fin)
    {
        var (filas, opciones) = await ObtenerDatosAsync(usuario, inicio, fin);
        if(filas.Count == 0) return (null, 0);
        var archivo = Crear(filas, opciones);
        await using var db = await factory.AbrirAsync();
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion) VALUES(@id, 'DESCARGAR_PLANTILLA_FISCALIA');", new { id = usuario.Id });
        return (archivo, filas.Count);
    }

    public async Task<(List<FilaInformePr> Filas, List<Opcion> Opciones)> ObtenerDatosAsync(UsuarioInfo usuario, DateTime inicio, DateTime fin)
    {
        if(!TieneAcceso(usuario)) throw new UnauthorizedAccessException();
        await using var db = await factory.AbrirAsync();
        var filas = (await db.QueryAsync<FilaInformePr>($"""
            SELECT c.periodo_inicio AS PeriodoInicio, c.periodo_fin AS PeriodoFin,
                c.folio AS FolioEntrega, i.nombre AS Corporacion, r.folio_rnd AS FolioRnd,
                r.fecha_detencion AS FechaDetencion, r.nombre_detenido AS NombreDetenido,
                e.nombre AS Entidad, r.autoridad_puesta AS AutoridadPuesta,
                CASE WHEN r.tipo_fuero = 'FF' THEN 'Federal' ELSE 'Común' END AS Fuero
            FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON r.id_carga = c.id
            JOIN catalogo.institucion i ON i.codigo = c.institucion
            JOIN catalogo.entidad e ON e.id = r.entidad
            WHERE {Elegibles} AND c.periodo_inicio = @inicio AND c.periodo_fin = @fin
            ORDER BY c.institucion, r.entidad, c.folio, r.fila;
            """, Alcance(usuario, inicio, fin), commandTimeout: 180)).ToList();
        if(filas.Count > XLHelper.MaxRowNumber - 2) throw new ArgumentException("Seleccione un periodo con menos registros para generar Excel.");
        var opciones = (await db.QueryAsync<Opcion>("""
            SELECT grupo AS Grupo, descripcion AS Descripcion FROM catalogo.opcion_ministerial
            WHERE activo = 1 AND (grupo <> 'DESCRIPCION_CONCLUSION' OR id BETWEEN 1 AND 13)
            ORDER BY grupo, id;
            """)).ToList();
        return (filas, opciones);
    }

    public sealed class Opcion
    {
        public string Grupo { get; set; } = "";
        public string Descripcion { get; set; } = "";
    }

    internal static readonly string[] Origen = ["INICIO ENTREGA", "FIN ENTREGA", "FOLIO ENTREGA", "CORPORACIÓN", "FOLIO RND INFORMADO", "FECHA DETENCIÓN", "NOMBRE INFORMADO", "ENTIDAD DETENCIÓN", "AUTORIDAD PUESTA", "FUERO"];
    internal static readonly string[] Respuesta = ["RECIBIDO POR FISCALÍA", "LIBERTAD MINISTERIAL", "MOTIVO LIBERTAD", "AUDIENCIA INICIAL", "DETENCIÓN LEGAL", "MOTIVO DETENCIÓN ILEGAL", "VINCULACIÓN A PROCESO", "MOTIVO NO VINCULACIÓN", "PRISIÓN PREVENTIVA", "MEDIDA CAUTELAR", "MASC", "PROCEDIMIENTO ABREVIADO", "INICIO DE JUICIO ORAL", "SITUACIÓN O CONCLUSIÓN DEL PROCESO", "COMENTARIOS"];
    internal static readonly int[] Binarias = [11, 12, 14, 15, 17, 19, 21, 22, 23];
    internal static readonly (string Grupo, int Columna)[] Grupos = [("MOTIVOS_LIBERTAD", 13), ("DETENCION_ILEGAL", 16), ("MOTIVO_NO_VINCULACION", 18), ("MEDIDA_CAUTELAR", 20), ("DESCRIPCION_CONCLUSION", 24)];

    public static byte[] Crear(IReadOnlyList<FilaInformePr> filas, IReadOnlyList<Opcion> opciones)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Seguimiento");
        hoja.Range(1, 1, 1, Origen.Length).Merge().Value = "DATOS REPORTADOS POR PRIMER RESPONDIENTE";
        hoja.Range(1, Origen.Length + 1, 1, Origen.Length + Respuesta.Length).Merge().Value = "SEGUIMIENTO DE FISCALÍA — CAMPOS PARA LLENAR";
        var encabezados = Origen.Concat(Respuesta).ToArray();
        for(var i = 0; i < encabezados.Length; i++) hoja.Cell(2, i + 1).Value = encabezados[i];
        var numero = 3;
        foreach(var r in filas)
        {
            hoja.Cell(numero, 1).Value = r.PeriodoInicio; hoja.Cell(numero, 2).Value = r.PeriodoFin;
            hoja.Cell(numero, 3).Value = r.FolioEntrega; hoja.Cell(numero, 4).Value = r.Corporacion;
            hoja.Cell(numero, 5).Value = r.FolioRnd; hoja.Cell(numero, 6).Value = r.FechaDetencion;
            hoja.Cell(numero, 7).Value = r.NombreDetenido; hoja.Cell(numero, 8).Value = r.Entidad;
            hoja.Cell(numero, 9).Value = r.AutoridadPuesta; hoja.Cell(numero, 10).Value = r.Fuero;
            numero++;
        }
        var ultima = Math.Max(3, numero - 1);
        hoja.Range(1, 1, 2, Origen.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#691c32");
        hoja.Range(1, 11, 2, encabezados.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#235b4e");
        hoja.Range(1, 1, 2, encabezados.Length).Style.Font.FontColor = XLColor.White;
        hoja.Range(1, 1, 2, encabezados.Length).Style.Font.Bold = true;
        hoja.Range(3, 11, ultima, encabezados.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#eaf3ec");
        hoja.Columns().Width = 25;
        hoja.Column(7).Width = 42; hoja.Column(9).Width = 45;
        hoja.RangeUsed()!.Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 30; hoja.Row(2).Height = 45;
        foreach(var columna in new[] { 1, 2, 6 }) hoja.Column(columna).Style.DateFormat.Format = "dd/MM/yyyy";
        hoja.Range(2, 1, ultima, encabezados.Length).SetAutoFilter();
        foreach(var columna in Binarias) hoja.Range(3, columna, ultima, columna).CreateDataValidation().List("\"Sí,No\"", true);
        var catalogos = libro.AddWorksheet("Catálogos");
        var col = 1;
        foreach(var (grupo, destino) in Grupos)
        {
            catalogos.Cell(1, col).Value = grupo;
            var lista = opciones.Where(o => o.Grupo == grupo).ToList();
            for(var j = 0; j < lista.Count; j++) catalogos.Cell(j + 2, col).Value = lista[j].Descripcion;
            if(lista.Count > 0)
            {
                var validacion = hoja.Range(3, destino, ultima, destino).CreateDataValidation();
                validacion.List(catalogos.Range(2, col, lista.Count + 1, col), true);
                if(destino == 20) validacion.ShowErrorMessage = false;
            }
            col++;
        }
        catalogos.Columns().Width = 65; catalogos.RangeUsed()!.Style.Alignment.WrapText = true;
        var guia = libro.AddWorksheet("Instrucciones");
        string[] instrucciones = ["Plantilla de seguimiento de fiscalía", "Complete únicamente los campos verdes de Seguimiento. Conserve los datos reportados y todas las filas, incluso folios repetidos.", "Las fechas deben capturarse como fechas de Excel: dd/mm/aaaa. Seleccione los motivos y medidas en las listas de Catálogos.", "La plantilla incluye sólo entregas confirmadas y vigentes del periodo seleccionado, localizadas en RND y dentro del alcance territorial de su usuario. No incluye faltas administrativas ni registros no localizados o pendientes.", "Los datos originales se conservan como fueron reportados; el folio informado puede estar vacío aunque la persona haya sido localizada por nombre y fecha.", "Responda Sí o No para cada detención. Si no se recibió, pase a comentarios. Si se recibió, complete las ocho preguntas binarias y los motivos aplicables. Para registrar varias medidas, separe las opciones con punto y coma. Guarde el Excel y seleccione Validar respuestas en el sistema."];
        for(var j = 0; j < instrucciones.Length; j++) guia.Cell(j + 1, 1).Value = instrucciones[j];
        guia.Column(1).Width = 110; guia.Column(1).Style.Alignment.WrapText = true;
        for(var j = 1; j <= instrucciones.Length; j++) guia.Row(j).Height = j == 1 ? 30 : 60;
        using var salida = new MemoryStream(); libro.SaveAs(salida); return salida.ToArray();
    }
}
