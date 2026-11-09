#:project ../EficienciaMinisterialAPI/EficienciaMinisterialAPI.csproj
using ClosedXML.Excel;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;

int fallos = 0;
void Probar(string nombre, Action accion)
{
    try { accion(); Console.WriteLine("OK " + nombre); }
    catch(Exception e) { fallos++; Console.WriteLine("ERROR " + nombre + ": " + e.Message); }
}
void Ver(bool valor, string mensaje) { if(!valor) throw new Exception(mensaje); }
UsuarioInfo usuario = new() { Institucion = "SEDENA", Ambito = "ESTATAL", Entidad = 9 };
var catalogo = new Dictionary<string, byte> { ["CIUDAD DE MEXICO"] = 9, ["MEXICO"] = 15 };
byte[] Libro(string folio, string nombre, string autoridad = "", string fuero = "", string entidad = "CDMX", bool dos = false)
{
    using var libro = new XLWorkbook(); var hoja = libro.AddWorksheet("PLANTILLA");
    for(int c = 0; c < 7; c++) hoja.Cell(1, c + 1).Value = ExcelCargaService.Encabezados[c];
    for(int f = 2; f <= (dos ? 3 : 2); f++)
    {
        hoja.Cell(f, 1).Value = "SEDENA"; hoja.Cell(f, 2).Value = folio;
        hoja.Cell(f, 3).Value = new DateTime(2026, 10, 5); hoja.Cell(f, 4).Value = autoridad;
        hoja.Cell(f, 5).Value = dos ? nombre + " " + f : nombre; hoja.Cell(f, 6).Value = entidad; hoja.Cell(f, 7).Value = fuero;
    }
    using var m = new MemoryStream(); libro.SaveAs(m); return m.ToArray();
}
Probar("Nombre normalizado", () => Ver(ExcelCargaService.Normalizar("  José   PéREZ \t López ") == "JOSE PEREZ LOPEZ", "Acentos o espacios no normalizados"));
Probar("Sin folio con nombre y opcionales vacíos", () => Ver(ExcelCargaService.Leer(Libro("", "José Pérez"), usuario, catalogo).Errores.Count == 0, "Campos opcionales rechazados"));
Probar("Folio sin nombre", () => Ver(ExcelCargaService.Leer(Libro("BC/FC/004/05102026/0068", ""), usuario, catalogo).Errores.Count == 0, "Folio válido rechazado"));
Probar("Sin folio ni nombre se rechaza", () => Ver(ExcelCargaService.Leer(Libro("", ""), usuario, catalogo).Errores.Any(e => e.Campo == "NOMBRE_DETENIDO"), "No se detectó identidad vacía"));
Probar("Folio mal formado se conserva para cruce", () => Ver(ExcelCargaService.Leer(Libro("FOLIO-POR-REVISAR", "José Pérez"), usuario, catalogo).Registros.Count == 1, "No permitió el respaldo por nombre"));
foreach(var tipo in new[] { "IA", "AI" }) Probar("Falta administrativa " + tipo, () => Ver(ExcelCargaService.Leer(Libro($"BC/{tipo}/004/05102026/0068", "José Pérez"), usuario, catalogo).Errores.Any(e => e.Campo == "FOLIO_RND"), "Se aceptó falta administrativa"));
Probar("Otra entidad se rechaza", () => Ver(ExcelCargaService.Leer(Libro("", "José Pérez", entidad: "MEXICO"), usuario, catalogo).Errores.Any(e => e.Campo == "ENTIDAD_DETENCION"), "Se mezcló alcance estatal"));
Probar("Varias personas/filas por folio no se eliminan", () => Ver(ExcelCargaService.Leer(Libro("BC/FC/004/05102026/0068", "José Pérez", dos: true), usuario, catalogo).Registros.Count == 2, "Se descartaron filas duplicadas"));
Probar("Accesos separados por entidad e institución", () =>
{
    var u = new UsuarioInfo { Accesos = [new() { Id = 1, Rol = "PRIMER_RESPONDIENTE", Institucion = "SEDENA", Ambito = "ESTATAL", Entidad = 9 }, new() { Id = 2, Rol = "CONSULTA", Ambito = "ESTATAL", Entidad = 15 }] };
    Ver(u.SeleccionarAcceso(1) && u.Entidad == 9 && u.Institucion == "SEDENA", "Contexto incorrecto");
    Ver(u.SeleccionarAcceso(2) && u.Entidad == 15 && u.Institucion == null && u.Rol == "CONSULTA", "Se mezclaron asignaciones");
    Ver(!u.SeleccionarAcceso(999), "Se aceptó acceso ajeno");
});
Probar("Superusuario estatal se rechaza", () =>
{
    try { UsuarioService.ValidarEstructuraAccesos([new() { Rol = "SUPER_USUARIO", Ambito = "ESTATAL", Entidad = 9 }]); }
    catch(ArgumentException) { return; }
    throw new Exception("Se aceptó alcance inválido");
});
Probar("Asignaciones repetidas se rechazan", () =>
{
    try { UsuarioService.ValidarEstructuraAccesos([new() { Rol = "CONSULTA", Ambito = "NACIONAL" }, new() { Rol = "CONSULTA", Ambito = "NACIONAL" }]); }
    catch(ArgumentException) { return; }
    throw new Exception("Se aceptó duplicado");
});
Environment.ExitCode = fallos == 0 ? 0 : 1;
