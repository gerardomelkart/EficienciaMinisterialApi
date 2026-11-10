using Dapper;
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CargaRepository(SqlConnectionFactory factory, ILogger<CargaRepository> logger)
{
    private const string Columnas = "id, folio, institucion, nombre_archivo AS NombreArchivo, periodo_inicio AS PeriodoInicio, periodo_fin AS PeriodoFin, estado, total_registros AS TotalRegistros, creado_utc AS CreadoUtc, confirmado_utc AS ConfirmadoUtc";

    public async Task<Guid> GuardarAsync(UsuarioInfo usuario, string nombre, string hash, int idPeriodo, List<RegistroCarga> registros, ResultadoCruceRnd? cruce = null)
    {
        var reloj = Stopwatch.StartNew();
        await using var db = await factory.AbrirAsync();
        var conexionMs = reloj.ElapsedMilliseconds;
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var periodo = await db.QuerySingleOrDefaultAsync<PeriodoInfo>("""
            SELECT id, inicio, fin FROM primer_respondiente.periodo WITH(HOLDLOCK)
            WHERE id = @idPeriodo AND habilitado = 1 AND cerrado = 0
            """, new { idPeriodo }, tx);
        if(periodo == null)
        {
            throw new ArgumentException("El periodo no existe, está deshabilitado o ya fue cerrado. Actualice la lista.");
        }
        if(usuario.Rol != "SUPER_USUARIO" && await TieneEntregaAsync(db, tx, usuario.Institucion, periodo))
        {
            throw new ArgumentException("La corporación ya tiene una entrega confirmada para esta quincena. Actualice los periodos disponibles.");
        }
        var id = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT primer_respondiente.carga(id, id_usuario, institucion, nombre_archivo, sha256, id_periodo, periodo_inicio, periodo_fin, estado, total_registros)
            VALUES(@id, @UsuarioId, @Institucion, @nombre, @hash, @idPeriodo, @inicio, @fin, 'VALIDADA', @total)
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, nombre, hash, idPeriodo, inicio = periodo.Inicio, fin = periodo.Fin, total = registros.Count }, tx);
        var preparacionMs = reloj.ElapsedMilliseconds - conexionMs;
        using var datos = PrepararRegistros(id, registros);
        if(cruce != null)
        {
            datos.Columns.Add("rnd_tabla", typeof(string));
            datos.Columns.Add("rnd_fecha_fuente", typeof(DateTime));
            datos.Columns.Add("rnd_consultado_utc", typeof(DateTime));
            datos.Columns.Add("rnd_coincidencias", typeof(string));
            datos.Columns.Add("estado_cruce", typeof(string));
            foreach(DataRow fila in datos.Rows)
            {
                var coincidencias = cruce.Filas.GetValueOrDefault((int)fila["fila"]) ?? [];
                fila["rnd_tabla"] = cruce.Tabla;
                fila["rnd_fecha_fuente"] = cruce.FechaFuente;
                fila["rnd_consultado_utc"] = DateTime.UtcNow;
                fila["rnd_coincidencias"] = System.Text.Json.JsonSerializer.Serialize(coincidencias);
                fila["estado_cruce"] = coincidencias.Count == 0 ? "NO_LOCALIZADO" : "LOCALIZADO";
            }
        }
        using var copia = new SqlBulkCopy(db, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, tx)
        {
            DestinationTableName = "primer_respondiente.registro"
        };
        foreach(DataColumn columna in datos.Columns)
        {
            copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
        }
        await copia.WriteToServerAsync(datos);
        var loteMs = reloj.ElapsedMilliseconds - conexionMs - preparacionMs;
        await db.ExecuteAsync("INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'VALIDAR_CARGA', @id)", new { UsuarioId = usuario.Id, id }, tx);
        await tx.CommitAsync();
        logger.LogInformation("Guardado de carga {IdCarga}: {Registros} registros; conexión {ConexionMs} ms; preparación {PreparacionMs} ms; lote {LoteMs} ms; total {TotalMs} ms", id, registros.Count, conexionMs, preparacionMs, loteMs, reloj.ElapsedMilliseconds);
        return id;
    }

    private static Task<bool> TieneEntregaAsync(SqlConnection db, SqlTransaction tx, string? institucion, PeriodoInfo periodo)
    {
        return db.ExecuteScalarAsync<bool>("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM primer_respondiente.carga
                WHERE institucion = @institucion AND estado = 'CONFIRMADA' AND vigente = 1
                    AND (id_periodo = @Id OR (id_periodo IS NULL AND periodo_inicio = @Inicio AND periodo_fin = @Fin))
            ) THEN 1 ELSE 0 END AS bit)
            """, new { institucion, periodo.Id, periodo.Inicio, periodo.Fin }, tx);
    }

    public static DataTable PrepararRegistros(Guid idCarga, IReadOnlyList<RegistroCarga> registros)
    {
        var datos = new DataTable();
        datos.Columns.Add("id_carga", typeof(Guid));
        datos.Columns.Add("fila", typeof(int));
        datos.Columns.Add("corporacion", typeof(string));
        datos.Columns.Add("folio_rnd", typeof(string));
        datos.Columns.Add("fecha_detencion", typeof(DateTime));
        datos.Columns.Add("autoridad_puesta", typeof(string));
        datos.Columns.Add("nombre_detenido", typeof(string));
        datos.Columns.Add("entidad", typeof(byte));
        datos.Columns.Add("tipo_fuero", typeof(string));
        foreach(var registro in registros)
        {
            datos.Rows.Add(idCarga, registro.Fila, registro.Corporacion, registro.FolioRnd, registro.FechaDetencion,
                registro.AutoridadPuesta, registro.NombreDetenido, registro.Entidad, registro.TipoFuero);
        }
        return datos;
    }

    // Se aplica únicamente el acceso elegido; no se mezclan instituciones ni entidades de otras asignaciones.
    private static string Filtro(UsuarioInfo usuario) => usuario.Rol == "SUPER_USUARIO"
        ? "1 = 1"
        : "(@Institucion IS NULL OR c.institucion = @Institucion) AND (@Ambito = 'NACIONAL' OR NOT EXISTS(SELECT 1 FROM primer_respondiente.registro r WHERE r.id_carga = c.id AND ((@Ambito = 'ESTATAL' AND r.entidad <> @Entidad))))";

    public async Task<IEnumerable<CargaInfo>> ListarAsync(UsuarioInfo usuario)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<CargaInfo>($"SELECT TOP (200) {Columnas} FROM primer_respondiente.carga c WHERE c.estado = 'CONFIRMADA' AND c.vigente = 1 AND {Filtro(usuario)} ORDER BY creado_utc DESC", usuario);
    }

    public async Task<CargaInfo?> ObtenerAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga c WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND {Filtro(usuario)}", new { id, usuario.Institucion, usuario.Ambito, usuario.Entidad });
    }

    public async Task<List<ErrorFila>> ValidarDuplicadosAsync(int idPeriodo, IReadOnlyList<RegistroCarga> registros)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var periodo = await db.QuerySingleOrDefaultAsync<PeriodoInfo>("SELECT id, inicio, fin FROM primer_respondiente.periodo WHERE id = @idPeriodo", new { idPeriodo }, tx);
        if(periodo == null) throw new ArgumentException("El periodo no existe.");
        await db.ExecuteAsync("""
            SELECT TOP (0) fila, corporacion, folio_rnd, fecha_detencion, nombre_detenido
            INTO #nuevos FROM primer_respondiente.registro;
            """, transaction: tx);
        using var datos = PrepararRegistros(Guid.Empty, registros);
        using(var copia = new SqlBulkCopy(db, SqlBulkCopyOptions.Default, tx))
        {
            copia.DestinationTableName = "#nuevos";
            foreach(var columna in new[] { "fila", "corporacion", "folio_rnd", "fecha_detencion", "nombre_detenido" })
            {
                copia.ColumnMappings.Add(columna, columna);
            }
            await copia.WriteToServerAsync(datos);
        }
        var errores = await ConsultarDuplicadosAsync(db, tx, "SELECT * FROM #nuevos", periodo, null);
        await db.ExecuteAsync("DROP TABLE #nuevos;", transaction: tx);
        await tx.CommitAsync();
        return errores;
    }

    private static async Task<List<ErrorFila>> ConsultarDuplicadosAsync(SqlConnection db, SqlTransaction tx, string fuente, PeriodoInfo periodo, Guid? id)
    {
        // fuente es una consulta interna fija, nunca texto recibido del usuario.
        var duplicados = await db.QueryAsync<DuplicadoInfo>($"""
            SELECT nuevo.fila, anterior.folio AS FolioEntrega, anterior.periodo_inicio AS Inicio,
                anterior.periodo_fin AS Fin, nuevo.folio_rnd AS FolioRnd, nuevo.nombre_detenido AS Nombre
            FROM ({fuente}) nuevo
            CROSS APPLY (
                SELECT TOP (1) c.folio, c.periodo_inicio, c.periodo_fin
                FROM primer_respondiente.registro previo
                JOIN primer_respondiente.carga c ON c.id = previo.id_carga
                WHERE c.estado = 'CONFIRMADA' AND c.vigente = 1
                    AND (c.id_periodo <> @IdPeriodo OR (c.id_periodo IS NULL AND (c.periodo_inicio <> @Inicio OR c.periodo_fin <> @Fin)))
                    AND previo.corporacion = nuevo.corporacion AND previo.fecha_detencion = nuevo.fecha_detencion
                    AND ((nuevo.folio_rnd <> '' AND previo.folio_rnd = nuevo.folio_rnd
                        AND (nuevo.nombre_detenido = '' OR previo.nombre_detenido = '' OR previo.nombre_detenido = nuevo.nombre_detenido))
                        OR (nuevo.nombre_detenido <> '' AND previo.nombre_detenido = nuevo.nombre_detenido))
                ORDER BY c.confirmado_utc DESC, c.id
            ) anterior
            ORDER BY nuevo.fila;
            """, new { IdPeriodo = periodo.Id, periodo.Inicio, periodo.Fin, id }, tx, commandTimeout: 180);
        return duplicados.Select(d => new ErrorFila(d.Fila, "REGISTRO_DUPLICADO",
            $"Información ya reportada en la entrega {d.FolioEntrega}, periodo {d.Inicio:dd/MM/yyyy} al {d.Fin:dd/MM/yyyy}. Corrija el archivo; no se permite reportarla nuevamente.",
            string.IsNullOrWhiteSpace(d.FolioRnd) ? d.Nombre : d.FolioRnd)).ToList();
    }

    private sealed class DuplicadoInfo
    {
        public int Fila { get; set; }
        public string FolioEntrega { get; set; } = "";
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string FolioRnd { get; set; } = "";
        public string Nombre { get; set; } = "";
    }

    public async Task<ResultadoConfirmacionCarga> ConfirmarAsync(UsuarioInfo usuario, Guid id, bool aceptaNoLocalizados = false)
    {
        await using var db = await factory.AbrirAsync();
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        var periodoId = await db.ExecuteScalarAsync<int?>("SELECT id_periodo FROM primer_respondiente.carga WHERE id = @id", new { id }, tx);
        if(periodoId == null) return new(null);
        var bloqueoPeriodo = await db.ExecuteScalarAsync<int>("""
            DECLARE @r int;
            EXEC @r = sys.sp_getapplock @Resource=@recurso, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            SELECT @r;
            """, new { recurso = "EM-CONFIRMAR-ENTREGA" }, tx);
        if(bloqueoPeriodo < 0) return new(null, Mensaje: "Hay otra entrega en curso para esta quincena. Intente nuevamente.");
        var estado = await db.QuerySingleOrDefaultAsync<string>("""
            SELECT estado FROM primer_respondiente.carga WITH (UPDLOCK, HOLDLOCK)
            WHERE id = @id AND id_usuario = @UsuarioId AND (@EsSuper = 1 OR institucion = @Institucion)
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, EsSuper = usuario.Rol == "SUPER_USUARIO" }, tx);
        if(estado == null)
        {
            return new(null);
        }
        // Revalidar el alcance actual: pudo cambiar desde la carga.
        var fuera = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM primer_respondiente.registro WHERE id_carga = @id
            AND ((@Ambito = 'ESTATAL' AND entidad <> @Entidad))
            """, new { id, usuario.Ambito, usuario.Entidad }, tx);
        if(fuera > 0)
        {
            return new(null, Mensaje: "La carga queda fuera del alcance actual del usuario.");
        }
        if(estado == "VALIDADA")
        {
            var periodo = await db.QuerySingleAsync<PeriodoInfo>("SELECT id, inicio, fin FROM primer_respondiente.periodo WHERE id = @periodoId", new { periodoId }, tx);
            if(usuario.Rol != "SUPER_USUARIO" && await TieneEntregaAsync(db, tx, usuario.Institucion, periodo))
            {
                return new(null, Mensaje: "La corporación ya tiene una entrega confirmada para esta quincena. No puede realizar otra carga inicial.");
            }
            var noLocalizados = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM primer_respondiente.registro WHERE id_carga = @id AND estado_cruce <> 'LOCALIZADO'", new { id }, tx);
            if(noLocalizados > 0 && !aceptaNoLocalizados)
            {
                return new(null, Mensaje: $"Debe aceptar expresamente los {noLocalizados} registros no localizados en RND.");
            }
            // Serializa la confirmación del mismo archivo, incluso entre usuarios distintos.
            var recurso = await db.ExecuteScalarAsync<string>("""
                SELECT CONCAT('EM-CARGA-', institucion, '-', id_periodo, '-', sha256)
                FROM primer_respondiente.carga WHERE id = @id
                """, new { id }, tx);
            var bloqueo = await db.ExecuteScalarAsync<int>("""
                DECLARE @resultado int;
                EXEC @resultado = sys.sp_getapplock @Resource = @recurso,
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                SELECT @resultado;
                """, new { recurso }, tx);
            if(bloqueo < 0)
            {
                return new(null, Mensaje: "Hay otra confirmación en curso. Intente nuevamente.");
            }
            var duplicada = await db.ExecuteScalarAsync<bool>("""
                SELECT CAST(CASE WHEN EXISTS(
                    SELECT 1 FROM primer_respondiente.carga original
                    JOIN primer_respondiente.carga nueva ON nueva.id = @id
                    WHERE original.id <> nueva.id AND original.estado = 'CONFIRMADA'
                        AND original.institucion = nueva.institucion
                        AND original.id_periodo = nueva.id_periodo AND original.sha256 = nueva.sha256
                ) THEN 1 ELSE 0 END AS bit)
                """, new { id }, tx);
            if(duplicada)
            {
                return new(null, Mensaje: "Este archivo ya fue confirmado para la corporación y periodo seleccionados. Consulte la carga existente.");
            }
            var habilitado = await db.ExecuteScalarAsync<bool>("""
                SELECT CAST(CASE WHEN EXISTS(
                    SELECT 1 FROM primer_respondiente.periodo p WITH(UPDLOCK, HOLDLOCK)
                    JOIN primer_respondiente.carga c ON c.id_periodo = p.id
                    WHERE c.id = @id AND p.habilitado = 1 AND p.cerrado = 0
                ) THEN 1 ELSE 0 END AS bit)
                """, new { id }, tx);
            if(!habilitado)
            {
                return new(null, Mensaje: "El periodo no está habilitado. Vuelva a validar el archivo con un periodo disponible.");
            }
            var repetidos = await ConsultarDuplicadosAsync(db, tx,
                "SELECT * FROM primer_respondiente.registro WHERE id_carga = @id", periodo, id);
            if(repetidos.Count > 0)
            {
                return new(null, "La carga no puede continuar: contiene información ya reportada en otro periodo.", repetidos);
            }
            await db.ExecuteAsync("""
                UPDATE primer_respondiente.carga SET vigente = 0
                WHERE id_periodo = @periodoId AND institucion = (SELECT institucion FROM primer_respondiente.carga WHERE id = @id)
                    AND estado = 'CONFIRMADA' AND vigente = 1 AND id <> @id;
                UPDATE primer_respondiente.carga SET estado = 'CONFIRMADA', confirmado_utc = SYSUTCDATETIME(),
                    folio = CONCAT('EM-PR-', YEAR(SYSUTCDATETIME()), '-', NEXT VALUE FOR primer_respondiente.folio_seq)
                WHERE id = @id;
                INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'CONFIRMAR_CARGA', @id);
                IF @aceptaNoLocalizados = 1 INSERT seguridad.bitacora(id_usuario, accion, id_carga) VALUES(@UsuarioId, 'ACEPTAR_NO_LOCALIZADOS_RND', @id);
                """, new { id, UsuarioId = usuario.Id, aceptaNoLocalizados, periodoId }, tx);
            await db.ExecuteAsync("""
                UPDATE r SET duplicado = CASE WHEN r.folio_rnd <> '' AND EXISTS(
                    SELECT 1 FROM primer_respondiente.registro otro
                    JOIN primer_respondiente.carga oc ON oc.id = otro.id_carga
                    WHERE oc.estado = 'CONFIRMADA' AND oc.vigente = 1
                        AND otro.id <> r.id AND otro.folio_rnd = r.folio_rnd
                ) THEN 1 ELSE 0 END
                FROM primer_respondiente.registro r JOIN primer_respondiente.carga c ON c.id = r.id_carga
                WHERE c.estado = 'CONFIRMADA' AND c.vigente = 1;
                UPDATE primer_respondiente.periodo SET cerrado = 1
                WHERE id = @periodoId
                    AND EXISTS(SELECT 1 FROM primer_respondiente.periodo_corporacion WHERE id_periodo = @periodoId)
                    AND NOT EXISTS(
                        SELECT 1 FROM primer_respondiente.periodo_corporacion esperada
                        WHERE esperada.id_periodo = @periodoId AND NOT EXISTS(
                            SELECT 1 FROM primer_respondiente.carga c WHERE c.id_periodo = @periodoId
                                AND c.institucion = esperada.institucion AND c.estado = 'CONFIRMADA' AND c.vigente = 1));
                """, new { periodoId }, tx);
        }
        var carga = await db.QuerySingleAsync<CargaInfo>($"SELECT {Columnas} FROM primer_respondiente.carga WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
        return new(carga);
    }

    public async Task<CargaInfo?> ObtenerParaAcuseAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QuerySingleOrDefaultAsync<CargaInfo>($"""
            SELECT {Columnas} FROM primer_respondiente.carga c WHERE c.id = @id AND {Filtro(usuario)}
            AND (c.estado = 'CONFIRMADA' OR (c.estado = 'VALIDADA' AND c.id_usuario = @UsuarioId AND @PuedeCargar = 1))
            """, new { id, UsuarioId = usuario.Id, usuario.Institucion, usuario.Ambito, usuario.Entidad, PuedeCargar = usuario.Rol is "SUPER_USUARIO" or "PRIMER_RESPONDIENTE" });
    }

    public async Task<IEnumerable<RegistroDescarga>> RegistrosAsync(UsuarioInfo usuario, Guid id)
    {
        await using var db = await factory.AbrirAsync();
        return await db.QueryAsync<RegistroDescarga>($"""
            SELECT r.corporacion, r.folio_rnd AS FolioRnd, r.fecha_detencion AS FechaDetencion,
                r.autoridad_puesta AS AutoridadPuesta, r.nombre_detenido AS NombreDetenido,
                e.nombre AS Entidad, COALESCE(f.nombre, r.tipo_fuero) AS TipoFuero
            FROM primer_respondiente.registro r
            JOIN primer_respondiente.carga c ON c.id = r.id_carga
            JOIN catalogo.entidad e ON e.id = r.entidad
            LEFT JOIN catalogo.fuero f ON f.codigo = r.tipo_fuero
            WHERE c.id = @id AND c.estado = 'CONFIRMADA' AND c.vigente = 1 AND {Filtro(usuario)} ORDER BY r.fila
            """, new { id, usuario.Institucion, usuario.Ambito, usuario.Entidad });
    }
}
