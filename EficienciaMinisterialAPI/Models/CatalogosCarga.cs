using System.Globalization;

namespace EficienciaMinisterialAPI.Models;
public sealed class PeriodoInfo
{
    public int Id { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public string Nombre => $"{(Inicio.Day == 1 ? "Primera" : "Segunda")} quincena de {Inicio.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-MX"))}";
}
public sealed class PerfilInfo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? TipoInstitucion { get; set; }
    public bool RequiereInstitucion { get; set; }
    public bool PermiteEstatal { get; set; }
}
public sealed class AmbitoInfo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public bool RequiereEntidad { get; set; }
}
public sealed class CatalogoCaptura
{
    public Dictionary<string, byte> Entidades { get; set; } = [];
    public Dictionary<string, string> Corporaciones { get; set; } = [];
    public Dictionary<string, string> Fueros { get; set; } = [];
}
public sealed class LimitesCarga
{
    public long MaximoBytes { get; set; }
    public int MaximoFilas { get; set; }
    public long MaximoDescomprimidoBytes { get; set; }
    public int MaximoEntradasZip { get; set; }
}
