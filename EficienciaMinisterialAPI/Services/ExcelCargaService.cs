using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using EficienciaMinisterialAPI.Repositories;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Services;
public sealed class ExcelCargaService(CatalogoRepository catalogos, LimitesCarga limites, ILogger<ExcelCargaService> logger)
{
    public static readonly string[] Encabezados = ["CORPORACION", "FOLIO_RND", "FECHA_DETENCION", "AUTORIDAD_EN_DONDE_SE_HIZO_LA_PUESTA", "NOMBRE_DETENIDO", "ENTIDAD_DETENCION", "TIPO_FUERO"];

    public static string Normalizar(string valor)
    {
        var texto = valor.Trim().Normalize(NormalizationForm.FormD);
        return Regex.Replace(new string(texto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize().ToUpperInvariant(), @"\s+", " ");
    }

    public static byte[] GenerarArchivo(IEnumerable<RegistroDescarga> registros)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("DETENCIONES");
        for(var columna = 0; columna < Encabezados.Length; columna++)
        {
            hoja.Cell(1, columna + 1).Value = Encabezados[columna];
        }
        var fila = 2;
        foreach(var registro in registros)
        {
            hoja.Cell(fila, 1).Value = registro.Corporacion;
            hoja.Cell(fila, 2).Value = registro.FolioRnd;
            hoja.Cell(fila, 3).Value = registro.FechaDetencion;
            hoja.Cell(fila, 4).Value = registro.AutoridadPuesta;
            hoja.Cell(fila, 5).Value = registro.NombreDetenido;
            hoja.Cell(fila, 6).Value = registro.Entidad;
            hoja.Cell(fila, 7).Value = registro.TipoFuero;
            fila++;
        }
        hoja.Column(3).Style.DateFormat.Format = "dd/mm/yyyy";
        hoja.Range(1, 1, fila - 1, Encabezados.Length).CreateTable();
        hoja.Columns().Width = 30;
        hoja.Column(4).Width = 50;
        hoja.Column(5).Width = 50;
        hoja.SheetView.FreezeRows(1);
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }

    public static byte[] ExportarResultados(IReadOnlyList<IncidenciaValidacion> filas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Validación");
        string[] columnas = ["TIPO", "FILA_EXCEL", "CAMPO", "VALOR_EXCEL", "DETALLE", "RESULTADO_CRUCE"];
        for(var columna = 0; columna < columnas.Length; columna++)
        {
            hoja.Cell(1, columna + 1).Value = columnas[columna];
        }
        for(var indice = 0; indice < filas.Count; indice++)
        {
            var fila = filas[indice];
            hoja.Cell(indice + 2, 1).Value = fila.Tipo;
            if(fila.Fila.HasValue)
            {
                hoja.Cell(indice + 2, 2).Value = fila.Fila.Value;
            }
            hoja.Cell(indice + 2, 3).Value = fila.Campo;
            hoja.Cell(indice + 2, 4).Value = fila.Valor ?? "";
            hoja.Cell(indice + 2, 5).Value = fila.Mensaje;
            var cruce = fila.Cruce ?? "";
            hoja.Cell(indice + 2, 6).Value = cruce.Length > 32767 ? cruce[..32700] + " [Detalle truncado por el límite de Excel; consulte la pantalla]" : cruce;
        }
        hoja.Range(1, 1, filas.Count + 1, columnas.Length).CreateTable();
        hoja.SheetView.FreezeRows(1);
        hoja.Column(1).Width = 18;
        hoja.Column(2).Width = 15;
        hoja.Column(3).Width = 32;
        hoja.Column(4).Width = 38;
        hoja.Column(4).Style.Alignment.WrapText = true;
        hoja.Column(5).Width = 100;
        hoja.Column(5).Style.Alignment.WrapText = true;
        hoja.Column(6).Width = 100;
        hoja.Column(6).Style.Alignment.WrapText = true;
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }

    public async Task<(List<RegistroCarga> Registros, List<ErrorFila> Errores, List<ErrorFila> Avisos)> LeerAsync(byte[] archivo, UsuarioInfo usuario)
    {
        var reloj = Stopwatch.StartNew();
        var catalogo = await catalogos.ObtenerCapturaAsync();
        var catalogosMs = reloj.ElapsedMilliseconds;
        var resultado = Leer(archivo, usuario, catalogo, limites.MaximoFilas);
        logger.LogInformation("Validación Excel: catálogos {CatalogosMs} ms; lectura y reglas {LecturaMs} ms; registros válidos {Registros}; errores {Errores}; observaciones {Avisos}", catalogosMs, reloj.ElapsedMilliseconds - catalogosMs, resultado.Registros.Count, resultado.Errores.Count, resultado.Avisos.Count);
        return resultado;
    }

    public static (List<RegistroCarga> Registros, List<ErrorFila> Errores, List<ErrorFila> Avisos) Leer(byte[] archivo, UsuarioInfo usuario, CatalogoCaptura catalogo, int maximoFilas)
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
        List<ErrorFila> avisos = [];
        if(hoja == null)
        {
            errores.Add(new(1, "ESTRUCTURA", "No se encontró una hoja con los encabezados de la plantilla."));
            return (registros, errores, avisos);
        }
        for(var columna = 1; columna <= Encabezados.Length; columna++)
        {
            if(hoja.Cell(1, columna).GetString().Trim() != Encabezados[columna - 1])
            {
                errores.Add(new(1, Encabezados[columna - 1], $"Encabezado incorrecto en columna {columna}.", hoja.Cell(1, columna).GetString()));
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
                avisos.Add(new(fila, "FOLIO_RND", "Sin folio válido; requiere búsqueda por nombre en RND.", hoja.Cell(fila, 2).GetString()));
            }
            if(folio.Length > 0 && !folios.Add(folio))
            {
                avisos.Add(new(fila, "FOLIO_RND", "Folio repetido dentro del archivo; se conservan todas las filas para el seguimiento por persona.", hoja.Cell(fila, 2).GetString()));
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
                avisos.Add(new(fila, "TIPO_FUERO", "El fuero del folio difiere del fuero reportado; requiere revisión.", hoja.Cell(fila, 7).GetString()));
            }
            if(fechaValida && partes.Length == 5 && DateTime.TryParseExact(partes[3], "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaFolio) && fechaFolio.Date != fecha.Date)
            {
                avisos.Add(new(fila, "FECHA_DETENCION", "La fecha del folio difiere de la fecha reportada; se conservan los datos originales.", hoja.Cell(fila, 3).GetString()));
            }
            if(valores[3].Length > 500 || valores[4].Length > 300)
            {
                errores.Add(new(fila, "LONGITUD", "Autoridad: máximo 500 caracteres. Nombre: máximo 300."));
            }
            for(var indice = anteriores; indice < errores.Count; indice++)
            {
                var columna = Array.IndexOf(Encabezados, errores[indice].Campo);
                var valor = columna >= 0 ? hoja.Cell(fila, columna + 1).GetString()
                    : errores[indice].Campo == "LONGITUD" ? $"Autoridad: {hoja.Cell(fila, 4).GetString()}; Nombre: {hoja.Cell(fila, 5).GetString()}" : null;
                errores[indice] = errores[indice] with { Valor = valor };
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
            avisos.Add(new(0, "", "La entrega quedará pendiente de cruce con RND. Los registros no se enviarán a fiscalías hasta completar ese cruce."));
        }
        return (registros, errores, avisos);
    }
}
