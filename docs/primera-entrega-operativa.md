# Primera entrega operativa — 9 de noviembre de 2026

## Diseño y alcance

Se extiende la API .NET 10, SQL Server y el front Angular existentes. Se conserva su estructura y diseño SIIID. El usuario autorizó continuar sobre los repositorios y las reglas acordadas en el cuestionario y el chat.

Cada cuenta tiene una o más asignaciones completas de perfil, institución y alcance (nacional o entidad). La sesión trabaja con una asignación elegida y la API vuelve a comprobarla en la base en cada solicitud. No se mezclan los permisos de una institución con los de otra entidad. El superusuario es nacional.

Se mantiene la compatibilidad de la cuenta primaria para el alta inicial y las columnas existentes. Una migración agrega las asignaciones y convierte los accesos federales anteriores en alcance nacional; federal identifica la institución, no un tercer alcance territorial. No se inventa el catálogo pendiente de fiscalías estatales.

La administración permite alta, edición, desactivación y reactivación. No se elimina la última cuenta activa con acceso de superusuario. Toda modificación se registra en bitácora sin contraseñas ni datos personales de detenciones. Los accesos se renuevan al editarlos y sus sesiones anteriores dejan de ser válidas.

Se ajusta la lectura de la plantilla: corporación, fecha y entidad obligatorias; al menos folio o nombre; autoridad y fuero opcionales; IA/AI rechazados; folio de nomenclatura incorrecta conservado para futuro cruce por nombre; nombres normalizados por acentos, espacios y mayúsculas. La validación sigue rechazando el archivo completo ante errores.

## Plan

- [x] BD: migración aditiva `003_usuarios_y_validacion.sql`, asignaciones y compatibilidad de campos opcionales. Script 004 con los cinco catálogos del Excel y MASC añadido.
- [x] API: contratos, validación, catálogo de usuarios desde BD, selección de acceso, administración y autorización actualizada.
- [x] Front: edición/estado, múltiples asignaciones y selector de acceso, catálogos desde API, visibilidad según perfil.
- [x] Validación: compilar API/front, comprobar normalización, campos opcionales, IA/AI, múltiples personas por folio y aislamiento de alcance.
- [x] Documentación lista; publicación mediante un commit por repositorio sobre main.

## Fuera de esta entrega

Cruce efectivo con RND, bandera de duplicados consolidada entre cargas, control y reemplazo de quincenas, entregas en ceros, plantillas/validación de fiscalías y reportes productivos. Los informes actuales contienen exclusivamente datos sintéticos y siguen siendo demo.

Los siete pendientes son: catálogo de fiscalías; asignación y reasignación; adolescentes/identidad reservada; conteo de un folio con varias personas y distintos resultados; denominadores de indicadores; fecha de agrupación de gráficas; corte de respuestas de fiscalías. No se implementan supuestos sobre esos puntos.

## Verificación y puesta en marcha

Ejecutar 001 y 002 si la base es nueva, y después 003 y 004, antes de usar la API actualizada. No se ejecuta nada en servidores desde esta entrega. La compilación y las verificaciones locales no sustituyen la prueba de persistencia con una base SQL Server de desarrollo.

## Decisiones de implementación

Se usa selección explícita de acceso por sesión en vez de sumar perfiles globalmente, para impedir combinar el permiso de una institución con la entidad de otra. Las ediciones regeneran los accesos y exigen nueva sesión. Se conserva el límite de 12 caracteres de contraseña ya existente; esta entrega no cambia su política.

Comprobaciones: compilación de API, compilaciones Angular normal y con base `/primer-respondiente/`, 12 pruebas de reglas con XLSX sintéticos y accesos, comprobaciones de lógica Angular con HTTP simulado y análisis de sintaxis de los cuatro scripts con Microsoft ScriptDom para SQL Server 2019. No se ha conectado a una base real ni probado la interfaz en un navegador en esta entrega.

Al actualizar desde la versión anterior se requiere volver a iniciar sesión: la API exige el identificador de acceso en los tokens nuevos. Las cuentas y contraseñas existentes se conservan.

Revisión final independiente: sin hallazgos críticos/importantes. Se cerró la compatibilidad de tokens antiguos sin acceso explícito: ahora se rechazan con 401 antes de consultar SQL. Se verificó por HTTP con tokens firmados sintéticos sin acceso y con acceso inválido. También se verificaron estado 200, endpoints protegidos 401 y login sin datos 400.
