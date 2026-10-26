using Dapper;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Models;

namespace EficienciaMinisterialAPI.Repositories;
// Una instantánea por solicitud; no conserva valores estáticos ni usa catálogos de respaldo.
public sealed class ConfiguracionRepository(SqlConnectionFactory factory)
{
    private Task<ConfiguracionSistema>? actual;
    public Task<ConfiguracionSistema> ObtenerAsync() => actual ??= LeerAsync();

    private async Task<ConfiguracionSistema> LeerAsync()
    {
        await using var db = await factory.AbrirAsync();
        using var resultados = await db.QueryMultipleAsync("""
            SELECT clave, valor FROM configuracion.parametro;
            SELECT codigo, nombre, tipo_institucion AS TipoInstitucion, alcance_total AS AlcanceTotal FROM catalogo.rol WHERE activo=1 ORDER BY nombre;
            SELECT codigo, nombre, requiere_entidad AS RequiereEntidad, fuero FROM catalogo.ambito WHERE activo=1 ORDER BY nombre;
            SELECT rol, ambito FROM catalogo.rol_ambito;
            SELECT rol, permiso FROM seguridad.rol_permiso;
            SELECT codigo, nombre, tipo FROM catalogo.institucion WHERE activo=1 ORDER BY nombre;
            SELECT id, nombre, asignable FROM catalogo.entidad WHERE activo=1 ORDER BY nombre;
            SELECT codigo, nombre FROM catalogo.fuero WHERE activo=1 ORDER BY nombre;
            SELECT tipo, nombre, codigo FROM catalogo.alias;
            SELECT codigo, posicion, encabezado, requerido, max_longitud AS MaxLongitud, regla FROM configuracion.campo_carga ORDER BY posicion;
            """);
        var config = new ConfiguracionSistema
        {
            Parametros = (await resultados.ReadAsync<(string Clave, string Valor)>()).ToDictionary(p => p.Clave, p => p.Valor),
            Roles = (await resultados.ReadAsync<RolConfigurado>()).ToList(),
            Ambitos = (await resultados.ReadAsync<AmbitoConfigurado>()).ToList(),
            RolesAmbitos = (await resultados.ReadAsync<RolAmbito>()).ToList(),
            Permisos = (await resultados.ReadAsync<PermisoRol>()).ToList(),
            Instituciones = (await resultados.ReadAsync<InstitucionConfigurada>()).ToList(),
            Entidades = (await resultados.ReadAsync<EntidadConfigurada>()).ToList(),
            Fueros = (await resultados.ReadAsync<Opcion>()).ToList(),
            Alias = (await resultados.ReadAsync<AliasConfigurado>()).ToList(),
            Campos = (await resultados.ReadAsync<CampoCarga>()).ToList()
        };
        config.Validar();
        return config;
    }
}
