# Revisión de carga, catálogos y simplicidad — 9 de noviembre de 2026

## Regla vigente y contraste con el cuestionario

- Corporación: proviene del acceso del usuario y no es editable para primer respondiente. La columna CORPORACION sigue siendo obligatoria y debe coincidir con ese acceso (pregunta 6).
- Acceso estatal: todas las filas deben pertenecer a su entidad. La API valida cada fila y cualquier error impide aceptar el archivo completo (pregunta 11 y aclaración de alcance 49–54).
- Excepción confirmada en el chat: solo el superusuario tiene selector de corporación, obtenido del catálogo de BD. Las filas del Excel deben coincidir con la selección.
- Periodicidad: se elige una quincena del catálogo; no se recibe un intervalo libre (pregunta 1).
- Periodo de entrega y fecha de detención son distintos. Se conservan las detenciones anteriores al periodo elegido (pregunta 2). El cuestionario es la referencia funcional y no se sustituye por una interpretación de una petición de interfaz.
- El inicio histórico de junio de 2025 es un dato de instalación en SQL, tomado de la pregunta 5; no está duplicado en el front ni en C#.

La solicitud inicial de tomar siempre la corporación del Excel fue precisada por el usuario: se toma del acceso, con selector exclusivo del superusuario. La implementación sigue esa última aclaración. No se identificó otra contradicción en estas correcciones.

## Datos que ahora se consultan

| Dato | Origen |
| --- | --- |
| Quincenas, habilitación y cierre | primer_respondiente.periodo |
| Corporaciones y nombres | catalogo.institucion |
| Equivalencias de corporaciones | catalogo.institucion_alias |
| Entidades y habilitación para captura | catalogo.entidad |
| Equivalencias de entidades | catalogo.entidad_alias |
| Fueros | catalogo.fuero |
| Perfiles, nombres y reglas de asignación | catalogo.perfil |
| Ámbitos y requisito de entidad | catalogo.ambito |
| Motivos, medidas y conclusiones | catalogo.opcion_ministerial, script 004 |
| Límites técnicos del archivo | sección Carga de la configuración de API; se devuelven al front |

La plantilla incluye una hoja Catálogos generada con los datos vigentes. No hay listas alternativas de corporaciones, estados, perfiles o fueros incrustadas en los componentes. Si faltan configuración o catálogos, la operación falla y muestra el error; no utiliza datos de ejemplo como respaldo.

## Constantes técnicas y datos de prueba

Las rutas HTTP, identificadores de permisos, nombres de columnas, formato RND (incluida exclusión IA/AI), restricciones de almacenamiento, mínimo criptográfico, textos y colores son contratos y reglas del programa. Se conservan en código. Los nombres y fechas sintéticos de verificaciones/Reglas.cs se usan exclusivamente en pruebas; no se sirven como datos de la aplicación.

Los INSERT de migraciones son datos iniciales de referencia y proceden del cuestionario, SIIID o catálogos del área. No son una segunda fuente de valores en ejecución: después de instalar, la API consulta SQL.

## Revisión Ponytail de simplicidad

- delete: @angular/router y sweetalert2 sin consumidores. Se retiraron de package.json y lockfile. [PrimerRespondienteFront/package.json]
- delete: SessionService.actualizarUsuario sin consumidores. Se eliminó. [PrimerRespondienteFront/src/app/core/services/session.service.ts]
- delete: CargasService.listar sin pantalla consumidora. Se eliminó; el endpoint real de API sigue disponible. [PrimerRespondienteFront/src/app/core/services/cargas.service.ts]

net: -14 lines, -2 deps possible. Recortes aplicados en esta entrega.

## Revisión funcional y seguridad

Se retiró el hash fijo usado para igualar la verificación de una cuenta inexistente: se genera aleatoriamente una vez por proceso. No es una credencial de acceso.

El multipart recibe idPeriodo. La API consulta y bloquea el periodo habilitado en SQL y obtiene sus fechas; no confía en fechas enviadas por el navegador. La confirmación vuelve a verificar la disponibilidad. Las cargas previas conservan sus fechas. Un borrador previo sin idPeriodo deberá validarse nuevamente.

La migración genera quincenas hasta la fecha actual de México, sin años o meses fijos en el front. Los estados habilitado y cerrado son administrables en SQL. El cierre automático al recibir las cuatro corporaciones, el reemplazo de entregas, entregas en ceros y duplicados entre periodos siguen pendientes de implementación; esta corrección no los da por terminados.

## Verificación

- API Release y front compilados, incluidas las rutas de publicación bajo /primer-respondiente/.
- 21 pruebas de reglas con libros XLSX sintéticos, alcance territorial, catálogo recibido y accesos.
- Revisión de interfaz con HTTP simulado: selección y eliminación de archivo, estado visual, periodos desde API, multipart sin fechas libres, selector exclusivo del superusuario y opciones de usuarios desde catálogo; tamaños de escritorio, tablet y móvil.
- Sintaxis de la migración 005 validada con Microsoft ScriptDom para SQL Server 2019.
- Sesiones con identificador de acceso ausente o inválido rechazadas con HTTP 401.

No se han ejecutado migraciones ni probado persistencia contra el servidor SQL real. Antes de usar esta versión, aplicar 005 y actualizar API y front juntos.

Revisión independiente final: sin hallazgos críticos o importantes verificables en los cambios de los tres repositorios. Se mantiene pendiente la comprobación contra SQL Server real.

## Selección por año y mes (2026-11-09)

La selección de entrega se divide en año, mes y quincena, filtrando los periodos habilitados que existen en SQL. No hay años ni meses fijos en Angular. Cambiar año o mes descarta la selección dependiente y cualquier validación anterior.

La bienvenida presenta una cabecera institucional, el acceso activo y acciones según perfil, sin métricas ficticias. Mantiene la navegación superior y los colores actuales.

### Corrección para respetar el cuestionario

La pregunta 2 permite fechas de detención anteriores al periodo de entrega. Se retiró la restricción añadida que obligaba a que cada fecha estuviera dentro de la quincena, tanto en la lectura del Excel como al guardar y confirmar. Se restauraron el diccionario de la plantilla y la nota de la pantalla. No se modifican la bienvenida ni los filtros de año, mes y quincena.

El periodo sigue debiendo estar habilitado al guardar y confirmar. Se conservan las validaciones de estructura, fecha real de Excel, corporación y alcance territorial. Se agregaron pruebas de regresión para fechas anteriores al periodo de entrega.

No requiere una migración nueva. Sigue siendo necesario tener aplicada la migración 005 de periodos y catálogos de captura. Las pruebas locales no ejecutan consultas contra la base real.

## Almacenamiento y acuses (2026-11-09)

La entrega de acuses PDF y carga por lote retira el almacenamiento binario del Excel. Aplicar la migración 006; las filas y metadatos se conservan. Se mantiene la regla del cuestionario de permitir detenciones anteriores al periodo de entrega. El acuse previo no acredita recepción; el definitivo se genera después de confirmar y asignar folio. El cruce con RND continúa pendiente.
