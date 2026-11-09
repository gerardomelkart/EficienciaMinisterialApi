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
// Datos sintéticos exclusivos de estas pruebas. La aplicación obtiene los catálogos desde SQL.
var catalogo = new CatalogoCaptura
{
    Entidades = new() { ["CIUDAD DE MEXICO"] = 9, ["CDMX"] = 9, ["MEXICO"] = 15 },
    Corporaciones = new() { ["SEDENA"] = "SEDENA", ["DEFENSA"] = "SEDENA", ["SEMAR"] = "SEMAR" },
    Fueros = new() { ["FC"] = "FC", ["Fuero común"] = "FC", ["FF"] = "FF", ["Fuero federal"] = "FF" }
};
var perfiles = new List<PerfilInfo>
{
    new() { Codigo = "SUPER_USUARIO" },
    new() { Codigo = "CONSULTA", PermiteEstatal = true },
    new() { Codigo = "PRIMER_RESPONDIENTE", PermiteEstatal = true, RequiereInstitucion = true }
};
var ambitos = new List<AmbitoInfo>
{
    new() { Codigo = "NACIONAL" }, new() { Codigo = "ESTATAL", RequiereEntidad = true }
};
(List<RegistroCarga> Registros, List<ErrorFila> Errores, List<ErrorFila> Avisos) Leer(byte[] bytes, UsuarioInfo cuenta, CatalogoCaptura opciones)
{
    return ExcelCargaService.Leer(bytes, cuenta, opciones, 20000);
}
byte[] CambiarCelda(byte[] bytes, int fila, int columna, string valor)
{
    using var memoria = new MemoryStream(bytes);
    using var libro = new XLWorkbook(memoria);
    libro.Worksheet(1).Cell(fila, columna).Value = valor;
    using var salida = new MemoryStream();
    libro.SaveAs(salida);
    return salida.ToArray();
}
byte[] Libro(string folio, string nombre, string autoridad = "", string fuero = "", string entidad = "CDMX", bool dos = false, DateTime? fecha = null)
{
    using var libro = new XLWorkbook(); var hoja = libro.AddWorksheet("PLANTILLA");
    for(int c = 0; c < 7; c++) hoja.Cell(1, c + 1).Value = ExcelCargaService.Encabezados[c];
    for(int f = 2; f <= (dos ? 3 : 2); f++)
    {
        hoja.Cell(f, 1).Value = "SEDENA"; hoja.Cell(f, 2).Value = folio;
        hoja.Cell(f, 3).Value = (fecha ?? new DateTime(2026, 10, 5)); hoja.Cell(f, 4).Value = autoridad;
        hoja.Cell(f, 5).Value = dos ? nombre + " " + f : nombre; hoja.Cell(f, 6).Value = entidad; hoja.Cell(f, 7).Value = fuero;
    }
    using var m = new MemoryStream(); libro.SaveAs(m); return m.ToArray();
}
Probar("Nombre normalizado", () => Ver(ExcelCargaService.Normalizar("  José   PéREZ \t López ") == "JOSE PEREZ LOPEZ", "Acentos o espacios no normalizados"));
Probar("Sin folio con nombre y opcionales vacíos", () => Ver(Leer(Libro("", "José Pérez"), usuario, catalogo).Errores.Count == 0, "Campos opcionales rechazados"));
Probar("Folio sin nombre", () => Ver(Leer(Libro("BC/FC/004/05102026/0068", ""), usuario, catalogo).Errores.Count == 0, "Folio válido rechazado"));
Probar("Sin folio ni nombre se rechaza", () => Ver(Leer(Libro("", ""), usuario, catalogo).Errores.Any(e => e.Campo == "NOMBRE_DETENIDO"), "No se detectó identidad vacía"));
Probar("Folio mal formado se conserva para cruce", () => Ver(Leer(Libro("FOLIO-POR-REVISAR", "José Pérez"), usuario, catalogo).Registros.Count == 1, "No permitió el respaldo por nombre"));
foreach(var tipo in new[] { "IA", "AI" }) Probar("Falta administrativa " + tipo, () => Ver(Leer(Libro($"BC/{tipo}/004/05102026/0068", "José Pérez"), usuario, catalogo).Errores.Any(e => e.Campo == "FOLIO_RND"), "Se aceptó falta administrativa"));
Probar("Otra entidad se rechaza", () => Ver(Leer(Libro("", "José Pérez", entidad: "MEXICO"), usuario, catalogo).Errores.Any(e => e.Campo == "ENTIDAD_DETENCION"), "Se mezcló alcance estatal"));
Probar("Varias personas/filas por folio no se eliminan", () => Ver(Leer(Libro("BC/FC/004/05102026/0068", "José Pérez", dos: true), usuario, catalogo).Registros.Count == 2, "Se descartaron filas duplicadas"));
Probar("Accesos separados por entidad e institución", () =>
{
    var u = new UsuarioInfo { Accesos = [new() { Id = 1, Rol = "PRIMER_RESPONDIENTE", Institucion = "SEDENA", Ambito = "ESTATAL", Entidad = 9 }, new() { Id = 2, Rol = "CONSULTA", Ambito = "ESTATAL", Entidad = 15 }] };
    Ver(u.SeleccionarAcceso(1) && u.Entidad == 9 && u.Institucion == "SEDENA", "Contexto incorrecto");
    Ver(u.SeleccionarAcceso(2) && u.Entidad == 15 && u.Institucion == null && u.Rol == "CONSULTA", "Se mezclaron asignaciones");
    Ver(!u.SeleccionarAcceso(999), "Se aceptó acceso ajeno");
});
Probar("Superusuario estatal se rechaza", () =>
{
    try { UsuarioService.ValidarEstructuraAccesos([new() { Rol = "SUPER_USUARIO", Ambito = "ESTATAL", Entidad = 9 }], perfiles, ambitos); }
    catch(ArgumentException) { return; }
    throw new Exception("Se aceptó alcance inválido");
});
Probar("Asignaciones repetidas se rechazan", () =>
{
    try { UsuarioService.ValidarEstructuraAccesos([new() { Rol = "CONSULTA", Ambito = "NACIONAL" }, new() { Rol = "CONSULTA", Ambito = "NACIONAL" }], perfiles, ambitos); }
    catch(ArgumentException) { return; }
    throw new Exception("Se aceptó duplicado");
});
Probar("Se rechaza otra corporación aunque esté en el catálogo", () =>
{
    var bytes = CambiarCelda(Libro("", "Persona de prueba"), 2, 1, "SEMAR");
    Ver(Leer(bytes, usuario, catalogo).Errores.Any(e => e.Campo == "CORPORACION"), "Se aceptó una corporación ajena");
});
Probar("Se rechaza corporación ausente del catálogo", () =>
{
    var bytes = CambiarCelda(Libro("", "Persona de prueba"), 2, 1, "DESCONOCIDA");
    Ver(Leer(bytes, usuario, catalogo).Errores.Any(e => e.Campo == "CORPORACION"), "Se aceptó una corporación desconocida");
});
Probar("Las equivalencias vienen del catálogo recibido", () =>
{
    var bytes = CambiarCelda(Libro("", "Persona de prueba"), 2, 1, "DEFENSA");
    Ver(Leer(bytes, usuario, catalogo).Registros.Single().Corporacion == "SEDENA", "No se aplicó el alias del catálogo");
});
Probar("No hay equivalencias implícitas fuera del catálogo", () =>
{
    var sinAlias = new CatalogoCaptura { Entidades = catalogo.Entidades, Corporaciones = new() { ["SEDENA"] = "SEDENA" }, Fueros = catalogo.Fueros };
    var bytes = CambiarCelda(Libro("", "Persona de prueba"), 2, 1, "SECRETARIA DE LA DEFENSA NACIONAL");
    Ver(Leer(bytes, usuario, sinAlias).Errores.Any(e => e.Campo == "CORPORACION"), "Persistió un alias fijo en el código");
});
Probar("El alcance nacional admite entidades distintas", () =>
{
    var nacional = new UsuarioInfo { Rol = "PRIMER_RESPONDIENTE", Institucion = "SEDENA", Ambito = "NACIONAL" };
    Ver(Leer(Libro("", "Persona de prueba", entidad: "MEXICO"), nacional, catalogo).Errores.Count == 0, "Se restringió indebidamente el alcance nacional");
});
Probar("Una fila ajena produce error aunque las demás sean válidas", () =>
{
    var bytes = CambiarCelda(Libro("", "Persona de prueba", dos: true), 3, 6, "MEXICO");
    Ver(Leer(bytes, usuario, catalogo).Errores.Any(e => e.Fila == 3 && e.Campo == "ENTIDAD_DETENCION"), "Se omitió el alcance en una fila posterior");
});
Probar("Fuero debe estar en el catálogo recibido", () =>
{
    var sinFuero = new CatalogoCaptura { Entidades = catalogo.Entidades, Corporaciones = catalogo.Corporaciones, Fueros = [] };
    Ver(Leer(Libro("", "Persona de prueba", fuero: "Fuero común"), usuario, sinFuero).Errores.Any(e => e.Campo == "TIPO_FUERO"), "Persistió un fuero fijo en el código");
});
Probar("Límite de filas configurado", () =>
{
    Ver(ExcelCargaService.Leer(Libro("", "Persona de prueba", dos: true), usuario, catalogo, 1).Errores.Any(e => e.Campo == "ARCHIVO"), "Se ignoró el límite configurado");
});
Probar("Etiqueta del periodo usa sus fechas reales", () =>
{
    Ver(new PeriodoInfo { Inicio = new DateTime(2084, 2, 16), Fin = new DateTime(2084, 2, 29) }.Nombre == "Segunda quincena de febrero 2084", "El periodo depende de una lista fija");
});
foreach(var fecha in new[] { new DateTime(2026, 9, 30), new DateTime(2025, 6, 1), new DateTime(2026, 10, 1), new DateTime(2026, 10, 15) })
{
    Probar("El periodo de entrega permite fechas anteriores: " + fecha.ToString("yyyy-MM-dd"), () =>
    {
        var resultado = Leer(Libro("", "Persona de prueba", fecha: fecha), usuario, catalogo);
        Ver(resultado.Errores.Count == 0 && resultado.Registros.Count == 1, "Se rechazó una fecha anterior permitida por el cuestionario");
    });
}
Probar("Excel de validación incluye errores y observaciones completos", () =>
{
    var filas = Enumerable.Range(1, 45).Select(i => new IncidenciaValidacion(i % 2 == 0 ? "Error" : "Observación", i + 1, "CORPORACION", "Detalle " + i)).ToList();
    filas.Add(new("Observación", null, "", "Cruce con RND pendiente"));
    using var memoria = new MemoryStream(ExcelCargaService.ExportarResultados(filas));
    using var libro = new XLWorkbook(memoria);
    var hoja = libro.Worksheet(1);
    Ver(hoja.LastRowUsed()!.RowNumber() == filas.Count + 1, "El Excel contiene solo una página");
    Ver(hoja.Cell(2, 1).GetString() == "Observación" && hoja.Cell(3, 1).GetString() == "Error", "No se incluyeron ambos tipos");
    Ver(hoja.Cell(2, 2).GetValue<int>() == 2 && hoja.Cell(47, 2).IsEmpty(), "Se alteraron las filas o el aviso general");
    Ver(hoja.Cell(47, 5).GetString() == "Cruce con RND pendiente", "Se perdió el aviso general");
});
Probar("Texto del reporte no se convierte en fórmula", () =>
{
    using var memoria = new MemoryStream(ExcelCargaService.ExportarResultados([new("Error", 2, "CAMPO", "=1+1")]));
    using var libro = new XLWorkbook(memoria);
    Ver(!libro.Worksheet(1).Cell(2, 5).HasFormula && libro.Worksheet(1).Cell(2, 5).GetString() == "=1+1", "Se interpretó texto como fórmula");
});
Probar("Error conserva el valor original sin normalizar", () =>
{
    var bytes = CambiarCelda(Libro("", "Persona de prueba"), 2, 7, "  FUERON COMUN  ");
    Ver(Leer(bytes, usuario, catalogo).Errores.Single(e => e.Campo == "TIPO_FUERO").Valor == "  FUERON COMUN  ", "Se perdió el valor original");
});
Probar("Observación conserva campo y folio original", () =>
{
    var resultado = Leer(Libro("folio a revisar", "Persona de prueba"), usuario, catalogo);
    Ver(resultado.Avisos.Any(a => a.Fila == 2 && a.Campo == "FOLIO_RND" && a.Valor == "folio a revisar"), "Se perdió el contexto del aviso");
});
Probar("Excel exporta el valor original como texto", () =>
{
    using var memoria = new MemoryStream(ExcelCargaService.ExportarResultados([new("Error", 84, "TIPO_FUERO", "No reconocido", "FUERON COMUN"), new("Error", 85, "FOLIO_RND", "Revisar", "=1+1")]));
    using var libro = new XLWorkbook(memoria);
    Ver(libro.Worksheet(1).Cell(2, 4).GetString() == "FUERON COMUN", "Falta el valor original");
    Ver(!libro.Worksheet(1).Cell(3, 4).HasFormula, "Se interpretó el valor como fórmula");
});
Environment.ExitCode = fallos == 0 ? 0 : 1;
