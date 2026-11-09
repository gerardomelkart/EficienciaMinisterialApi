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

El flujo implementado para las corporaciones permite descargar una plantilla, cargar un Excel, revisar errores y observaciones y confirmar una entrega para obtener un folio y un acuse PDF institucional generado bajo demanda. Se conservan los registros y metadatos de la entrega; no se almacenan archivos Excel ni PDF en la base o en el disco de la API.

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

1. Instalar SDK .NET 10 y ejecutar los scripts `001_base.sql`, `002_catalogos.sql`, `003_usuarios_y_validacion.sql`, `004_catalogos_ministeriales.sql`, `005_periodos_y_catalogos_captura.sql` y `006_archivos_bajo_demanda.sql` del repositorio de BD.
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
| GET `/api/catalogos/usuarios` | Instituciones, entidades, perfiles y ámbitos desde SQL; solo superusuario. |
| GET `/api/catalogos/carga` | Quincenas habilitadas desde SQL y límites técnicos configurados; primer respondiente y superusuario. |
| GET `/api/primer-respondiente/plantilla` | Excel de siete columnas y diccionario. |
| POST `/api/primer-respondiente/cargas/validar` | Multipart: archivo e idPeriodo; el superusuario agrega institucion. Las fechas se obtienen del periodo en SQL. |
| POST `/api/primer-respondiente/cargas/confirmar` | JSON `{ "idCarga": "UUID" }`. Confirmación idempotente por usuario. |
| GET `/api/primer-respondiente/cargas` | Últimas 200 entregas confirmadas visibles. |
| GET `/api/primer-respondiente/cargas/{id}` | Datos de la entrega visible. |
| GET `/api/primer-respondiente/cargas/{id}/archivo` | XLSX reconstruido bajo demanda desde los registros confirmados. |
| GET `/api/primer-respondiente/cargas/{id}/acuse` | PDF previo para el borrador propio y PDF definitivo para la entrega confirmada, generados bajo demanda. |

La validación no genera un folio. Guarda las filas y los metadatos solo si todo el archivo es válido; el binario se usa únicamente en memoria durante la petición. La confirmación asigna un folio `EM-PR-AÑO-SECUENCIA` dentro de una transacción y registra bitácora. Repetirla devuelve la misma entrega. La fecha de inserción se genera en SQL en UTC, separada de la fecha de detención y del periodo declarado. Se ignoran las antiguas columnas H e I del Excel recibido; no se conserva el binario del archivo.

Los accesos se consultan en SQL en cada solicitud. Cada sesión trabaja con una sola asignación de perfil, institución y alcance; no se combinan los permisos territoriales de distintas asignaciones. El token incluye su identificador. La desactivación o edición de la cuenta revoca sus sesiones anteriores. Los accesos heredados FEDERAL pasan a NACIONAL en la migración 003: FGR sigue siendo la institución federal.

SUPER_USUARIO administra cuentas y puede cargar para una corporación activa elegida. PRIMER_RESPONDIENTE carga dentro de su institución y entidad asignadas. CONSULTA lee dentro del alcance elegido. Una carga con filas ajenas queda completamente oculta, incluidas sus descargas. FISCALIA puede autenticarse; su operación de seguimiento se implementará en una entrega posterior. No se permite desactivar ni retirar los permisos del último superusuario activo.

Las altas aceptan `accesos: [{ rol, institucion, ambito, entidad }]`; por compatibilidad también reciben los campos primarios en el cuerpo. La edición recibe `usuario`, `nombre`, `activo` y `accesos`, sin contraseña. Solo se admiten los ámbitos NACIONAL y ESTATAL, con entidades activas 1–32. Los cambios se auditan con el identificador interno de la cuenta objetivo.

## Reglas implementadas y alcance pendiente

- Corporación, fecha y entidad obligatorias. Al menos folio RND o nombre. Autoridad y fuero opcionales.
- Los límites técnicos se configuran en la sección Carga y se entregan al front; fechas de Excel y sin fórmulas. Cualquier error impide aceptar el archivo completo.
- Se rechazan IA/AI. Una nomenclatura incorrecta genera observación para búsqueda posterior por nombre; no acredita un cruce exitoso.
- Se normalizan acentos, espacios y mayúsculas para comparar. Se reconocen CDMX/Ciudad de México y México/Estado de México. El periodo corresponde a la entrega; las fechas de detención pueden ser anteriores, conforme a la pregunta 2 del cuestionario.
- Las filas con folio repetido se conservan. La bandera consolidada de duplicados entre cargas y resultados RND se implementará con el cruce.
- Los cinco catálogos ministeriales del área, incluido MASC agregado a conclusiones, quedan disponibles en SQL mediante el script 004. El script 005 agrega perfiles, ámbitos, equivalencias y periodos quincenales. Todavía no se implementa la carga de respuestas de fiscalías.
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

## Corrección de carga y revisión de valores fijos

Aplicar la migración 005 antes de iniciar esta API. La corporación viene del acceso del primer respondiente; solo superusuario puede elegir otra desde el catálogo. Todas las filas se validan contra esa corporación y, en alcance estatal, contra su entidad.

Se recibe idPeriodo, no un par de fechas. El calendario genera las siguientes quincenas en SQL y permite habilitarlas o cerrarlas. El front y la plantilla consultan los catálogos vigentes. Las restricciones de almacenamiento y reglas de autorización siguen siendo contratos del programa.

La [revisión de carga y catálogos](docs/revision-carga-y-catalogos.md) documenta el contraste con el cuestionario, la auditoría Ponytail, el origen de los datos y los límites de las pruebas.

## Exportación del resultado de validación

POST `/api/primer-respondiente/cargas/validacion/excel` recibe `{ filas: [{ tipo, fila, campo, valor, mensaje }] }` y genera un XLSX bajo demanda. `fila` puede ser null para mensajes generales. Disponible para primer respondiente y superusuario. El front envía el conjunto completo de incidencias, no solo la página visible. Esta operación formatea el resultado mostrado por el navegador; no valida un archivo nuevo, no confirma entregas ni consulta RND.

La respuesta de validación entrega errores y observaciones estructurados con `fila`, `campo`, `mensaje` y `valor`. El valor corresponde a la celda antes de recortar espacios o normalizar acentos y mayúsculas. Los avisos generales usan fila 0 y valor null. El XLSX incluye VALOR_EXCEL como texto; no interpreta su contenido como fórmula. Actualizar front y API juntos; el front también puede mostrar respuestas antiguas con observaciones de texto, pero esas respuestas no contienen los valores originales.

## Rendimiento de cargas

Las filas validadas se insertan con SqlBulkCopy dentro de la misma transacción que la cabecera y la bitácora. Se conservan claves foráneas, restricciones, triggers y filas repetidas; el folio se asigna únicamente al confirmar. Un fallo del lote revierte la operación. La lectura del periodo mantiene un bloqueo compartido hasta terminar, para impedir su cierre durante el guardado sin serializar cargas entre sí con un bloqueo de actualización.

El catálogo consulta los periodos existentes y solo ejecuta GenerarPeriodos si falta el correspondiente a la fecha actual de México. No se almacena en caché: los cambios de habilitación y cierre siguen consultándose en SQL.

Los logs de API incluyen tiempos de conexión, preparación y guardado del lote; consulta de periodos; y catálogos/lectura y reglas del Excel. No incluyen contenido del archivo, nombres de personas ni credenciales. Para comparar una carga, revisar los mensajes «Validación Excel», «Guardado de carga» y «Catálogo de periodos». Las pruebas locales verifican la correspondencia de los nueve campos y todas las filas del lote; el tiempo y la reversión del bulk copy deben comprobarse contra SQL Server en desarrollo. El cambio de almacenamiento descrito abajo requiere la migración 006.

## Acuses y archivos bajo demanda

Aplicar `scripts/006_archivos_bajo_demanda.sql` del repo de BD antes de iniciar esta API. La migración elimina la columna binaria `archivo`, incluidos los Excel guardados previamente; conserva sus filas, folios, nombre y SHA-256. Las instalaciones nuevas también ejecutan 006 (es idempotente).

`GET /api/primer-respondiente/cargas/{id}/acuse` genera un PDF previo si la carga está VALIDADA y pertenece al usuario y acceso actual; el PDF definitivo requiere una carga CONFIRMADA visible para el acceso. El previo no tiene folio y se marca PREVIO. El definitivo usa la fecha de confirmación en horario de México. No se genera folio hasta confirmar.

`GET /api/primer-respondiente/cargas/{id}/archivo` reconstruye un XLSX desde las filas persistidas y los catálogos. No es una copia binaria del original: normalización, metadatos, formatos, filas vacías y columnas ignoradas del libro de entrada no se conservan. Se asignan nombres descriptivos con institución, periodo y folio. Los PDF y XLSX se producen en memoria por petición.

El modal sigue el patrón de SIIID2.FrontEnd; el generador PDF toma como referencia AcuseFese de IngestaDeDatos.Api y usa el mismo encabezado institucional. Solo se incorporan recursos institucionales, sin datos ni configuración de esos sistemas.

El formulario multipart mantiene en memoria los archivos hasta el máximo configurado en Carga:MaximoBytes, sin el derrame temporal a disco del umbral predeterminado de ASP.NET. Ese máximo no puede superar Int32.MaxValue; el valor habitual es 10 MB. El PDF usa las fuentes estándar y codificación de FESE; caracteres fuera de Latin-1 en el nombre original pueden mostrarse como sustituciones en su texto. El nombre de descarga se construye con institución, periodo y folio, usando caracteres seguros.
