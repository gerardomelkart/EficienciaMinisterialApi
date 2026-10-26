using System.Text.Json;

namespace EficienciaMinisterialAPI.Models;
public sealed record Opcion(string Codigo, string Nombre);
public sealed record RolConfigurado(string Codigo, string Nombre, string? TipoInstitucion, bool AlcanceTotal);
public sealed record AmbitoConfigurado(string Codigo, string Nombre, bool RequiereEntidad, string? Fuero);
public sealed record RolAmbito(string Rol, string Ambito);
public sealed record PermisoRol(string Rol, string Permiso);
public sealed record InstitucionConfigurada(string Codigo, string Nombre, string Tipo);
public sealed record EntidadConfigurada(byte Id, string Nombre, bool Asignable);
public sealed record AliasConfigurado(string Tipo, string Nombre, string Codigo);
public sealed record CampoCarga(string Codigo, int Posicion, string Encabezado, bool Requerido, int? MaxLongitud, string Regla);
public sealed class ConfiguracionSistema
{
    public Dictionary<string, string> Parametros { get; init; } = [];
    public List<RolConfigurado> Roles { get; init; } = [];
    public List<AmbitoConfigurado> Ambitos { get; init; } = [];
    public List<RolAmbito> RolesAmbitos { get; init; } = [];
    public List<PermisoRol> Permisos { get; init; } = [];
    public List<InstitucionConfigurada> Instituciones { get; init; } = [];
    public List<EntidadConfigurada> Entidades { get; init; } = [];
    public List<Opcion> Fueros { get; init; } = [];
    public List<AliasConfigurado> Alias { get; init; } = [];
    public List<CampoCarga> Campos { get; init; } = [];

    public T Valor<T>(string clave) => Parametros.TryGetValue(clave, out var valor)
        ? JsonSerializer.Deserialize<T>(valor)!
        : throw new InvalidOperationException($"Falta el parámetro de base de datos {clave}. Ejecute 003_configuracion.sql.");

    public void Validar()
    {
        string[] positivos = ["Carga.MaxBytes", "Carga.MaxFilas", "Carga.MaxDescomprimido", "Carga.MaxEntradasZip", "Carga.AnioMinimo", "Carga.AnioMaximo", "Carga.LimiteConsulta", "Usuarios.PasswordMinimo", "Usuarios.PorPagina", "Seguridad.TokenMinutos", "Seguridad.IntentosLogin", "Seguridad.VentanaLoginSegundos"];
        foreach(var clave in positivos)
        {
            if(Valor<long>(clave) <= 0)
            {
                throw new InvalidOperationException($"El parámetro {clave} debe ser positivo.");
            }
        }
        if(Valor<int>("Carga.AnioMinimo") > Valor<int>("Carga.AnioMaximo") || Valor<int>("Carga.AnioMaximo") > DateTime.MaxValue.Year
            || Valor<int>("Usuarios.PasswordMinimo") > 72 || Valor<int>("Seguridad.CostoHash") is < 4 or > 16
            || Valor<string>("Carga.PrefijoFolio").Length > 20 || !Valor<string>("Carga.Extension").Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Los parámetros exceden la capacidad del formato, almacenamiento o algoritmo.");
        }
        _ = new System.Text.RegularExpressions.Regex(Valor<string>("Carga.PatronFolio"), System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        _ = Valor<string>("Carga.FormatoFechaFolio");
        _ = Valor<bool>("Carga.AvisarDuplicados");
        _ = Valor<bool>("Carga.AvisarDiferencias");
        _ = Valor<bool>("Carga.PermitirFormulas");
        string[] codigos = ["CORPORACION", "FOLIO_RND", "FECHA_DETENCION", "AUTORIDAD_EN_DONDE_SE_HIZO_LA_PUESTA", "NOMBRE_DETENIDO", "ENTIDAD_DETENCION", "TIPO_FUERO"];
        if(!Campos.Select(c => c.Codigo).Order().SequenceEqual(codigos.Order())
            || !Campos.Select(c => c.Posicion).SequenceEqual(Enumerable.Range(1, Campos.Count))
            || Campos.Any(c => string.IsNullOrWhiteSpace(c.Encabezado) || c.MaxLongitud <= 0)
            || Roles.Count == 0 || Ambitos.Count == 0 || Instituciones.Count == 0 || Entidades.Count == 0 || Fueros.Count == 0)
        {
            throw new InvalidOperationException("Catálogos o definición de columnas incompletos. Ejecute 003_configuracion.sql.");
        }
    }

    public bool Permite(string rol, string permiso) => Permisos.Any(p => p.Rol == rol && p.Permiso == permiso);
}
