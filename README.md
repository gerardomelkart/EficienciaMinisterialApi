# Eficiencia Ministerial API

API central del proyecto, desarrollada en C# con .NET 10. Este repositorio contiene los controladores, servicios, acceso SQL y configuración de autenticación y logs de la primera etapa.

## Proyecto Eficiencia Ministerial

Eficiencia Ministerial es una plataforma en desarrollo para recibir información de detenciones y, en una etapa posterior, incorporar el seguimiento ministerial de las fiscalías. Su arquitectura contempla una API central y una base propia, con **Primer Respondiente** como primera interfaz de captura.

Esta versión reúne la base técnica y el diseño inicial. Las reglas operativas definitivas están pendientes de respuesta del área responsable; las funcionalidades provisionales no deben interpretarse como un proceso productivo terminado.

## Repositorios relacionados

| Repositorio | Responsabilidad | Tecnología |
| --- | --- | --- |
| [PrimerRespondienteFront](https://github.com/gerardomelkart/PrimerRespondienteFront) | Interfaz de acceso, captura por Excel, navegación e informes provisionales. | Angular, TypeScript, Bootstrap |
| [EficienciaMinisterialApi](https://github.com/gerardomelkart/EficienciaMinisterialApi) | Autenticación, autorización, validaciones, recepción de archivos y acceso a datos. | C#, ASP.NET Core, .NET 10 |
| [EficienciaMinisterialBD](https://github.com/gerardomelkart/EficienciaMinisterialBD) | Esquemas, tablas, catálogos, índices y secuencia de folios. | SQL Server 2019 o superior |

El navegador se comunica por HTTP con la API; la API consulta y persiste en SQL Server. **El front no se conecta directamente a la base.** Para ejecutar el sistema completo se requieren los tres repositorios.

## Flujo y alcance

El flujo implementado para las corporaciones permite descargar una plantilla, cargar un Excel, revisar errores y observaciones y confirmar una entrega para obtener un folio y un acuse HTML imprimible. El archivo original se conserva junto con los registros aceptados.

El flujo previsto para fiscalías consiste en descargar una plantilla con información de referencia y completar los campos de seguimiento ministerial. Esa segunda etapa todavía no está implementada.

- **Disponible:** autenticación JWT, perfiles iniciales, alta y listado de usuarios, carga y validación de Excel, confirmación con folio, acuse y registros de operación.
- **Provisional:** pantallas de informes “Reporte primer respondiente” y “Reporte completo”, sin consultas ni indicadores definitivos.
- **Pendiente:** catálogos y reglas definitivas, fiscalías estatales, plantilla de seguimiento, cruces automáticos, edición/baja de usuarios y reportes operativos.

Las futuras conexiones con SIIID e Ingesta de Datos no están implementadas. El cruce con RND/RND_FA, a partir de las copias generadas por DBBridge, también está pendiente. No existe actualmente un integrador entre estos sistemas.

## Puesta en marcha del conjunto

1. Preparar una instancia SQL Server de pruebas y ejecutar los scripts del repositorio de BD.
2. Configurar la conexión y la clave JWT fuera del código, crear el superusuario e iniciar la API.
3. Instalar las dependencias del front e iniciar Angular con su proxy local.

Cada repositorio detalla sus pasos más abajo. No se incluyen credenciales predeterminadas ni datos personales de ejemplo; las demostraciones deben utilizar datos ficticios.

## Contenido de este repositorio

- `EficienciaMinisterialAPI/Controllers`: endpoints de autenticación, usuarios y cargas.
- `EficienciaMinisterialAPI/Services`: autenticación y lectura/validación de Excel.
- `EficienciaMinisterialAPI/Repositories`: consultas y persistencia SQL con Dapper.
- `EficienciaMinisterialAPI/Data`: creación de conexiones SQL.
- `docs/configuracion-entornos.md`: guía de conexión, JWT, usuario inicial y logs.

Consultar la [guía de entornos, conexión, usuario inicial y logs](docs/configuracion-entornos.md). Desarrollo utiliza `EficienciaMinisterial_DEV`; producción tendrá una base independiente al publicarse.

## Preparación local

1. Instalar SDK .NET 10 y ejecutar los scripts `001_base.sql` y `002_catalogos.sql` del repositorio de BD.
2. Desde `EficienciaMinisterialAPI`, configurar los secretos locales. No enviar ni guardar credenciales en Git:

```powershell
dotnet user-secrets set "ConnectionStrings:EficienciaMinisterial" "Server=SERVIDOR,1433;Database=EficienciaMinisterial_DEV;User Id=USUARIO;Password=CONTRASEÑA;Encrypt=True;TrustServerCertificate=False"
dotnet user-secrets set "Jwt:Key" "CLAVE_ALEATORIA_DE_AL_MENOS_32_BYTES"
```

También se admiten las variables `ConnectionStrings__EficienciaMinisterial` y `Jwt__Key`. En servidores usar variables de entorno protegidas y TLS en el proxy. El proxy local debe enviar `X-Forwarded-For` y `X-Forwarded-Proto`; solo se confían proxies de loopback. El servicio escucha en loopback; no necesita CORS si el proxy publica front y API bajo el mismo origen. No hay contraseñas predeterminadas ni usuarios de demostración.

3. Crear el primer superusuario desde una terminal local con acceso a la BD. Definir `EM_ADMIN_USUARIO` y `EM_ADMIN_PASSWORD` en el entorno; la contraseña requiere al menos 12 caracteres y como máximo 72 bytes UTF-8. Ejecutar:

```powershell
dotnet run -- --crear-superusuario
```

Eliminar `EM_ADMIN_PASSWORD` del entorno al terminar. El proceso finaliza después del alta; ejecutar `dotnet run` para iniciar la API en `http://127.0.0.1:5010`. El comando de provisionamiento no requiere `Jwt__Key`, sí la conexión SQL. En Development lee user-secrets; en producción usar entorno.

## Operaciones

| Método y ruta | Uso |
|---|---|
| GET `/api/estado` | Estado del proceso, sin comprobar SQL. |
| POST `/api/auth/login` | Usuario y contraseña; token de una hora. |
| GET `/api/auth/me` | Perfil activo actual. |
| GET / POST `/api/usuarios` | Listado y creación; solo superusuario. |
| GET `/api/primer-respondiente/plantilla` | Excel de siete columnas y diccionario. |
| POST `/api/primer-respondiente/cargas/validar` | Multipart: archivo, periodoInicio y periodoFin en formato ISO. |
| POST `/api/primer-respondiente/cargas/confirmar` | JSON `{ "idCarga": "UUID" }`. Confirmación idempotente por usuario. |
| GET `/api/primer-respondiente/cargas` | Últimas 200 entregas confirmadas visibles. |
| GET `/api/primer-respondiente/cargas/{id}` | Datos de la entrega visible. |
| GET `/api/primer-respondiente/cargas/{id}/archivo` | Archivo original. |
| GET `/api/primer-respondiente/cargas/{id}/acuse` | Acuse HTML descargable, imprimible o guardable como PDF desde el navegador. |

La validación no genera un folio. Guarda el archivo y las filas solo si todo el archivo es válido. La confirmación asigna un folio `EM-PR-AÑO-SECUENCIA` dentro de una transacción y registra bitácora. Repetirla devuelve la misma entrega. La fecha de inserción se genera en SQL en UTC, separada de la fecha de detención y del periodo declarado. Se ignoran las antiguas columnas H e I del Excel recibido; se conserva el archivo íntegro.

Los perfiles se consultan en SQL en cada solicitud: un cambio de rol o desactivación se aplica al siguiente acceso. Solo PRIMER_RESPONDIENTE carga y confirma. SUPER_USUARIO consulta nacional y crea usuarios. CONSULTA lee dentro de la institución/ámbito asignados; sin institución puede consultar entre corporaciones, con el filtro territorial correspondiente. Una carga con filas fuera del alcance queda completamente oculta; no se entrega un archivo original con filas ajenas. FISCALIA puede autenticarse, pero su operación de seguimiento queda pendiente de definición.

## Reglas iniciales y pendientes

- Siete campos requeridos provisionalmente; máximo 10 MB, 20,000 filas y 100 MB descomprimidos.
- Folio con estructura y fecha válida; exclusión IA/AI indicada por el diccionario. Fecha de detención como fecha real de Excel. No se aceptan fórmulas en los siete campos.
- Corporación del archivo igual a la institución de la cuenta; homologación de Marina/SEMAR y Guardia Nacional/GN. Catálogo de entidades con equivalencias comunes. Fuero común/federal.
- Autoridad receptora libre (500 caracteres), nombre libre (300). El periodo se declara sin imponer quincenas, cierres ni relación con las fechas de las filas.
- Los repetidos dentro del archivo y las diferencias entre fecha/fuero del folio y del reporte generan observaciones. No se eliminan ni se fusionan registros. No hay aún detección operativa de duplicados entre cargas.
- Pendientes: cruce RND/RND_FA, decisión entre coincidencias múltiples, asignación a fiscalías, catálogos de fiscalías estatales, plantilla verde, seguimiento, aprobación, cierres y métricas.
- La columna `homologar_a_diccionario` no forma parte del modelo.
- No hay eliminación automática de cargas validadas sin confirmar ni recuperación de borradores desde el front. Definir retención antes de operación productiva.
- Administración inicial: alta y consulta de usuarios. Edición, baja, recuperación/cambio de contraseña y caducidad por inactividad se incorporarán en otra entrega. El token actual vence en una hora.

La API no contiene cadenas de conexión reales, no accede a DBBridge ni modifica sus tablas. La integración utilizará las copias diarias de DBBridge, con conexión separada de solo lectura y corte registrado cuando se defina la regla operativa.

## Compilación

```powershell
dotnet build EficienciaMinisterialAPI.slnx -c Release
```

La solución contiene únicamente el proyecto de la API. La primera entrega se compila con SDK .NET 10; los scripts pasaron análisis de sintaxis T-SQL 2019, pero el flujo completo requiere validación en una instancia SQL Server de pruebas antes del despliegue.

## Formato

Cuatro espacios, llaves en línea aparte, namespace de archivo y condiciones `if(...)`, conforme al fragmento proporcionado. `.editorconfig` y `.gitattributes` conservan sangría y CRLF. `.gitignore` excluye `.vs`, `bin`, `obj`, archivos de usuario, credenciales y logs. Se retiraron del seguimiento los archivos locales de Visual Studio y compilación del commit inicial.
