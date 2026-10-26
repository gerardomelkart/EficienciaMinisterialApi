using Dapper;
using EficienciaMinisterialAPI.Data;

namespace EficienciaMinisterialAPI.Repositories;
public sealed class CatalogoRepository(SqlConnectionFactory factory)
{
    public async Task<Dictionary<string, byte>> ObtenerEntidadesAsync()
    {
        await using var db = await factory.AbrirAsync();
        var catalogo = await db.QueryAsync<(byte Id, string Nombre)>("SELECT id, nombre FROM catalogo.entidad");
        return catalogo.ToDictionary(e => e.Nombre, e => e.Id);
    }
}
