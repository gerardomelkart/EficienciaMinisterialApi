namespace EficienciaMinisterialAPI.Models;

public sealed class FiltrosInformePr
{
    public int? Anio { get; set; }
    public string Orden { get; set; } = "PeriodoInicio";
    public bool Descendente { get; set; } = true;
    public string OrdenResumen { get; set; } = "PeriodoInicio";
    public bool DescendenteResumen { get; set; } = true;
    public static readonly string[] Columnas = ["PeriodoInicio", "FolioEntrega", "Corporacion", "FolioRnd", "FechaDetencion", "NombreDetenido", "Entidad", "EstadoCruce", "Duplicado", "FechaFuente"];
    public static readonly string[] ColumnasResumen = ["PeriodoInicio", "Corporacion", "Entregas", "Registros", "Localizados", "NoLocalizados", "Pendientes", "Duplicados"];
    public DateTime? Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public string? Corporacion { get; set; }
    public byte? Entidad { get; set; }
    public int Pagina { get; set; } = 1;
    public int Tamano { get; set; } = 25;

    public void Validar()
    {
        if(Anio is < 2000 or > 2100 || !Columnas.Contains(Orden) || !ColumnasResumen.Contains(OrdenResumen) || Inicio.HasValue != Fin.HasValue || Inicio > Fin || Pagina < 1 || Pagina > 1_000_000
            || Tamano is < 1 or > 100 || Corporacion?.Length > 20 || Entidad is 0 or > 32)
        {
            throw new ArgumentException("Revise el periodo, corporación, entidad o paginación del reporte.");
        }
    }
}

public class TotalesInformePr
{
    public int Entregas { get; set; }
    public int Registros { get; set; }
    public int Folios { get; set; }
    public int Localizados { get; set; }
    public int NoLocalizados { get; set; }
    public int Pendientes { get; set; }
    public int Duplicados { get; set; }
}
public sealed class ResumenInformePr : TotalesInformePr
{
    public DateTime PeriodoInicio { get; set; }
    public DateTime PeriodoFin { get; set; }
    public string Corporacion { get; set; } = "";
}
public sealed class FilaInformePr
{
    public DateTime PeriodoInicio { get; set; }
    public DateTime PeriodoFin { get; set; }
    public string FolioEntrega { get; set; } = "";
    public string Corporacion { get; set; } = "";
    public string FolioRnd { get; set; } = "";
    public DateTime FechaDetencion { get; set; }
    public string NombreDetenido { get; set; } = "";
    public string Entidad { get; set; } = "";
    public string AutoridadPuesta { get; set; } = "";
    public string Fuero { get; set; } = "";
    public string EstadoCruce { get; set; } = "";
    public bool Duplicado { get; set; }
    public DateTime? FechaFuente { get; set; }
}
public sealed record InformePr(TotalesInformePr Totales, List<ResumenInformePr> Resumen, List<FilaInformePr> Registros, int Pagina, int Tamano);
