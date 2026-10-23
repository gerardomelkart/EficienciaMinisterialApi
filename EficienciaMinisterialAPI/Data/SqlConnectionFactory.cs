using Microsoft.Data.SqlClient;

namespace EficienciaMinisterialAPI.Data;
public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    public async Task<SqlConnection> AbrirAsync()
    {
        var cadena = configuration.GetConnectionString("EficienciaMinisterial");
        if(string.IsNullOrWhiteSpace(cadena))
        {
            throw new InvalidOperationException("Configure ConnectionStrings__EficienciaMinisterial.");
        }
        var conexion = new SqlConnection(cadena);
        try
        {
            await conexion.OpenAsync();
            return conexion;
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }
    }
}
