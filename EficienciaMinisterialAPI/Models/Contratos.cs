using System.ComponentModel.DataAnnotations;

namespace EficienciaMinisterialAPI.Models;
public sealed record LoginRequest([Required, MaxLength(80)] string Usuario, [Required, MaxLength(200)] string Password);
public sealed record CrearUsuarioRequest(
    [Required, MaxLength(80)] string Usuario,
    [Required, MaxLength(200)] string Nombre,
    [Required, MinLength(12), MaxLength(72)] string Password,
    [Required] string Rol,
    string? Institucion,
    [Required] string Ambito,
    byte? Entidad,
    List<AccesoUsuario>? Accesos = null);
public sealed record ActualizarUsuarioRequest(
    [Required, MaxLength(80)] string Usuario,
    [Required, MaxLength(200)] string Nombre,
    bool Activo,
    [Required, MinLength(1)] List<AccesoUsuario> Accesos);
public sealed record SeleccionarAccesoRequest(int IdAcceso);
public sealed class AccesoUsuario
{
    public int Id { get; set; }
    public string Rol { get; set; } = "";
    public string? Institucion { get; set; }
    public string Ambito { get; set; } = "";
    public byte? Entidad { get; set; }
}
public sealed record ConfirmarCargaRequest(Guid IdCarga);
public sealed record ErrorFila(int Fila, string Campo, string Mensaje);
public sealed record RegistroCarga(int Fila, string Corporacion, string FolioRnd, DateTime FechaDetencion,
                                    string AutoridadPuesta, string NombreDetenido, byte Entidad, string TipoFuero);
public sealed class UsuarioInfo
{
    public bool Activo { get; set; } = true;
    public int IdAcceso { get; set; }
    public List<AccesoUsuario> Accesos { get; set; } = [];

    public bool SeleccionarAcceso(int? id)
    {
        var acceso = id.HasValue ? Accesos.Find(a => a.Id == id) : Accesos.FirstOrDefault();
        if(acceso == null)
        {
            return false;
        }
        IdAcceso = acceso.Id;
        Rol = acceso.Rol;
        Institucion = acceso.Institucion;
        Ambito = acceso.Ambito;
        Entidad = acceso.Entidad;
        return true;
    }

    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public string? Institucion { get; set; }
    public string Ambito { get; set; } = "";
    public byte? Entidad { get; set; }
}
public sealed class UsuarioAutenticacion
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = "";
}
public sealed class CargaInfo
{
    public Guid Id { get; set; }
    public string? Folio { get; set; }
    public string Institucion { get; set; } = "";
    public string NombreArchivo { get; set; } = "";
    public DateTime PeriodoInicio { get; set; }
    public DateTime PeriodoFin { get; set; }
    public string Estado { get; set; } = "";
    public int TotalRegistros { get; set; }
    public DateTime CreadoUtc { get; set; }
    public DateTime? ConfirmadoUtc { get; set; }
}
