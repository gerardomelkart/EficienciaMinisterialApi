# Revisión de carga, catálogos y simplicidad — 9 de noviembre de 2026

## Regla vigente y contraste con el cuestionario

- Corporación: proviene del acceso del usuario y no es editable para primer respondiente. La columna CORPORACION sigue siendo obligatoria y debe coincidir con ese acceso (pregunta 6).
- Acceso estatal: todas las filas deben pertenecer a su entidad. La API valida cada fila y cualquier error impide aceptar el archivo completo (pregunta 11 y aclaración de alcance 49–54).
- Excepción confirmada en el chat: solo el superusuario tiene selector de corporación, obtenido del catálogo de BD. Las filas del Excel deben coincidir con la selección.
- Periodicidad: se elige una quincena del catálogo; no se recibe un intervalo libre (pregunta 1).
- Cambio acordado el 9 de noviembre de 2026: las fechas de detención deben pertenecer a la quincena seleccionada, sin tolerancia de días. Esta decisión sustituye la respuesta anterior de la pregunta 2, que permitía fechas anteriores al periodo.
- El inicio histórico de junio de 2025 es un dato de instalación en SQL, tomado de la pregunta 5; no está duplicado en el front ni en C#.

La solicitud inicial de tomar siempre la corporación del Excel fue precisada por el usuario: se toma del acceso, con selector exclusivo del superusuario. La implementación sigue esa última aclaración. Fuera del cambio de fechas documentado arriba, no se identificó otra contradicción en estas correcciones.

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

## Selección por año y mes y validación estricta (2026-11-09)

Diseño: conservar la entrega quincenal y dividir el selector extenso en año, mes y quincena. Se eligió filtrar los periodos existentes de SQL, en lugar de generar calendarios o listas de años en Angular. Al cambiar año o mes se descarta la selección dependiente y cualquier validación anterior.

La bienvenida presenta una cabecera institucional, el acceso activo y acciones según perfil, sin métricas ficticias. Mantiene la navegación superior y los colores actuales.

Implementación y comprobaciones:

- [x] Sustituir el selector extenso por filtros dependientes en CargaPage; enviar el mismo idPeriodo.
- [x] Consultar el periodo habilitado antes de leer el Excel y validar cada FECHA_DETENCION, con límites incluidos.
- [x] Revalidar las fechas dentro de la transacción al guardar y al confirmar. Los borradores de versiones anteriores con fechas fuera de rango deben validarse de nuevo. Las entregas ya confirmadas no se modifican.
- [x] Actualizar el diccionario de la plantilla y los README.
- [x] Compilar front y API, ejecutar verificaciones de reglas y comprobar bienvenida y selección en navegador.

No requiere una migración nueva. Sigue siendo necesario tener aplicada la migración 005 de periodos y catálogos de captura.

Validación de esta entrega: 34 comprobaciones de reglas con Excel reales generados en memoria, incluidas fechas límite, fuera de rango, año distinto, febrero bisiesto y cambio de año; compilación Release de API sin advertencias ni errores; compilación Angular; análisis sintáctico de las consultas nuevas con ScriptDom; navegador en 1440, 768, 390 y 320 píxeles, con HTTP simulado, para selección de periodo, reinicio de filtros y validación anterior, estado del archivo y permisos de interfaz. Revisión de código sin hallazgos críticos o importantes. No se ejecutaron las consultas contra la base real.
