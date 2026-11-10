namespace EficienciaMinisterialAPI.Models;

public sealed record CoincidenciaRnd(string Folio, string IdDetenido, string IdDetencion,
    string? IdPuesta, string NombrePr, string NombreMp, DateTime? FechaDetencion,
    string Entidad, string Fiscalia, string Fuero, string Metodo = "");
public sealed record ResultadoCruceRnd(string Tabla, DateTime FechaFuente,
    Dictionary<int, List<CoincidenciaRnd>> Filas)
{
    public Dictionary<int, List<CoincidenciaRnd>> Administrativas { get; init; } = [];
    public string? TablaFa { get; init; }
}
