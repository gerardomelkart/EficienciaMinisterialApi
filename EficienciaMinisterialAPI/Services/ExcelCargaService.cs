using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class ExcelCargaService(CatalogoRepository catalogos, LimitesCarga limites)
{
    public static readonly string[] Encabezados = ["CORPORACION", "FOLIO_RND", "FECHA_DETENCION", "AUTORIDAD_EN_DONDE_SE_HIZO_LA_PUESTA", "NOMBRE_DETENIDO", "ENTIDAD_DETENCION", "TIPO_FUERO"];

    public static string Normalizar(string valor)
    {
        var texto = valor.Trim().Normalize(NormalizationForm.FormD);
        return Regex.Replace(new string(texto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize().ToUpperInvariant(), @"\s+", " ");
    }

    public async Task<(List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos)> LeerAsync(byte[] archivo, UsuarioInfo usuario)
    {
        return Leer(archivo, usuario, await catalogos.ObtenerCapturaAsync(), limites.MaximoFilas);
    }

    public static (List<RegistroCarga> Registros, List<ErrorFila> Errores, List<string> Avisos) Leer(byte[] archivo, UsuarioInfo usuario, CatalogoCaptura catalogo, int maximoFilas)
    {
        Dictionary<string, T> NormalizarCatalogo<T>(Dictionary<string, T> valores) => valores
            .GroupBy(v => Normalizar(v.Key))
            .ToDictionary(g => g.Key, g => g.Select(v => v.Value).Distinct().Single());
        var entidades = NormalizarCatalogo(catalogo.Entidades);
        var corporaciones = NormalizarCatalogo(catalogo.Corporaciones);
        var fueros = NormalizarCatalogo(catalogo.Fueros);
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
        if(ultima - 1 > maximoFilas)
        {
            errores.Add(new(0, "ARCHIVO", $"Máximo técnico de {maximoFilas:N0} filas por archivo."));
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
                if((i is 0 or 2 or 5) && string.IsNullOrWhiteSpace(valores[i]))
                {
                    errores.Add(new(fila, Encabezados[i], "Campo obligatorio."));
                }
            }
            var corporacionValida = corporaciones.TryGetValue(Normalizar(valores[0]), out var corporacion);
            if(!corporacionValida)
            {
                errores.Add(new(fila, "CORPORACION", "Corporación no reconocida o inactiva."));
            }
            else if(corporacion != usuario.Institucion)
            {
                errores.Add(new(fila, "CORPORACION", "La corporación del Excel no corresponde al acceso del usuario."));
            }
            if(string.IsNullOrWhiteSpace(valores[1]) && string.IsNullOrWhiteSpace(valores[4]))
            {
                errores.Add(new(fila, "NOMBRE_DETENIDO", "Debe indicar folio RND o nombre de la persona detenida."));
            }
            var folio = Normalizar(valores[1]);
            if(folio.Length > 40)
            {
                errores.Add(new(fila, "FOLIO_RND", "Máximo 40 caracteres."));
            }
            if(Regex.IsMatch(folio, @"/(IA|AI)/"))
            {
                errores.Add(new(fila, "FOLIO_RND", "No se admiten folios de faltas administrativas IA/AI."));
            }
            var partes = folio.Split('/');
            if(!Regex.IsMatch(folio, @"^[A-Z]{2}/(FC|FF)/[0-9]{3}/[0-9]{8}/[0-9]{4}$")
                || partes.Length != 5
                || !DateTime.TryParseExact(partes.ElementAtOrDefault(3), "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                avisos.Add($"Fila {fila}: sin folio válido; requiere búsqueda por nombre en RND.");
            }
            if(folio.Length > 0 && !folios.Add(folio))
            {
                avisos.Add($"Fila {fila}: folio repetido dentro del archivo; se conservan todas las filas para el seguimiento por persona.");
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
            var fuero = fueros.GetValueOrDefault(Normalizar(valores[6]), "");
            if(valores[6].Length > 0 && fuero.Length == 0)
            {
                errores.Add(new(fila, "TIPO_FUERO", "El fuero debe pertenecer al catálogo vigente, o puede quedar vacío si no se cuenta con la información."));
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
                registros.Add(new(fila, corporacion!, folio, fecha.Date, valores[3], valores[4], entidad, fuero));
            }
        }
        if(registros.Count == 0 && errores.Count == 0)
        {
            errores.Add(new(0, "ARCHIVO", "El archivo no contiene registros."));
        }
        if(registros.Count > 0)
        {
            avisos.Add("La entrega quedará pendiente de cruce con RND. Los registros no se enviarán a fiscalías hasta completar ese cruce.");
        }
        return (registros, errores, avisos);
    }
}
