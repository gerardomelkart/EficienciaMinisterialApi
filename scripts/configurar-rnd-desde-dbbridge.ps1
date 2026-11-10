param(
    [string]$RutaDBBridge = 'D:\CNI\DBBridge'
)

$ErrorActionPreference = 'Stop'
$archivo = Join-Path $RutaDBBridge 'src\DBBridge\SqlServer\SqlConnections.cs'
if (!(Test-Path -LiteralPath $archivo)) {
    throw "No se encontró $archivo. Indique la carpeta del repositorio con -RutaDBBridge."
}

$texto = Get-Content -LiteralPath $archivo -Raw -Encoding UTF8
$patron = '(?s)CreateDestination\(\).*?return\s+Create\("(?<servidor>[^"\r\n]+)",\s*"(?<base>[^"\r\n]+)",\s*"(?<usuario>[^"\r\n]+)",\s*"(?<password>[^"\r\n]+)"'
$destino = [regex]::Match($texto, $patron)
if (!$destino.Success -or $destino.Groups['base'].Value -ne 'RND') {
    throw 'No se pudo identificar CreateDestination de RND en DBBridge. No se modificaron UserSecrets.'
}

$cadena = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$cadena['Data Source'] = $destino.Groups['servidor'].Value
$cadena['Initial Catalog'] = $destino.Groups['base'].Value
$cadena['User ID'] = $destino.Groups['usuario'].Value
$cadena['Password'] = $destino.Groups['password'].Value
$cadena['Encrypt'] = $false
$cadena['TrustServerCertificate'] = $true
$cadena['Connect Timeout'] = 30
$cadena['Application Name'] = 'EficienciaMinisterial cruce RND'
$proyecto = Join-Path $PSScriptRoot '..\EficienciaMinisterialAPI\EficienciaMinisterialAPI.csproj'

$valorConexion = $cadena.get_ConnectionString()
& dotnet user-secrets set 'ConnectionStrings:RndDestino' $valorConexion --project $proyecto
if ($LASTEXITCODE -ne 0) {
    throw 'No fue posible guardar la conexión en UserSecrets.'
}
Write-Host 'Conexión destino RND configurada. Reinicie la API. La contraseña no se escribió en el repositorio.'
