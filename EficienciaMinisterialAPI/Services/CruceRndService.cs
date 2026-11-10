using System.Globalization;
using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;

public sealed class CruceRndService(IConfiguration configuration, ILogger<CruceRndService> logger)
{
    public static string? UltimaTabla(IEnumerable<string> tablas, DateTime hoy, bool fa = false)
    {
        var sufijo = fa ? "_fa" : "";
        return tablas.Where(t => t.Length == 20 + sufijo.Length && t.StartsWith("tablero_rnd_", StringComparison.Ordinal)
            && t.EndsWith(sufijo, StringComparison.Ordinal)
            && t.Substring(12, 8).All(char.IsAsciiDigit)
            && DateTime.TryParseExact(t.Substring(12, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            && fecha <= hoy.Date).OrderDescending(StringComparer.Ordinal).FirstOrDefault();
    }

    public async Task<ResultadoCruceRnd> CruzarAsync(IReadOnlyList<RegistroCarga> registros)
    {
        var normal = await CruzarSerieAsync(registros, false);
        var pendientes = registros.Where(r => !normal.Filas.ContainsKey(r.Fila)).ToList();
        if(pendientes.Count == 0) return normal;
        var administrativas = await CruzarSerieAsync(pendientes, true);
        return normal with { Administrativas = administrativas.Filas, TablaFa = administrativas.Tabla };
    }

    private async Task<ResultadoCruceRnd> CruzarSerieAsync(IReadOnlyList<RegistroCarga> registros, bool fa)
    {
        var reloj = Stopwatch.StartNew();
        logger.LogInformation("Inicio cruce {Serie}: {Filas} filas", fa ? "RND_FA" : "RND", registros.Count);
        var cadena = configuration.GetConnectionString("RndDestino");
        if(string.IsNullOrWhiteSpace(cadena))
        {
            throw new InvalidOperationException("Configure ConnectionStrings__RndDestino para consultar las cargas diarias de RND.");
        }
        if(!new SqlConnectionStringBuilder(cadena).InitialCatalog.Equals("RND", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RndDestino debe apuntar a la base RND del destino DBBridge.");
        }
        await using var db = new SqlConnection(cadena);
        await db.OpenAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        // Mismo recurso y base de DBBridge: una carga no se consulta antes de su commit.
        var bloqueo = await db.ExecuteScalarAsync<int>("""
            DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource=@recurso, @LockMode='Shared',
                @LockOwner='Transaction', @LockTimeout=30000;
            SELECT @r;
            """, new { recurso = fa ? "DBBridge_RND_FA" : "DBBridge_RND" }, transaction: tx, commandTimeout: 40);
        logger.LogInformation("Conexión y bloqueo {Serie}: {Ms} ms", fa ? "RND_FA" : "RND", reloj.ElapsedMilliseconds);
        if(bloqueo < 0) throw new InvalidOperationException("La carga diaria de RND está en proceso. Intente nuevamente.");
        var tablas = await db.QueryAsync<string>("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') AND name LIKE 'tablero[_]rnd[_]%'", transaction: tx);
        var hoy = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Mexico_City").Date;
        var tabla = UltimaTabla(tablas, hoy, fa) ?? throw new InvalidOperationException("No existe una carga diaria válida para la serie RND solicitada en el destino.");
        var columnas = (await db.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@objeto)", new { objeto = "dbo." + tabla }, tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] requeridas = ["FOLIO_DETENIDO", "ID_DETENIDO", "ID_DETENCION", "ID_PUESTA_DISPOSICION", "NOMBRE_PR", "APELLIDO_PATERNO_PR", "APELLIDO_MATERNO_PR", "NOMBRE_MP", "APELLIDO_PATERNO_MP", "APELLIDO_MATERNO_MP", "FECHA_DETENCION", "ENTIDAD_DETENCION", "NOMBRE_FISCALIA", "NOMBRE_FUERO"];
        // FA conserva nombres históricos en algunas tablas: DBBridge copia por posición.
        var equivalencias = new Dictionary<string, string>
        {
            ["NOMBRE_PR"] = "NOMBRE", ["APELLIDO_PATERNO_PR"] = "APELLIDO_PATERNO",
            ["APELLIDO_MATERNO_PR"] = "APELLIDO_MATERNO", ["ENTIDAD_DETENCION"] = "ENTIDAD"
        };
        var campos = new List<string>();
        foreach(var requerida in requeridas)
        {
            var origen = columnas.Contains(requerida) ? requerida
                : equivalencias.TryGetValue(requerida, out var anterior) && columnas.Contains(anterior) ? anterior : null;
            if(origen == null)
            {
                throw new InvalidOperationException($"La tabla {tabla} no tiene la columna requerida {requerida}.");
            }
            campos.Add($"[{origen}] AS [{requerida}]");
        }
        var fuenteSql = $"SELECT {string.Join(", ", campos)} FROM [dbo].[{tabla}]";
        var filtroAdministrativas = fa ? "1 = 1" : "r.FOLIO_DETENIDO NOT LIKE '%/IA/%' AND r.FOLIO_DETENIDO NOT LIKE '%/AI/%'";
        // #solicitud vive sólo en tempdb para esta conexión; no escribe tablas de RND.
        await db.ExecuteAsync("CREATE TABLE #solicitud(fila int PRIMARY KEY, folio nvarchar(40), nombre nvarchar(300), fecha date);", transaction: tx);
        using var solicitudes = new DataTable();
        solicitudes.Columns.Add("fila", typeof(int));
        solicitudes.Columns.Add("folio", typeof(string));
        solicitudes.Columns.Add("nombre", typeof(string));
        solicitudes.Columns.Add("fecha", typeof(DateTime));
        foreach(var registro in registros)
        {
            solicitudes.Rows.Add(registro.Fila, ExcelCargaService.Normalizar(registro.FolioRnd),
                ExcelCargaService.Normalizar(registro.NombreDetenido), registro.FechaDetencion.Date);
        }
        using(var copia = new SqlBulkCopy(db, SqlBulkCopyOptions.Default, tx))
        {
            copia.DestinationTableName = "#solicitud";
            foreach(DataColumn columna in solicitudes.Columns)
            {
                copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
            }
            await copia.WriteToServerAsync(solicitudes);
        }
        await db.ExecuteAsync("""
            CREATE INDEX IX_solicitud_folio ON #solicitud(folio);
            CREATE INDEX IX_solicitud_nombre ON #solicitud(fecha, nombre);
            """, transaction: tx);
        var nombrePrSql = "TRIM(CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_PR), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_PR), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_PR), '')))";
        nombrePrSql = $"REPLACE(REPLACE(REPLACE({nombrePrSql}, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' ')";
        for(var paso = 0; paso < 9; paso++)
        {
            nombrePrSql = $"REPLACE({nombrePrSql}, '  ', ' ')";
        }
        // La fuente procede de metadatos y nombres estrictos. Sólo se crean objetos locales en tempdb.
        var proyeccion = """
            SELECT DISTINCT s.fila, CONVERT(nvarchar(40), r.FOLIO_DETENIDO) AS Folio,
                CONVERT(nvarchar(80), r.ID_DETENIDO) AS IdDetenido,
                CONVERT(nvarchar(80), r.ID_DETENCION) AS IdDetencion,
                CONVERT(nvarchar(80), r.ID_PUESTA_DISPOSICION) AS IdPuesta,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_PR), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_PR), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_PR), '')) AS NombrePr,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_MP), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_MP), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_MP), '')) AS NombreMp,
                TRY_CONVERT(datetime2, r.FECHA_DETENCION) AS FechaDetencion,
                COALESCE(r.ENTIDAD_DETENCION, '') AS Entidad,
                COALESCE(r.NOMBRE_FISCALIA, '') AS Fiscalia, COALESCE(r.NOMBRE_FUERO, '') AS Fuero,
                CAST(@porFolio AS bit) AS PorFolio
            """;
        var preparacionMs = reloj.ElapsedMilliseconds;
        logger.LogInformation("Cruce {Tabla}: preparación {Ms} ms; iniciando búsqueda por folio", tabla, preparacionMs);
        // El hash join evita recorrer la tabla diaria una vez por cada fila del Excel.
        var candidatos = (await db.QueryAsync<FilaRnd>($"""
            {proyeccion}
            INTO #coincidencia
            FROM ({fuenteSql}) r
            INNER HASH JOIN #solicitud s ON s.folio <> ''
                AND TRIM(r.FOLIO_DETENIDO) COLLATE Latin1_General_100_CI_AI = s.folio
            WHERE {filtroAdministrativas}
            OPTION(RECOMPILE);
            DELETE s FROM #solicitud s WHERE EXISTS(SELECT 1 FROM #coincidencia c WHERE c.fila = s.fila);
            SELECT * FROM #coincidencia;
            DROP TABLE #coincidencia;
            """, new { porFolio = true }, tx, commandTimeout: 180)).ToList();
        var folioMs = reloj.ElapsedMilliseconds;
        var pendientes = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM #solicitud WHERE nombre <> ''", transaction: tx);
        logger.LogInformation("Cruce {Tabla}: folios {Ms} ms; filas encontradas {Encontradas}; pendientes con nombre {Pendientes}",
            tabla, folioMs - preparacionMs, candidatos.Select(c => c.Fila).Distinct().Count(), pendientes);
        if(pendientes > 0)
        {
            // Acotar por fechas originales, sin transformar la columna fuente en el filtro de rango.
            // Las fechas anteriores a la quincena siguen siendo válidas.
            var rango = await db.QuerySingleAsync<RangoFechas>("""
                SELECT MIN(fecha) AS Desde, DATEADD(day, 1, MAX(fecha)) AS Hasta
                FROM #solicitud WHERE nombre <> '';
                """, transaction: tx);
            var porNombre = await db.QueryAsync<FilaRnd>($"""
                {proyeccion}
                FROM ({fuenteSql}) r
                INNER HASH JOIN #solicitud s ON s.nombre <> ''
                    AND TRY_CONVERT(date, r.FECHA_DETENCION) = s.fecha
                    AND {nombrePrSql} COLLATE Latin1_General_100_CI_AI = s.nombre
                WHERE {filtroAdministrativas}
                    AND r.FECHA_DETENCION >= @Desde AND r.FECHA_DETENCION < @Hasta
                OPTION(RECOMPILE);
                """, new { porFolio = false, rango.Desde, rango.Hasta }, tx, commandTimeout: 180);
            candidatos.AddRange(porNombre);
        }
        logger.LogInformation("Cruce {Tabla}: nombres {Ms} ms; total {TotalMs} ms", tabla, reloj.ElapsedMilliseconds - folioMs, reloj.ElapsedMilliseconds);
        await db.ExecuteAsync("DROP TABLE #solicitud;", transaction: tx);
        var filas = candidatos.GroupBy(c => c.Fila).ToDictionary(g => g.Key,
            g => g.Select(c => new CoincidenciaRnd(c.Folio, c.IdDetenido, c.IdDetencion, c.IdPuesta,
                c.NombrePr, c.NombreMp, c.FechaDetencion, c.Entidad, c.Fiscalia, c.Fuero)).Distinct().ToList());
        await tx.CommitAsync();
        logger.LogInformation("Cruce RND con {Tabla}: filas consultadas {Filas}; localizadas {Localizadas}", tabla, registros.Count, filas.Count);
        return new ResultadoCruceRnd(tabla, DateTime.ParseExact(tabla.Substring(12, 8), "yyyyMMdd", CultureInfo.InvariantCulture), filas);
    }

    private sealed class RangoFechas
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
    }

    private sealed class FilaRnd
    {
        public int Fila { get; set; }
        public bool PorFolio { get; set; }
        public string Folio { get; set; } = "";
        public string IdDetenido { get; set; } = "";
        public string IdDetencion { get; set; } = "";
        public string? IdPuesta { get; set; }
        public string NombrePr { get; set; } = "";
        public string NombreMp { get; set; } = "";
        public DateTime? FechaDetencion { get; set; }
        public string Entidad { get; set; } = "";
        public string Fiscalia { get; set; } = "";
        public string Fuero { get; set; } = "";
    }
}
