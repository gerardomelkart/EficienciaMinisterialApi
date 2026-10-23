using ClosedXML.Excel;
using EficienciaMinisterialAPI.Controllers;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;
using Microsoft.AspNetCore.Mvc;

var usuario = new UsuarioInfo { Id = 1, Rol = "PRIMER_RESPONDIENTE", Institucion = "SSPC", Ambito = "NACIONAL" };
string[] nombres = ["Aguascalientes", "Baja California", "Baja California Sur", "Campeche", "Coahuila de Zaragoza", "Colima", "Chiapas", "Chihuahua", "Ciudad de México", "Durango", "Guanajuato", "Guerrero", "Hidalgo", "Jalisco", "México", "Michoacán de Ocampo", "Morelos", "Nayarit", "Nuevo León", "Oaxaca", "Puebla", "Querétaro", "Quintana Roo", "San Luis Potosí", "Sinaloa", "Sonora", "Tabasco", "Tamaulipas", "Tlaxcala", "Veracruz de Ignacio de la Llave", "Yucatán", "Zacatecas"];
var entidades = nombres.Select((nombre, index) => (Nombre: ExcelCargaService.Normalizar(nombre), Id: (byte)(index + 1))).ToDictionary(e => e.Nombre, e => e.Id);
var pruebas = 0;

void Verificar(bool condicion, string descripcion)
{
    if(!condicion)
    {
        throw new InvalidOperationException(descripcion);
    }
    pruebas++;
    Console.WriteLine($"OK: {descripcion}");
}

byte[] Archivo(Action<IXLWorksheet>? modificar = null)
{
    using var libro = new XLWorkbook();
    var hoja = libro.AddWorksheet("PLANTILLA");
    for(var columna = 1; columna <= 7; columna++)
    {
        hoja.Cell(1, columna).Value = ExcelCargaService.Encabezados[columna - 1];
    }
    hoja.Cell(2, 1).Value = "SSPC";
    hoja.Cell(2, 2).Value = "MS/FF/011/01102026/0001";
    hoja.Cell(2, 3).Value = new DateTime(2026, 10, 1);
    hoja.Cell(2, 4).Value = "FGR";
    hoja.Cell(2, 5).Value = "PERSONA DE PRUEBA";
    hoja.Cell(2, 6).Value = "MORELOS";
    hoja.Cell(2, 7).Value = "Fuero federal";
    modificar?.Invoke(hoja);
    using var memoria = new MemoryStream();
    libro.SaveAs(memoria);
    return memoria.ToArray();
}

var valido = ExcelCargaService.Leer(Archivo(), usuario, entidades);
Verificar(valido.Registros.Count == 1 && valido.Errores.Count == 0, "Acepta registro válido con fecha real de Excel.");
var textoFecha = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 3).Value = "2026-10-01"), usuario, entidades);
Verificar(textoFecha.Errores.Any(e => e.Campo == "FECHA_DETENCION"), "Rechaza fecha como texto.");
var administracion = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 2).Value = "MS/IA/011/01102026/0001"), usuario, entidades);
Verificar(administracion.Errores.Any(e => e.Campo == "FOLIO_RND"), "Rechaza folio de falta administrativa.");
var imposible = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 2).Value = "MS/FF/011/31112026/0001"), usuario, entidades);
Verificar(imposible.Errores.Any(e => e.Campo == "FOLIO_RND"), "Rechaza fecha imposible dentro del folio.");
var ajena = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 1).Value = "SEDENA"), usuario, entidades);
Verificar(ajena.Errores.Any(e => e.Campo == "CORPORACION"), "No permite cargar por otra institución.");
var alias = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 6).Value = "ESTADO DE MÉXICO"), usuario, entidades);
Verificar(alias.Registros.Single().Entidad == 15, "Homologa Estado de México.");
var formula = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 5).FormulaA1 = "\"PERSONA\""), usuario, entidades);
Verificar(formula.Errores.Any(e => e.Mensaje.Contains("fórmulas")), "Rechaza fórmulas en datos de captura.");
var repetido = ExcelCargaService.Leer(Archivo(h => h.Row(2).CopyTo(h.Row(3))), usuario, entidades);
Verificar(repetido.Registros.Count == 2 && repetido.Avisos.Count > 0, "Advierte repetidos sin imponer una regla pendiente del área.");
usuario.Ambito = "ESTATAL";
usuario.Entidad = 15;
var fuera = ExcelCargaService.Leer(Archivo(), usuario, entidades);
Verificar(fuera.Errores.Any(e => e.Campo == "ENTIDAD_DETENCION"), "Restringe alcance estatal en servidor.");
usuario.Ambito = "FEDERAL";
var comun = ExcelCargaService.Leer(Archivo(h => h.Cell(2, 7).Value = "Fuero común"), usuario, entidades);
Verificar(comun.Errores.Any(e => e.Campo == "TIPO_FUERO"), "Restringe alcance federal en servidor.");
var encabezado = ExcelCargaService.Leer(Archivo(h => h.Cell(1, 2).Value = "OTRO"), usuario, entidades);
Verificar(encabezado.Errores.Any(e => e.Fila == 1), "Detecta encabezados cambiados.");
var plantilla = (FileContentResult)new CargasController(null!, null!).Plantilla();
using(var libro = new XLWorkbook(new MemoryStream(plantilla.FileContents)))
{
    Verificar(libro.Worksheet("PLANTILLA").Cell(1, 7).GetString() == "TIPO_FUERO" && libro.Worksheets.Count == 2, "Plantilla generada conserva siete campos y diccionario.");
}
if(args.Length == 1)
{
    usuario.Ambito = "NACIONAL";
    var resultado = ExcelCargaService.Leer(File.ReadAllBytes(args[0]), usuario, entidades);
    Console.WriteLine($"Archivo local analizado: {resultado.Registros.Count} válidos, {resultado.Errores.Count} errores, {resultado.Avisos.Count} avisos. No se muestran datos personales.");
}
Console.WriteLine($"{pruebas} verificaciones correctas.");
