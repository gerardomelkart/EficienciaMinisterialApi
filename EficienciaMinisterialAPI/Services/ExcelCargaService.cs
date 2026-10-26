using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class ExcelCargaService(ConfiguracionRepository configuracion)
{
    public static string Normalizar(string valor)
    {
        var texto = valor.Trim().Normalize(NormalizationForm.FormD);
        return new string(texto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize().ToUpperInvariant();
    }

    public async Task<(List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos)> LeerAsync(byte[] archivo, UsuarioInfo usuario)
    {
        var config = await configuracion.ObtenerAsync();
        var campos = config.Campos;
        var encabezados = campos.Select(c => c.Encabezado).ToArray();
        int Columna(string codigo) => campos.Single(c => c.Codigo == codigo).Posicion;
        var entidades = config.Entidades.ToDictionary(e => Normalizar(e.Nombre), e => e.Id);
        foreach(var alias in config.Alias.Where(a => a.Tipo == "ENTIDAD"))
        {
            if(byte.TryParse(alias.Codigo, out var id) && config.Entidades.Any(e => e.Id == id))
            {
                entidades[Normalizar(alias.Nombre)] = id;
            }
        }
        var instituciones = config.Instituciones.ToDictionary(i => Normalizar(i.Codigo), i => i.Codigo);
        foreach(var institucion in config.Instituciones)
        {
            instituciones[Normalizar(institucion.Nombre)] = institucion.Codigo;
        }
        foreach(var alias in config.Alias.Where(a => a.Tipo == "INSTITUCION"))
        {
            if(config.Instituciones.Any(i => i.Codigo == alias.Codigo))
            {
                instituciones[Normalizar(alias.Nombre)] = alias.Codigo;
            }
        }
        var ambito = config.Ambitos.Single(a => a.Codigo == usuario.Ambito);
        using var stream = new MemoryStream(archivo);
        using var libro = new XLWorkbook(stream);
        var hoja = libro.Worksheets.FirstOrDefault(h => h.Cell(1, campos[0].Posicion).GetString().Trim() == encabezados[0]);
        List<RegistroCarga> registros = [];
        List<ErrorFila> errores = [];
        List<string> avisos = [];
        if(hoja == null)
        {
            errores.Add(new(1, "ESTRUCTURA", "No se encontró una hoja con los encabezados de la plantilla."));
            return (registros, errores, avisos);
        }
        for(var columna = 1; columna <= encabezados.Length; columna++)
        {
            if(hoja.Cell(1, columna).GetString().Trim() != encabezados[columna - 1])
            {
                errores.Add(new(1, encabezados[columna - 1], $"Encabezado incorrecto en columna {columna}."));
            }
        }
        if(errores.Count > 0)
        {
            return (registros, errores, avisos);
        }
        var ultima = hoja.LastRowUsed()?.RowNumber() ?? 1;
        if(ultima > config.Valor<int>("Carga.MaxFilas") + 1)
        {
            errores.Add(new(0, "ARCHIVO", $"Máximo de {config.Valor<int>("Carga.MaxFilas")} filas por archivo."));
            return (registros, errores, avisos);
        }
        var folios = new HashSet<string>();
        for(var fila = 2; fila <= ultima; fila++)
        {
            if(Enumerable.Range(1, campos.Count).All(c => hoja.Cell(fila, c).IsEmpty()))
            {
                continue;
            }
            var anteriores = errores.Count;
            var valores = Enumerable.Range(1, campos.Count).Select(c => hoja.Cell(fila, c).GetString().Trim()).ToArray();
            for(var i = 0; i < campos.Count; i++)
            {
                if(!config.Valor<bool>("Carga.PermitirFormulas") && hoja.Cell(fila, i + 1).HasFormula)
                {
                    errores.Add(new(fila, encabezados[i], "Use valores, no fórmulas."));
                }
                if(campos[i].Requerido && string.IsNullOrWhiteSpace(valores[i]))
                {
                    errores.Add(new(fila, encabezados[i], "Campo requerido por la configuración vigente."));
                }
            }
            string Valor(string codigo) => valores[Columna(codigo) - 1];
            var corporacion = instituciones.GetValueOrDefault(Normalizar(Valor("CORPORACION")), "");
            if(corporacion != usuario.Institucion)
            {
                errores.Add(new(fila, "CORPORACION", "La corporación debe corresponder a la institución del usuario."));
            }
            var folio = Normalizar(Valor("FOLIO_RND"));
            var partes = folio.Split('/');
            if(!Regex.IsMatch(folio, config.Valor<string>("Carga.PatronFolio"), RegexOptions.None, TimeSpan.FromSeconds(1))
                || partes.Length != 5
                || !DateTime.TryParseExact(partes.ElementAtOrDefault(3), config.Valor<string>("Carga.FormatoFechaFolio"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errores.Add(new(fila, "FOLIO_RND", "Folio inválido según el formato configurado."));
            }
            if(!folios.Add(folio) && config.Valor<bool>("Carga.AvisarDuplicados"))
            {
                avisos.Add($"Fila {fila}: folio repetido dentro del archivo; pendiente definir su tratamiento operativo.");
            }
            var celdaFecha = hoja.Cell(fila, Columna("FECHA_DETENCION"));
            var fechaValida = celdaFecha.TryGetValue<DateTime>(out var fecha) && celdaFecha.DataType != XLDataType.Text;
            if(!fechaValida)
            {
                errores.Add(new(fila, "FECHA_DETENCION", "Debe ser una fecha de Excel, no texto."));
            }
            var entidadValida = entidades.TryGetValue(Normalizar(Valor("ENTIDAD_DETENCION")), out var entidad);
            if(!entidadValida)
            {
                errores.Add(new(fila, "ENTIDAD_DETENCION", "Entidad no reconocida."));
            }
            if(ambito.RequiereEntidad && entidad != usuario.Entidad)
            {
                errores.Add(new(fila, "ENTIDAD_DETENCION", "La entidad queda fuera del alcance del usuario."));
            }
            var fuero = config.Fueros.FirstOrDefault(f => Normalizar(f.Nombre) == Normalizar(Valor("TIPO_FUERO")))?.Codigo ?? "";
            if(fuero.Length == 0 || (ambito.Fuero != null && fuero != ambito.Fuero))
            {
                errores.Add(new(fila, "TIPO_FUERO", "Seleccione un fuero del catálogo dentro del alcance del usuario."));
            }
            if(config.Valor<bool>("Carga.AvisarDiferencias") && partes.Length == 5 && fuero.Length > 0 && partes[1] != fuero)
            {
                avisos.Add($"Fila {fila}: el fuero del folio difiere del fuero reportado; requiere revisión.");
            }
            if(config.Valor<bool>("Carga.AvisarDiferencias") && fechaValida && partes.Length == 5 && DateTime.TryParseExact(partes[3], config.Valor<string>("Carga.FormatoFechaFolio"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaFolio) && fechaFolio.Date != fecha.Date)
            {
                avisos.Add($"Fila {fila}: la fecha del folio difiere de la fecha reportada; se conservan los datos originales.");
            }
            foreach(var campo in campos.Where(c => c.MaxLongitud != null))
            {
                if(Valor(campo.Codigo).Length > campo.MaxLongitud)
                {
                    errores.Add(new(fila, campo.Encabezado, $"Máximo {campo.MaxLongitud} caracteres."));
                }
            }
            if(errores.Count == anteriores)
            {
                registros.Add(new(fila, corporacion, folio, fecha.Date, Valor("AUTORIDAD_EN_DONDE_SE_HIZO_LA_PUESTA"), Valor("NOMBRE_DETENIDO"), entidad, fuero));
            }
        }
        if(registros.Count == 0 && errores.Count == 0)
        {
            errores.Add(new(0, "ARCHIVO", "El archivo no contiene registros."));
        }
        return (registros, errores, avisos);
    }
}
