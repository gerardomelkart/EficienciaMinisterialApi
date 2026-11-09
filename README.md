# Eficiencia Ministerial API

API central del proyecto, desarrollada en C# con .NET 10. Este repositorio contiene los controladores, servicios, acceso SQL y configuración de autenticación y logs de la primera etapa.

## Proyecto Eficiencia Ministerial

Eficiencia Ministerial es una plataforma en desarrollo para recibir información de detenciones y, en una etapa posterior, incorporar el seguimiento ministerial de las fiscalías. Su arquitectura contempla una API central y una base propia, con **Primer Respondiente** como primera interfaz de captura.

Esta versión incorpora las primeras reglas confirmadas del cuestionario: usuarios con múltiples accesos y validación básica de la captura. El seguimiento de fiscalías, el cruce con RND y los reportes productivos siguen en desarrollo.

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

- **Disponible:** autenticación JWT, selección de acceso, alta/edición/activación de usuarios, carga y validación de Excel, confirmación con folio, acuse y registros de operación.
- **Provisional:** pantallas de informes “Reporte primer respondiente” y “Reporte completo”, sin consultas ni indicadores definitivos.
- **Pendiente:** catálogo definitivo de fiscalías, plantilla de seguimiento, cruces automáticos, control de quincenas y reportes operativos.

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

1. Instalar SDK .NET 10 y ejecutar los scripts `001_base.sql`, `002_catalogos.sql`, `003_usuarios_y_validacion.sql` y `004_catalogos_ministeriales.sql` del repositorio de BD.
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
| GET / POST `/api/usuarios` | Listado de activas/inactivas y creación; solo superusuario. |
| PUT `/api/usuarios/{id}` | Edición, activación/desactivación y reemplazo de accesos; solo superusuario. |
| POST `/api/auth/acceso` | Selecciona una asignación propia mediante `idAcceso`; devuelve una sesión nueva. |
| GET `/api/catalogos/usuarios` | Instituciones y entidades activas desde SQL; solo superusuario. |
| GET `/api/primer-respondiente/plantilla` | Excel de siete columnas y diccionario. |
| POST `/api/primer-respondiente/cargas/validar` | Multipart: archivo, periodoInicio y periodoFin en formato ISO; el superusuario agrega institucion. |
| POST `/api/primer-respondiente/cargas/confirmar` | JSON `{ "idCarga": "UUID" }`. Confirmación idempotente por usuario. |
| GET `/api/primer-respondiente/cargas` | Últimas 200 entregas confirmadas visibles. |
| GET `/api/primer-respondiente/cargas/{id}` | Datos de la entrega visible. |
| GET `/api/primer-respondiente/cargas/{id}/archivo` | Archivo original. |
| GET `/api/primer-respondiente/cargas/{id}/acuse` | Acuse HTML descargable, imprimible o guardable como PDF desde el navegador. |

La validación no genera un folio. Guarda el archivo y las filas solo si todo el archivo es válido. La confirmación asigna un folio `EM-PR-AÑO-SECUENCIA` dentro de una transacción y registra bitácora. Repetirla devuelve la misma entrega. La fecha de inserción se genera en SQL en UTC, separada de la fecha de detención y del periodo declarado. Se ignoran las antiguas columnas H e I del Excel recibido; se conserva el archivo íntegro.

Los accesos se consultan en SQL en cada solicitud. Cada sesión trabaja con una sola asignación de perfil, institución y alcance; no se combinan los permisos territoriales de distintas asignaciones. El token incluye su identificador. La desactivación o edición de la cuenta revoca sus sesiones anteriores. Los accesos heredados FEDERAL pasan a NACIONAL en la migración 003: FGR sigue siendo la institución federal.

SUPER_USUARIO administra cuentas y puede cargar para una corporación activa elegida. PRIMER_RESPONDIENTE carga dentro de su institución y entidad asignadas. CONSULTA lee dentro del alcance elegido. Una carga con filas ajenas queda completamente oculta, incluido su archivo original. FISCALIA puede autenticarse; su operación de seguimiento se implementará en una entrega posterior. No se permite desactivar ni retirar los permisos del último superusuario activo.

Las altas aceptan `accesos: [{ rol, institucion, ambito, entidad }]`; por compatibilidad también reciben los campos primarios en el cuerpo. La edición recibe `usuario`, `nombre`, `activo` y `accesos`, sin contraseña. Solo se admiten los ámbitos NACIONAL y ESTATAL, con entidades activas 1–32. Los cambios se auditan con el identificador interno de la cuenta objetivo.

## Reglas implementadas y alcance pendiente

- Corporación, fecha y entidad obligatorias. Al menos folio RND o nombre. Autoridad y fuero opcionales.
- Máximo 10 MB, 20,000 filas y 100 MB descomprimidos; fechas de Excel y sin fórmulas. Cualquier error impide aceptar el archivo completo.
- Se rechazan IA/AI. Una nomenclatura incorrecta genera observación para búsqueda posterior por nombre; no acredita un cruce exitoso.
- Se normalizan acentos, espacios y mayúsculas para comparar. Se reconocen CDMX/Ciudad de México y México/Estado de México. Las fechas de detención pueden ser anteriores al periodo de entrega.
- Las filas con folio repetido se conservan. La bandera consolidada de duplicados entre cargas y resultados RND se implementará con el cruce.
- Los cinco catálogos ministeriales del área, incluido MASC agregado a conclusiones, quedan disponibles en SQL mediante el script 004. Todavía no se implementa la carga de respuestas de fiscalías.
- Pendientes de implementación: RND, quincenas/cierres y reemplazos, entregas en ceros, rechazo de detenciones reportadas en periodos anteriores, plantilla verde y reportes reales. La recepción actual conserva estado de cruce PENDIENTE y no habilita envío a fiscalías.
- Los siete puntos de negocio aún abiertos están enumerados en [el alcance de esta entrega](docs/primera-entrega-operativa.md).
- `homologar_a_diccionario` no forma parte del modelo. Cambio/recuperación de contraseña y limpieza de cargas sin confirmar siguen pendientes.

La API no contiene cadenas de conexión reales, no accede a DBBridge ni modifica sus tablas. La integración utilizará las copias diarias de DBBridge, con conexión separada de solo lectura y corte registrado cuando se defina la regla operativa.

## Compilación

```powershell
dotnet build EficienciaMinisterialAPI.slnx -c Release
```

La solución contiene únicamente el proyecto de la API. Esta entrega se compila con SDK .NET 10; los scripts pasaron análisis de sintaxis T-SQL 2019, pero el flujo completo requiere validación en una instancia SQL Server de pruebas antes del despliegue.

## Formato

Cuatro espacios, llaves en línea aparte, namespace de archivo y condiciones `if(...)`, conforme al fragmento proporcionado. `.editorconfig` y `.gitattributes` conservan sangría y CRLF. `.gitignore` excluye `.vs`, `bin`, `obj`, archivos de usuario, credenciales y logs. Se retiraron del seguimiento los archivos locales de Visual Studio y compilación del commit inicial.

## Verificación de reglas sin SQL

```powershell
dotnet run --file verificaciones/Reglas.cs
```

Es una aplicación de archivo de .NET 10, fuera de la solución; no agrega otro proyecto a la API. Prueba libros XLSX sintéticos, normalización, campos opcionales, IA/AI y selección/validación de accesos. No prueba persistencia ni sustituye las pruebas de integración en SQL Server de desarrollo.

Al actualizar desde la versión anterior se requiere volver a iniciar sesión: la API exige el identificador de acceso en los tokens nuevos. Las cuentas y contraseñas existentes se conservan.
