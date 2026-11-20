using ClosedXML.Excel;
using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;

public sealed class PlantillaFiscaliaService(SqlConnectionFactory factory, CruceRndService rnd)
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
        AND c.reabierta = 0 AND r.estado_cruce IN ('LOCALIZADO','PENDIENTE')
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
        var entregas = (await db.QueryAsync<Entrega>($"SELECT {CamposEntrega} FROM fiscalia.entrega WHERE vigente=1 AND reabierta=0 AND ((@Nacional=1 AND entidad IS NULL) OR (@Nacional=0 AND entidad=@Entidad)) ORDER BY inicio DESC", Alcance(usuario))).ToList();
        var bloqueadas = await db.QueryAsync<PeriodoInfo>("SELECT inicio, fin FROM fiscalia.entrega WHERE vigente=1 AND reabierta=0 AND ((@Nacional=1 AND entidad IS NULL) OR (@Nacional=0 AND entidad=@Entidad))", Alcance(usuario));
        var confirmados = bloqueadas.Select(p => (p.Inicio, p.Fin)).ToHashSet();
        periodos.RemoveAll(p => confirmados.Contains((p.Inicio, p.Fin)));
        return new { periodos, entregas };
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
        await using var tx = await db.BeginTransactionAsync();
        var datos = await LeerAsync(usuario, inicio, fin, db, tx);
        await tx.CommitAsync();
        return datos;
    }

    private async Task<(List<FilaInformePr> Filas, List<Opcion> Opciones)> LeerAsync(UsuarioInfo usuario, DateTime inicio, DateTime fin,
        Microsoft.Data.SqlClient.SqlConnection db, System.Data.Common.DbTransaction? tx, bool soloLocalizadas = false)
    {
        var filas = (await db.QueryAsync<FilaInformePr>($"""
            SELECT r.estado_cruce AS EstadoCruce, r.id AS IdRegistro, c.periodo_inicio AS PeriodoInicio, c.periodo_fin AS PeriodoFin,
                c.folio AS FolioEntrega, i.nombre AS Corporacion, r.folio_rnd AS FolioRnd,
                r.fecha_detencion AS FechaDetencion, r.nombre_detenido AS NombreDetenido,
                e.nombre AS Entidad, r.autoridad_puesta AS AutoridadPuesta,
                CASE WHEN r.tipo_fuero = 'FF' THEN 'Federal' ELSE 'Común' END AS Fuero
            FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON r.id_carga = c.id
            JOIN catalogo.institucion i ON i.codigo = c.institucion
            JOIN catalogo.entidad e ON e.id = r.entidad
            WHERE {Elegibles} {(soloLocalizadas ? "AND r.estado_cruce='LOCALIZADO'" : "")} AND c.periodo_inicio = @inicio AND c.periodo_fin = @fin
            ORDER BY c.institucion, r.entidad, c.folio, r.fila;
            """, Alcance(usuario, inicio, fin), transaction: tx, commandTimeout: 180)).ToList();
        var pendientes = filas.Where(f => f.EstadoCruce == "PENDIENTE").ToList();
        if (pendientes.Count > 0)
        {
            // La consulta RND usa el número de fila, el folio, el nombre y la fecha.
            var solicitudes = pendientes.Select((f, i) => new RegistroCarga(i + 1, f.Corporacion,
                f.FolioRnd, f.FechaDetencion, f.AutoridadPuesta, f.NombreDetenido, 0, f.Fuero)).ToList();
            var resultado = await rnd.CruzarAsync(solicitudes);
            var actualizaciones = pendientes.Select((f, i) =>
            {
                var normales = resultado.Filas.GetValueOrDefault(i + 1);
                var administrativas = resultado.Administrativas.GetValueOrDefault(i + 1);
                var estado = normales?.Count > 0 ? "LOCALIZADO" :
                    administrativas?.Count > 0 ? "FALTA_ADMINISTRATIVA" : "NO_LOCALIZADO";
                return new { f.IdRegistro, Estado = estado,
                    Tabla = administrativas?.Count > 0 ? resultado.TablaFa : resultado.Tabla,
                    Fecha = administrativas?.Count > 0 ? DateTime.ParseExact(resultado.TablaFa!.Substring(12, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) : resultado.FechaFuente,
                    Coincidencias = System.Text.Json.JsonSerializer.Serialize(normales ?? administrativas ?? []) };
            }).ToList();
            await db.ExecuteAsync("""
                UPDATE r SET estado_cruce=@Estado, rnd_tabla=@Tabla, rnd_fecha_fuente=@Fecha,
                    rnd_consultado_utc=SYSUTCDATETIME(), rnd_coincidencias=@Coincidencias
                FROM primer_respondiente.registro r JOIN primer_respondiente.carga c ON c.id=r.id_carga
                WHERE r.id=@IdRegistro AND r.estado_cruce='PENDIENTE' AND c.vigente=1 AND c.reabierta=0
                """, actualizaciones, tx);
            // Volver a leer el estado persistido, incluso ante descargas simultáneas.
            return await LeerAsync(usuario, inicio, fin, db, tx, soloLocalizadas: true);
        }
        if(filas.Count > XLHelper.MaxRowNumber - 2) throw new ArgumentException("Seleccione un periodo con menos registros para generar Excel.");
        var opciones = (await db.QueryAsync<Opcion>("""
            SELECT grupo AS Grupo, descripcion AS Descripcion FROM catalogo.opcion_ministerial
            WHERE activo = 1 AND (grupo <> 'DESCRIPCION_CONCLUSION' OR id BETWEEN 1 AND 13)
            ORDER BY grupo, id;
            """, transaction: tx)).ToList();
        return (filas, opciones);
    }

    public sealed record Entrega(long Id, DateTime Inicio, DateTime Fin, DateTime Fecha, int Recibidos, int NoRecibidos, byte? Entidad)
    {
        public string Folio => $"EM-FIS-{Inicio:yyyy}-{Id}";
    }

    private const string CamposEntrega = "id, inicio, fin, fecha, recibidos, no_recibidos AS NoRecibidos, entidad";

    public async Task<Entrega?> EntregaPeriodoAsync(UsuarioInfo usuario, DateTime inicio, DateTime fin)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<Entrega>($"SELECT {CamposEntrega} FROM fiscalia.entrega WHERE vigente=1 AND reabierta=0 AND inicio=@inicio AND fin=@fin AND ((@Nacional=1 AND entidad IS NULL) OR (@Nacional=0 AND entidad=@Entidad))",
            Alcance(usuario, inicio, fin));
    }

    public async Task<Entrega?> AcuseAsync(UsuarioInfo usuario, long id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<Entrega>($"SELECT {CamposEntrega} FROM fiscalia.entrega WHERE id=@id AND vigente=1 AND reabierta=0 AND (@nacional=1 OR entidad=@entidad)",
            new { id, nacional = usuario.Rol == "SUPER_USUARIO" || usuario.Ambito == "NACIONAL", usuario.Entidad });
    }

    public async Task<(Entrega? Entrega, ValidacionFiscalia? Revision)> ConfirmarAsync(UsuarioInfo usuario, DateTime inicio, DateTime fin, XLWorkbook libro)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        byte? entidad = usuario.Rol == "SUPER_USUARIO" || usuario.Ambito == "NACIONAL" ? null : usuario.Entidad;
        var parametros = new { inicio = inicio.Date, fin = fin.Date, entidad };
        var previa = await db.QuerySingleOrDefaultAsync<Entrega>($"SELECT {CamposEntrega} FROM fiscalia.entrega WITH (UPDLOCK,HOLDLOCK) WHERE vigente=1 AND inicio=@inicio AND fin=@fin AND (entidad=@entidad OR (entidad IS NULL AND @entidad IS NULL))", parametros, tx);
        if(previa != null && !await db.ExecuteScalarAsync<bool>("SELECT reabierta FROM fiscalia.entrega WHERE id=@Id", previa, tx)) return (null, null);
        var (filas, opciones) = await LeerAsync(usuario, inicio, fin, db, tx);
        var revision = ValidacionFiscalia.Revisar(libro, filas, opciones);
        if(filas.Count == 0 || revision.Errores.Count != 0) return (null, revision);
        var json = System.Text.Json.JsonSerializer.Serialize(revision.Respuestas);
        var huella = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        if(previa != null)
        {
            var anterior = await db.QuerySingleAsync<byte[]>("SELECT huella FROM fiscalia.entrega WHERE id=@Id", previa, tx);
            if(anterior.SequenceEqual(huella))
            {
                revision.Errores.Add(new(null, "Entrega", "Las respuestas son idénticas a la entrega vigente. Cargue la corrección."));
                return (null, revision);
            }
            await db.ExecuteAsync("UPDATE fiscalia.entrega SET vigente=0,reabierta=0 WHERE id=@Id", previa, tx);
        }
        var entrega = await db.QuerySingleAsync<Entrega>($"""
            INSERT fiscalia.entrega(inicio,fin,entidad,usuario,recibidos,no_recibidos,huella)
            VALUES(@inicio,@fin,@entidad,@usuario,@recibidos,@noRecibidos,@huella);
            SELECT {CamposEntrega} FROM fiscalia.entrega WHERE id=SCOPE_IDENTITY();
            """, new { parametros.inicio, parametros.fin, entidad, usuario = usuario.Id, revision.Recibidos, revision.NoRecibidos, huella }, tx);
        using var tabla = new System.Data.DataTable();
        tabla.Columns.Add("entrega", typeof(long)); tabla.Columns.Add("registro", typeof(long)); tabla.Columns.Add("datos", typeof(string));
        foreach(var (registro, datos) in revision.Respuestas) tabla.Rows.Add(entrega.Id, registro, System.Text.Json.JsonSerializer.Serialize(datos));
        using var copia = new Microsoft.Data.SqlClient.SqlBulkCopy(db, Microsoft.Data.SqlClient.SqlBulkCopyOptions.CheckConstraints, (Microsoft.Data.SqlClient.SqlTransaction)tx);
        copia.DestinationTableName = "fiscalia.respuesta";
        foreach(System.Data.DataColumn col in tabla.Columns) copia.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await copia.WriteToServerAsync(tabla);
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario,accion) VALUES(@Id,'CONFIRMAR_ENTREGA_FISCALIA')", new { usuario.Id }, tx);
        await tx.CommitAsync();
        return (entrega, null);
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
        var captura = hoja.Range(3, 11, ultima, encabezados.Length);
        captura.Style.Fill.BackgroundColor = XLColor.FromHtml("#eaf3ec");
        var colorLinea = XLColor.FromHtml("#abc1b4");
        captura.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        captura.Style.Border.InsideBorderColor = colorLinea;
        captura.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        captura.Style.Border.OutsideBorderColor = colorLinea;
        hoja.Columns().Width = 25;
        hoja.Column(7).Width = 42; hoja.Column(9).Width = 45;
        hoja.RangeUsed()!.Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 30; hoja.Row(2).Height = 45;
        foreach(var columna in new[] { 1, 2, 6 }) hoja.Column(columna).Style.DateFormat.Format = "dd/MM/yyyy";
        hoja.Range(2, 1, ultima, encabezados.Length).SetAutoFilter();
        foreach(var columna in Binarias)
        {
            var validacion = hoja.Range(3, columna, ultima, columna).CreateDataValidation();
            validacion.List("\"Sí,No\"", true);
            validacion.ShowErrorMessage = false;
        }
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
                validacion.ShowErrorMessage = false;
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
