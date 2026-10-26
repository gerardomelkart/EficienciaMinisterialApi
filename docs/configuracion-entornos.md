# Configuración de desarrollo y producción

## Entornos y base de datos

| Entorno | API | Base SQL Server |
|---|---|---|
| Desarrollo actual | Local, Visual Studio o dotnet run | `EficienciaMinisterial_DEV`, en el servidor de SQL designado para desarrollo. |
| Producción futura | Servidor de aplicaciones, al publicar | `EficienciaMinisterial`, base independiente que se creará al desplegar. |

No se usa LocalDB. No se comparte la base SIIID2_QA: únicamente se reutiliza su servidor SQL. El nombre de la base no depende de ASPNETCORE_ENVIRONMENT: lo determina la cadena configurada. Production nunca debe apuntar a DEV.

El responsable crea la base DEV y ejecuta `001_base.sql` y `002_catalogos.sql` del repositorio de BD. Los scripts iniciales apuntan a `EficienciaMinisterial_DEV`; si la base ya existe, `001_base.sql` completa sus objetos. La nueva cuenta debe estar mapeada en esa base y contar con los permisos descritos en el README de BD. Que un login funcione en SIIID2_QA no implica permisos sobre la nueva base ni sobre sus esquemas `seguridad`, `catalogo` y `primer_respondiente`.

## Ruta de desarrollo actual

El proyecto de la API está en:

```text
D:\CNI\EficienciaMinisterial\EficienciaMinisterialApi\EficienciaMinisterialAPI
```

El front está en `D:\CNI\EficienciaMinisterial\PrimerRespondienteFront`.

## Configurar la cadena sin guardarla en Git

En PowerShell, desde la carpeta del `.csproj`, guardar la conexión mediante User Secrets. Reemplazar el servidor y escribir la contraseña únicamente en el diálogo local:

```powershell
cd D:\CNI\EficienciaMinisterial\EficienciaMinisterialApi\EficienciaMinisterialAPI

$servidorSql = Read-Host 'Servidor SQL (host,puerto)'
$credencialSql = Get-Credential -Message 'Credenciales SQL de desarrollo'
$conexionSql = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$conexionSql['Data Source'] = $servidorSql
$conexionSql['Initial Catalog'] = 'EficienciaMinisterial_DEV'
$conexionSql['User ID'] = $credencialSql.UserName
$conexionSql['Password'] = $credencialSql.GetNetworkCredential().Password
$conexionSql['Encrypt'] = $true
$conexionSql['TrustServerCertificate'] = $false

dotnet user-secrets set 'ConnectionStrings:EficienciaMinisterial' $conexionSql.ConnectionString
Remove-Variable conexionSql, credencialSql
```

Esto requiere que el certificado SQL sea válido y confiable para el equipo. La conexión debe configurarse en el usuario de Windows que ejecuta Visual Studio. No imprimir ni compartir `dotnet user-secrets list`, ya que muestra valores secretos.

La clave lógica permanece `ConnectionStrings:EficienciaMinisterial` en ambos entornos, aunque la base DEV tenga sufijo `_DEV`. User Secrets se carga al ejecutar en Development; el perfil HTTP del repo ya usa ese entorno y `http://127.0.0.1:5010`. Se pueden editar los mismos valores desde Visual Studio, clic derecho sobre el proyecto y Administrar secretos de usuario.

## Clave JWT local

Se genera una vez por entorno. No hace falta regenerarla en cada arranque:

```powershell
$bytesJwt = New-Object byte[] 48
$generadorJwt = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$generadorJwt.GetBytes($bytesJwt)
$generadorJwt.Dispose()
dotnet user-secrets set 'Jwt:Key' ([Convert]::ToBase64String($bytesJwt))
Remove-Variable bytesJwt
```

## Crear el primer superusuario

Después de crear la BD, ejecutar los scripts y configurar la conexión:

```powershell
$cuentaInicial = Get-Credential -Message 'Nuevo superusuario de Eficiencia Ministerial (contraseña de 12 caracteres o más)'
$env:EM_ADMIN_USUARIO = $cuentaInicial.UserName
$env:EM_ADMIN_PASSWORD = $cuentaInicial.GetNetworkCredential().Password
try
{
    dotnet run -- --crear-superusuario
}
finally
{
    Remove-Item Env:EM_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:EM_ADMIN_USUARIO -ErrorAction SilentlyContinue
    Remove-Variable cuentaInicial
}
```

Esta cuenta es una cuenta de la aplicación, distinta del login de SQL Server. El comando crea la cuenta y termina. Ejecutar después `dotnet run` o iniciar desde Visual Studio para abrir la API. No hay usuarios o contraseñas predeterminados.

## Logs de la API

Se escribe tanto en consola como en archivos diarios con Serilog. Carpeta predeterminada: `logs`, relativa al Content root del proyecto. En la ruta de desarrollo actual:

```text
D:\CNI\EficienciaMinisterial\EficienciaMinisterialApi\EficienciaMinisterialAPI\logs
```

Archivos `eficiencia-ministerial-AAAAMMDD.log`; rotación diaria y al alcanzar 10 MB, conservando hasta 31 archivos. Puede haber varios archivos el mismo día. El servicio necesita permiso de escritura en esa carpeta.

Registran arranque, peticiones HTTP (método, ruta, estado y duración), inicios de sesión correctos/rechazados y errores, con TraceId para correlación. No se registra el cuerpo de las solicitudes, contraseñas, tokens, cabeceras Authorization ni cadenas de conexión. Las marcas de tiempo incluyen su offset. Evitar incluir datos personales o secretos en las URL.

Para cambiar la carpeta local:

```powershell
dotnet user-secrets set 'Logging:Directorio' 'D:\CNI\EficienciaMinisterial\LogsApi'
```

Reiniciar la API para aplicar cambios. `.gitignore` excluye `logs/` y `*.log`; los archivos de bitácora no se publican en GitHub. El registro en archivos complementa la bitácora de operaciones en SQL.

## Publicación futura

No se crea ni se configura producción en esta entrega. Al desplegar, crear la base independiente `EficienciaMinisterial`, preparar los scripts para ese destino y configurar el servicio con un archivo de entorno protegido, fuera del repo y de la carpeta publicada:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__EficienciaMinisterial=Server=SQL_HOST,1433;Database=EficienciaMinisterial;User Id=SQL_USER;Password=REEMPLAZAR;Encrypt=True;TrustServerCertificate=False
Jwt__Key=CLAVE_ALEATORIA_DISTINTA_A_DEV
Logging__Directorio=/var/log/eficiencia-ministerial-api
```

Los valores anteriores son marcadores de ejemplo. Completar únicamente en el servidor y asignar lectura al usuario que ejecuta el servicio. Verificar permisos SQL y de escritura en logs antes de iniciar. User Secrets no se copia al publicar: la configuración del servidor se conserva fuera de la publicación. La clave JWT de producción debe ser distinta de la de desarrollo.

Para quien revise este repo público: no se requieren credenciales reales para compilar. Para probar el flujo completo, crear una base de pruebas propia, instalar los scripts y proporcionar sus secretos mediante estos mecanismos. No subir archivos `.env`, credenciales reales ni logs al repositorio.

## Configuración desde base de datos

Esta entrega requiere aplicar `003_configuracion.sql` del repositorio de BD antes de actualizar API y front. En una base existente no repetir la creación ni eliminar datos. Los catálogos, permisos y parámetros operativos se obtienen de SQL; ya no existen listas de instituciones, roles o ámbitos en el formulario. Ver `docs/configuracion.md` en EficienciaMinisterialBD para tablas, parámetros, permisos y orden de actualización. Conexión y claves permanecen en User Secrets o entorno.
