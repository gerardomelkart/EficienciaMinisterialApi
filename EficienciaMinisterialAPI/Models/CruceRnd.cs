namespace EficienciaMinisterialAPI.Models;

public sealed record CoincidenciaRnd(string Folio, string IdDetenido, string IdDetencion,
    string? IdPuesta, string NombrePr, string NombreMp, DateTime? FechaDetencion,
    string Entidad, string Fiscalia, string Fuero);
public sealed record ResultadoCruceRnd(string Tabla, DateTime FechaFuente,
    Dictionary<int, List<CoincidenciaRnd>> Filas);
