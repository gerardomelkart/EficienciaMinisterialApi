using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class ExcelCargaService(CatalogoRepository catalogos)
{
    public static readonly string[] Encabezados = ["CORPORACION", "FOLIO_RND", "FECHA_DETENCION", "AUTORIDAD_EN_DONDE_SE_HIZO_LA_PUESTA", "NOMBRE_DETENIDO", "ENTIDAD_DETENCION", "TIPO_FUERO"];

    public static string Normalizar(string valor)
    {
        var texto = valor.Trim().Normalize(NormalizationForm.FormD);
        return new string(texto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize().ToUpperInvariant();
    }

    public async Task<(List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos)> LeerAsync(byte[] archivo, UsuarioInfo usuario)
    {
        var catalogo = await catalogos.ObtenerEntidadesAsync();
        var entidades = catalogo.ToDictionary(e => Normalizar(e.Key), e => e.Value);
        return Leer(archivo, usuario, entidades);
    }

    public static (List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos) Leer(byte[] archivo, UsuarioInfo usuario, Dictionary<string, byte> catalogo)
    {
        var entidades = new Dictionary<string, byte>(catalogo);
        foreach(var alias in new[] { ("ESTADO DE MEXICO", "MEXICO"), ("COAHUILA", "COAHUILA DE ZARAGOZA"), ("MICHOACAN", "MICHOACAN DE OCAMPO"), ("VERACRUZ", "VERACRUZ DE IGNACIO DE LA LLAVE"), ("DISTRITO FEDERAL", "CIUDAD DE MEXICO") })
        {
            if(entidades.TryGetValue(alias.Item2, out var id))
            {
                entidades[alias.Item1] = id;
            }
        }
        using var stream = new MemoryStream(archivo);
        using var libro = new XLWorkbook(stream);
        var hoja = libro.Worksheets.FirstOrDefault(h => h.Cell(1, 1).GetString().Trim() == "CORPORACION");
        List<RegistroCarga> registros = [];
        List<ErrorFila> errores = [];
        List<string> avisos = [];
        if(hoja == null)
        {
            errores.Add(new(1, "ESTRUCTURA", "No se encontró una hoja con los encabezados de la plantilla."));
            return (registros, errores, avisos);
        }
        for(var columna = 1; columna <= Encabezados.Length; columna++)
        {
            if(hoja.Cell(1, columna).GetString().Trim() != Encabezados[columna - 1])
            {
                errores.Add(new(1, Encabezados[columna - 1], $"Encabezado incorrecto en columna {columna}."));
            }
        }
        if(errores.Count > 0)
        {
            return (registros, errores, avisos);
        }
        var ultima = hoja.LastRowUsed()?.RowNumber() ?? 1;
        if(ultima > 20001)
        {
            errores.Add(new(0, "ARCHIVO", "Máximo técnico de 20,000 filas por archivo."));
            return (registros, errores, avisos);
        }
        var folios = new HashSet<string>();
        for(var fila = 2; fila <= ultima; fila++)
        {
            if(Enumerable.Range(1, 7).All(c => hoja.Cell(fila, c).IsEmpty()))
            {
                continue;
            }
            var anteriores = errores.Count;
            var valores = Enumerable.Range(1, 7).Select(c => hoja.Cell(fila, c).GetString().Trim()).ToArray();
            for(var i = 0; i < 7; i++)
            {
                if(hoja.Cell(fila, i + 1).HasFormula)
                {
                    errores.Add(new(fila, Encabezados[i], "Use valores, no fórmulas."));
                }
                if(string.IsNullOrWhiteSpace(valores[i]))
                {
                    errores.Add(new(fila, Encabezados[i], "Campo requerido en esta versión inicial."));
                }
            }
            var corporacion = Normalizar(valores[0]) switch
            {
                "MARINA" or "SECRETARIA DE MARINA" => "SEMAR",
                "GUARDIA NACIONAL" => "GN",
                "SECRETARIA DE LA DEFENSA NACIONAL" => "SEDENA",
                "SECRETARIA DE SEGURIDAD Y PROTECCION CIUDADANA" => "SSPC",
                var codigo => codigo
            };
            if(corporacion != usuario.Institucion)
            {
                errores.Add(new(fila, "CORPORACION", "La corporación debe corresponder a la institución del usuario."));
            }
            var folio = Normalizar(valores[1]);
            var partes = folio.Split('/');
            if(!Regex.IsMatch(folio, @"^[A-Z]{2}/(FC|FF)/[0-9]{3}/[0-9]{8}/[0-9]{4}$")
                || partes.Length != 5
                || !DateTime.TryParseExact(partes.ElementAtOrDefault(3), "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errores.Add(new(fila, "FOLIO_RND", "Folio inválido. Use AA/FC/000/DDMMAAAA/0000 o AA/FF/000/DDMMAAAA/0000. No se admiten IA/AI."));
            }
            if(!folios.Add(folio))
            {
                avisos.Add($"Fila {fila}: folio repetido dentro del archivo; pendiente definir su tratamiento operativo.");
            }
            var celdaFecha = hoja.Cell(fila, 3);
            var fechaValida = celdaFecha.TryGetValue<DateTime>(out var fecha) && celdaFecha.DataType != XLDataType.Text;
            if(!fechaValida)
            {
                errores.Add(new(fila, "FECHA_DETENCION", "Debe ser una fecha de Excel, no texto."));
            }
            var entidadValida = entidades.TryGetValue(Normalizar(valores[5]), out var entidad);
            if(!entidadValida)
            {
                errores.Add(new(fila, "ENTIDAD_DETENCION", "Entidad no reconocida."));
            }
            if(usuario.Ambito == "ESTATAL" && entidad != usuario.Entidad)
            {
                errores.Add(new(fila, "ENTIDAD_DETENCION", "La entidad queda fuera del alcance del usuario."));
            }
            var fuero = Normalizar(valores[6]) switch { "FUERO COMUN" => "FC", "FUERO FEDERAL" => "FF", _ => "" };
            if(fuero.Length == 0 || (usuario.Ambito == "FEDERAL" && fuero != "FF"))
            {
                errores.Add(new(fila, "TIPO_FUERO", "Use Fuero común o Fuero federal dentro del alcance del usuario."));
            }
            if(partes.Length == 5 && fuero.Length > 0 && partes[1] != fuero)
            {
                avisos.Add($"Fila {fila}: el fuero del folio difiere del fuero reportado; requiere revisión.");
            }
            if(fechaValida && partes.Length == 5 && DateTime.TryParseExact(partes[3], "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaFolio) && fechaFolio.Date != fecha.Date)
            {
                avisos.Add($"Fila {fila}: la fecha del folio difiere de la fecha reportada; se conservan los datos originales.");
            }
            if(valores[3].Length > 500 || valores[4].Length > 300)
            {
                errores.Add(new(fila, "LONGITUD", "Autoridad: máximo 500 caracteres. Nombre: máximo 300."));
            }
            if(errores.Count == anteriores)
            {
                registros.Add(new(fila, corporacion, folio, fecha.Date, valores[3], valores[4], entidad, fuero));
            }
        }
        if(registros.Count == 0 && errores.Count == 0)
        {
            errores.Add(new(0, "ARCHIVO", "El archivo no contiene registros."));
        }
        return (registros, errores, avisos);
    }
}
