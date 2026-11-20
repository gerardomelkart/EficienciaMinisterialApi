using ClosedXML.Excel;
using EficienciaMinisterialAPI.Models;
using System.Text.Json;
using static EficienciaMinisterialAPI.Services.PlantillaFiscaliaService;

namespace EficienciaMinisterialAPI.Services;

public sealed record IncidenciaFiscalia(int? Fila, string Campo, string Detalle);

public sealed class ValidacionFiscalia(int esperados)
{
    internal SortedDictionary<long, Dictionary<string,string>> Respuestas { get; } = new();
    public int Esperados { get; } = esperados;
    public int Recibidos { get; private set; }
    public int NoRecibidos { get; private set; }
    public List<IncidenciaFiscalia> Errores { get; } = [];

    private static string TextoComparable(string valor) =>
        new string(ExcelCargaService.Normalizar(valor).Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c)).ToArray());

    private static string Fecha(DateTime valor) => valor.ToString("yyyy-MM-dd HH:mm:ss");
    private static string Clave(params string[] valores) => JsonSerializer.Serialize(valores);
    private static string ClaveOrigen(FilaInformePr r) => Clave(Fecha(r.PeriodoInicio), Fecha(r.PeriodoFin),
        r.FolioEntrega, r.Corporacion, r.FolioRnd, Fecha(r.FechaDetencion), r.NombreDetenido, r.Entidad, r.AutoridadPuesta, r.Fuero);

    public static ValidacionFiscalia Revisar(XLWorkbook libro, List<FilaInformePr> filas, List<Opcion> opciones)
    {
        var resultado = new ValidacionFiscalia(filas.Count);
        var columnas = Origen.Concat(Respuesta).ToArray();
        if(!libro.TryGetWorksheet("Seguimiento", out var hoja)
            || columnas.Where((titulo, i) => hoja.Cell(2, i + 1).GetString() != titulo).Any())
        {
            resultado.Errores.Add(new(null, "Plantilla", "Descargue la plantilla vigente y conserve sus encabezados."));
            return resultado;
        }
        var pendientes = filas.GroupBy(ClaveOrigen).ToDictionary(g => g.Key, g => new Queue<long>(g.Select(f => f.IdRegistro)));
        var listas = opciones.ToLookup(x => x.Grupo);
        foreach(var fila in hoja.RowsUsed(XLCellsUsedOptions.Contents).Where(r => r.RowNumber() >= 3))
        {
            string Valor(int col) => fila.Cell(col).HasFormula ? "[fórmula]" : fila.Cell(col).GetString().Trim();
            void Error(int col, string mensaje) => resultado.Errores.Add(new(fila.RowNumber(), columnas[col - 1], mensaje));
            var clave = Clave(Enumerable.Range(1, Origen.Length).Select(i => {
                var c = fila.Cell(i);
                if(c.HasFormula) return "[fórmula]";
                return i is 1 or 2 or 6 && c.DataType == XLDataType.DateTime ? Fecha(c.GetDateTime()) : c.GetString();
            }).ToArray());
            if(!pendientes.TryGetValue(clave, out var ids) || !ids.TryDequeue(out var registro))
            {
                resultado.Errores.Add(new(fila.RowNumber(), "Precarga", "Fila repetida, modificada o ajena a la precarga vigente."));
                continue;
            }

            string Binaria(int col)
            {
                var valor = TextoComparable(Valor(col));
                if(valor == "SI") return "Sí";
                if(valor == "NO") return "No";
                Error(col, "Seleccione Sí o No.");
                return "";
            }
            var recibido = Binaria(11);
            var datos = new Dictionary<string,string> { [columnas[10]] = recibido, [columnas[24]] = Valor(25) };
            resultado.Respuestas[registro] = datos;
            if(recibido == "No")
            {
                resultado.NoRecibidos++;
                for (int col = 12; col <= 24; col++)
                    if (Valor(col).Length > 0)
                        Error(col, "Deje el seguimiento vacío cuando Recibido por Fiscalía es No. Puede agregar comentarios.");
            }
            if(recibido != "Sí") continue;
            resultado.Recibidos++;
            var respuestas = Binarias.Skip(1).ToDictionary(c => c, Binaria);
            foreach(var (columna, respuesta) in respuestas) datos[columnas[columna - 1]] = respuesta;
            if (respuestas[17] == "No" && respuestas[23] == "Sí")
                Error(23, "Revise la vinculación a proceso y el inicio de juicio oral para esta detención: indicó No en vinculación y Sí en juicio oral.");
            foreach(var (grupo, col) in Grupos)
            {
                var valor = Valor(col);
                if(valor.Length == 0)
                {
                    if(col == 13 && respuestas[12] == "Sí" || col == 16 && respuestas[15] == "No" || col == 18 && respuestas[17] == "No")
                        Error(col, "Seleccione el motivo correspondiente.");
                    continue;
                }
                if (col == 13 && respuestas[12] == "No")
                    Error(col, "Indicó No en libertad ministerial. Revise esa respuesta o deje vacío el motivo de libertad.");
                if (col == 16 && respuestas[15] == "Sí")
                    Error(col, "Indicó que la detención fue legal. Revise esa respuesta o deje vacío el motivo de detención ilegal.");
                if (col == 18 && respuestas[17] == "Sí")
                    Error(col, "Indicó que hubo vinculación a proceso. Revise esa respuesta o deje vacío el motivo de no vinculación.");
                if (col == 18 && respuestas[12] == "No" && TextoComparable(valor) == "LIBERTADENSEDEMINISTERIAL")
                    Error(col, "El motivo de no vinculación indica libertad en sede ministerial, pero respondió No en libertad ministerial.");
                var valores = col == 20 ? valor.Split(';', StringSplitOptions.TrimEntries) : new[] { valor };
                var canonicas = new List<string>();
                foreach(var opcion in valores)
                {
                    var claveOpcion = TextoComparable(opcion);
                    var oficial = listas[grupo].FirstOrDefault(o => TextoComparable(o.Descripcion) == claveOpcion);
                    if(oficial != null) canonicas.Add(oficial.Descripcion);
                    if(claveOpcion.Length == 0 || oficial == null) Error(col, "Use una opción del catálogo.");
                    else if(col == 20 && claveOpcion == "XIVPRISIONPREVENTIVA" && respuestas[19] == "No")
                        Error(col, "Seleccionó prisión preventiva y respondió No en prisión preventiva.");
                }
                datos[columnas[col - 1]] = string.Join("; ", canonicas);
            }
        }
        var faltantes = pendientes.Values.Sum(q => q.Count);
        if(faltantes > 0) resultado.Errores.Add(new(null, "Precarga", $"Faltan {faltantes} filas originales de la precarga vigente."));
        if(resultado.Recibidos + resultado.NoRecibidos != filas.Count)
            resultado.Errores.Add(new(null, "Recibido por fiscalía", "La suma de Sí y No debe coincidir con el total precargado."));
        return resultado;
    }
}
