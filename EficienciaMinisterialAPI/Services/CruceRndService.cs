using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;

public sealed class CruceRndService(IConfiguration configuration, ILogger<CruceRndService> logger)
{
    public static string? UltimaTabla(IEnumerable<string> tablas, DateTime hoy)
    {
        return tablas.Where(t => t.Length == 20 && t.StartsWith("tablero_rnd_", StringComparison.Ordinal)
            && t[12..].All(char.IsAsciiDigit)
            && DateTime.TryParseExact(t[12..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            && fecha <= hoy.Date).OrderDescending(StringComparer.Ordinal).FirstOrDefault();
    }

    public async Task<ResultadoCruceRnd> CruzarAsync(IReadOnlyList<RegistroCarga> registros)
    {
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
            EXEC @r = sys.sp_getapplock @Resource='DBBridge_RND', @LockMode='Shared',
                @LockOwner='Transaction', @LockTimeout=30000;
            SELECT @r;
            """, transaction: tx, commandTimeout: 40);
        if(bloqueo < 0) throw new InvalidOperationException("La carga diaria de RND está en proceso. Intente nuevamente.");
        var tablas = await db.QueryAsync<string>("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') AND name LIKE 'tablero[_]rnd[_]%'", transaction: tx);
        var hoy = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Mexico_City").Date;
        var tabla = UltimaTabla(tablas, hoy) ?? throw new InvalidOperationException("No existe una carga diaria RND válida en el destino.");
        var columnas = (await db.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@objeto)", new { objeto = "dbo." + tabla }, tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] requeridas = ["FOLIO_DETENIDO", "ID_DETENIDO", "ID_DETENCION", "ID_PUESTA_DISPOSICION", "NOMBRE_PR", "APELLIDO_PATERNO_PR", "APELLIDO_MATERNO_PR", "NOMBRE_MP", "APELLIDO_PATERNO_MP", "APELLIDO_MATERNO_MP", "FECHA_DETENCION", "ENTIDAD_DETENCION", "NOMBRE_FISCALIA", "NOMBRE_FUERO"];
        if(requeridas.Any(c => !columnas.Contains(c)))
        {
            throw new InvalidOperationException("La tabla diaria más reciente no tiene la estructura RND vigente de DBBridge.");
        }
        var solicitudes = JsonSerializer.Serialize(registros.Select(r => new { fila = r.Fila, folio = ExcelCargaService.Normalizar(r.FolioRnd), nombre = ExcelCargaService.Normalizar(r.NombreDetenido), fecha = r.FechaDetencion.Date }));
        var nombrePrSql = "TRIM(CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_PR), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_PR), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_PR), '')))";
        nombrePrSql = $"REPLACE(REPLACE(REPLACE({nombrePrSql}, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' ')";
        for(var paso = 0; paso < 9; paso++)
        {
            nombrePrSql = $"REPLACE({nombrePrSql}, '  ', ' ')";
        }
        // El identificador interpolado procede exclusivamente de sys.tables y del formato estricto validado arriba.
        var candidatos = await db.QueryAsync<FilaRnd>($"""
            CREATE TABLE #solicitud(fila int PRIMARY KEY, folio nvarchar(40), nombre nvarchar(300), fecha date);
            INSERT #solicitud SELECT fila, folio, nombre, fecha FROM OPENJSON(@solicitudes)
            WITH(fila int, folio nvarchar(40), nombre nvarchar(300), fecha date);
            SELECT DISTINCT s.fila, CONVERT(nvarchar(40), r.FOLIO_DETENIDO) AS Folio,
                CONVERT(nvarchar(80), r.ID_DETENIDO) AS IdDetenido,
                CONVERT(nvarchar(80), r.ID_DETENCION) AS IdDetencion,
                CONVERT(nvarchar(80), r.ID_PUESTA_DISPOSICION) AS IdPuesta,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_PR), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_PR), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_PR), '')) AS NombrePr,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_MP), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_MP), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_MP), '')) AS NombreMp,
                TRY_CONVERT(datetime2, r.FECHA_DETENCION) AS FechaDetencion,
                COALESCE(r.ENTIDAD_DETENCION, '') AS Entidad,
                COALESCE(r.NOMBRE_FISCALIA, '') AS Fiscalia, COALESCE(r.NOMBRE_FUERO, '') AS Fuero,
                CAST(CASE WHEN s.folio <> '' AND TRIM(r.FOLIO_DETENIDO) COLLATE Latin1_General_100_CI_AI = s.folio THEN 1 ELSE 0 END AS bit) AS PorFolio
            FROM [dbo].[{tabla}] r
            JOIN #solicitud s ON s.folio <> '' AND TRIM(r.FOLIO_DETENIDO) COLLATE Latin1_General_100_CI_AI = s.folio
            WHERE r.FOLIO_DETENIDO NOT LIKE '%/IA/%' AND r.FOLIO_DETENIDO NOT LIKE '%/AI/%'
            UNION ALL
            SELECT DISTINCT s.fila, CONVERT(nvarchar(40), r.FOLIO_DETENIDO) AS Folio,
                CONVERT(nvarchar(80), r.ID_DETENIDO) AS IdDetenido,
                CONVERT(nvarchar(80), r.ID_DETENCION) AS IdDetencion,
                CONVERT(nvarchar(80), r.ID_PUESTA_DISPOSICION) AS IdPuesta,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_PR), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_PR), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_PR), '')) AS NombrePr,
                CONCAT_WS(' ', NULLIF(TRIM(r.NOMBRE_MP), ''), NULLIF(TRIM(r.APELLIDO_PATERNO_MP), ''), NULLIF(TRIM(r.APELLIDO_MATERNO_MP), '')) AS NombreMp,
                TRY_CONVERT(datetime2, r.FECHA_DETENCION) AS FechaDetencion,
                COALESCE(r.ENTIDAD_DETENCION, '') AS Entidad,
                COALESCE(r.NOMBRE_FISCALIA, '') AS Fiscalia, COALESCE(r.NOMBRE_FUERO, '') AS Fuero,
                CAST(CASE WHEN s.folio <> '' AND TRIM(r.FOLIO_DETENIDO) COLLATE Latin1_General_100_CI_AI = s.folio THEN 1 ELSE 0 END AS bit) AS PorFolio
            FROM [dbo].[{tabla}] r
            JOIN #solicitud s ON s.nombre <> '' AND TRY_CONVERT(date, r.FECHA_DETENCION) = s.fecha
                AND {nombrePrSql} COLLATE Latin1_General_100_CI_AI = s.nombre
            WHERE r.FOLIO_DETENIDO NOT LIKE '%/IA/%' AND r.FOLIO_DETENIDO NOT LIKE '%/AI/%';
            DROP TABLE #solicitud;
            """, new { solicitudes }, tx, commandTimeout: 180);
        var filas = candidatos.GroupBy(c => c.Fila).ToDictionary(g => g.Key,
            g => g.Where(c => !g.Any(x => x.PorFolio) || c.PorFolio).Select(c => new CoincidenciaRnd(c.Folio, c.IdDetenido, c.IdDetencion, c.IdPuesta,
                c.NombrePr, c.NombreMp, c.FechaDetencion, c.Entidad, c.Fiscalia, c.Fuero)).Distinct().ToList());
        await tx.CommitAsync();
        logger.LogInformation("Cruce RND con {Tabla}: filas consultadas {Filas}; localizadas {Localizadas}", tabla, registros.Count, filas.Count);
        return new ResultadoCruceRnd(tabla, DateTime.ParseExact(tabla[12..], "yyyyMMdd", CultureInfo.InvariantCulture), filas);
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
