using Dapper;
using EficienciaMinisterialAPI.Data;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CatalogoRepository(SqlConnectionFactory factory)
{
    public async Task<object> ObtenerUsuariosAsync()
    {
        await using var db = await factory.AbrirAsync();
        var entidades = await db.QueryAsync("SELECT id, nombre FROM catalogo.entidad WHERE activo = 1 AND id BETWEEN 1 AND 32 ORDER BY nombre");
        var instituciones = await db.QueryAsync("SELECT codigo, nombre, tipo FROM catalogo.institucion WHERE activo = 1 ORDER BY nombre");
        return new { entidades, instituciones };
    }

    public async Task<Dictionary<string, byte>> ObtenerEntidadesAsync()
    {
        await using var db = await factory.AbrirAsync();
        var catalogo = await db.QueryAsync<(byte Id, string Nombre)>("SELECT id, nombre FROM catalogo.entidad WHERE activo = 1 AND id BETWEEN 1 AND 32");
        return catalogo.ToDictionary(e => e.Nombre, e => e.Id);
    }
}
