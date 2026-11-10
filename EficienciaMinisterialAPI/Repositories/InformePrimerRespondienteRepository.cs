using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;

public sealed class InformePrimerRespondienteRepository(SqlConnectionFactory factory)
{
    // Todos los filtros se intersectan con el acceso activo recuperado desde BD, incluso en Excel.
    private const string Alcance = """
        c.estado = 'CONFIRMADA' AND c.vigente = 1
        AND (@EsSuper = 1 OR (
            (@InstitucionAcceso IS NULL OR c.institucion = @InstitucionAcceso)
            AND (@Rol <> 'PRIMER_RESPONDIENTE' OR @InstitucionAcceso IS NOT NULL)
            AND (@Ambito = 'NACIONAL' OR (@Ambito = 'ESTATAL' AND r.entidad = @EntidadAcceso))))
        """;

    public async Task<object> CatalogosAsync(UsuarioInfo usuario)
    {
        await using var db = await factory.AbrirAsync();
        using var datos = await db.QueryMultipleAsync($"""
            SELECT DISTINCT c.periodo_inicio AS Inicio, c.periodo_fin AS Fin
            INTO #periodos FROM primer_respondiente.carga c
            JOIN primer_respondiente.registro r ON r.id_carga = c.id WHERE {Alcance};
            SELECT Inicio, Fin FROM #periodos ORDER BY Inicio DESC;
            SELECT DISTINCT c.institucion AS codigo, i.nombre AS nombre
            FROM primer_respondiente.carga c JOIN primer_respondiente.registro r ON r.id_carga = c.id
            JOIN catalogo.institucion i ON i.codigo = c.institucion WHERE {Alcance} ORDER BY nombre;
            SELECT DISTINCT e.id, e.nombre FROM primer_respondiente.carga c
            JOIN primer_respondiente.registro r ON r.id_carga = c.id JOIN catalogo.entidad e ON e.id = r.entidad
            WHERE {Alcance} ORDER BY e.nombre;
            DROP TABLE #periodos;
            """, Parametros(usuario, new()));
        var periodos = (await datos.ReadAsync<PeriodoInfo>()).ToList();
        var corporaciones = (await datos.ReadAsync()).ToList();
        var entidades = (await datos.ReadAsync()).ToList();
        return new { periodos, corporaciones, entidades, acceso = new {
            puedeElegirCorporacion = usuario.Rol == "SUPER_USUARIO" || (usuario.Rol == "CONSULTA" && usuario.Institucion == null),
            puedeElegirEntidad = usuario.Rol == "SUPER_USUARIO" || usuario.Ambito == "NACIONAL",
            corporacion = usuario.Institucion, entidad = usuario.Entidad
        } };
    }

    private static object Parametros(UsuarioInfo usuario, FiltrosInformePr filtros) => new
    {
        EsSuper = usuario.Rol == "SUPER_USUARIO", Rol = usuario.Rol,
        InstitucionAcceso = usuario.Institucion, usuario.Ambito, EntidadAcceso = usuario.Entidad,
        filtros.Anio, Inicio = filtros.Inicio?.Date, Fin = filtros.Fin?.Date, filtros.Corporacion, filtros.Entidad,
        Offset = (filtros.Pagina - 1) * filtros.Tamano, filtros.Tamano, UsuarioId = usuario.Id
    };

    private const string Agregados = """
        COUNT(DISTINCT FolioEntrega) AS Entregas, COUNT(*) AS Registros,
        COUNT(DISTINCT NULLIF(FolioRnd, '')) AS Folios,
        COALESCE(SUM(CASE WHEN EstadoCruce = 'LOCALIZADO' THEN 1 ELSE 0 END), 0) AS Localizados,
        COALESCE(SUM(CASE WHEN EstadoCruce = 'NO_LOCALIZADO' THEN 1 ELSE 0 END), 0) AS NoLocalizados,
        COALESCE(SUM(CASE WHEN EstadoCruce NOT IN ('LOCALIZADO', 'NO_LOCALIZADO') THEN 1 ELSE 0 END), 0) AS Pendientes,
        COALESCE(SUM(CASE WHEN Duplicado = 1 THEN 1 ELSE 0 END), 0) AS Duplicados
        """;

    public async Task<InformePr> ConsultarAsync(UsuarioInfo usuario, FiltrosInformePr filtros, bool exportar = false)
    {
        filtros.Validar();
        await using var db = await factory.AbrirAsync();
        // Materializar sólo en tempdb da una misma base a resumen, conteos y página.
        using var datos = await db.QueryMultipleAsync($"""
            SELECT c.periodo_inicio AS PeriodoInicio, c.periodo_fin AS PeriodoFin, c.folio AS FolioEntrega,
                i.nombre AS Corporacion, r.folio_rnd AS FolioRnd, r.fecha_detencion AS FechaDetencion,
                r.nombre_detenido AS NombreDetenido, e.nombre AS Entidad, r.autoridad_puesta AS AutoridadPuesta,
                COALESCE(f.nombre, r.tipo_fuero, '') AS Fuero, r.estado_cruce AS EstadoCruce,
                r.duplicado AS Duplicado, r.rnd_fecha_fuente AS FechaFuente, c.id AS OrdenCarga, r.fila AS OrdenFila
            INTO #informe
            FROM primer_respondiente.carga c
            JOIN primer_respondiente.registro r ON r.id_carga = c.id
            JOIN catalogo.institucion i ON i.codigo = c.institucion
            JOIN catalogo.entidad e ON e.id = r.entidad
            LEFT JOIN catalogo.fuero f ON f.codigo = r.tipo_fuero
            WHERE {Alcance}
                AND (@Anio IS NULL OR (c.periodo_inicio >= DATEFROMPARTS(@Anio, 1, 1) AND c.periodo_inicio < DATEFROMPARTS(@Anio + 1, 1, 1)))
                AND (@Inicio IS NULL OR (c.periodo_inicio = @Inicio AND c.periodo_fin = @Fin))
                AND (@Corporacion IS NULL OR c.institucion = @Corporacion)
                AND (@Entidad IS NULL OR r.entidad = @Entidad);
            SELECT {Agregados} FROM #informe;
            SELECT PeriodoInicio, PeriodoFin, Corporacion, {Agregados}
            FROM #informe GROUP BY PeriodoInicio, PeriodoFin, Corporacion ORDER BY {filtros.OrdenResumen} {(filtros.DescendenteResumen ? "DESC" : "ASC")}{(filtros.OrdenResumen == "PeriodoInicio" ? ", Corporacion" : filtros.OrdenResumen == "Corporacion" ? ", PeriodoInicio" : ", PeriodoInicio, Corporacion")};
            SELECT * FROM #informe ORDER BY {filtros.Orden} {(filtros.Descendente ? "DESC" : "ASC")}, OrdenCarga, OrdenFila
                OFFSET {(exportar ? "0" : "@Offset")} ROWS FETCH NEXT {(exportar ? "1048575" : "@Tamano")} ROWS ONLY;
            DROP TABLE #informe;
            INSERT seguridad.bitacora(id_usuario, accion) VALUES(@UsuarioId, '{(exportar ? "DESCARGAR_REPORTE_PR" : "CONSULTAR_REPORTE_PR")}');
            """, Parametros(usuario, filtros), commandTimeout: 180);
        var totales = await datos.ReadSingleAsync<TotalesInformePr>();
        var resumen = (await datos.ReadAsync<ResumenInformePr>()).ToList();
        var registros = (await datos.ReadAsync<FilaInformePr>()).ToList();
        return new(totales, resumen, registros, filtros.Pagina, filtros.Tamano);
    }
}
