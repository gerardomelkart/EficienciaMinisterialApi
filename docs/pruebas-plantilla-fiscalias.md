# Prueba de plantilla para fiscalías por entidad

1. Ejecutar en desarrollo el script 010_fiscalias_por_entidad.sql del repositorio EficienciaMinisterialBD, después de 001–009. Actualizar y reiniciar API y Front.
2. En Usuarios crear un acceso FISCALIA, ámbito ESTATAL y entidad Jalisco. No se solicita institución. Si se edita una cuenta existente, volver a iniciar sesión.
3. Entrar como ese usuario. Abrir Plantilla de fiscalías, seleccionar año y periodo y descargar.
4. Revisar que Catálogos de periodos y Excel incluyan únicamente registros de la entidad de detención asignada. No depende de institución, corporación o fuero: incluye FC y FF dentro de ese territorio. Se mantienen filtros de confirmada, vigente y LOCALIZADO.
5. Crear otro usuario FISCALIA, ámbito NACIONAL, sin entidad ni institución. Debe poder descargar los registros elegibles de todas las entidades. Superusuario también tiene ese alcance.
6. Un periodo ajeno solicitado manualmente no devuelve registros de otras entidades. Parámetros de fiscalía o entidad enviados por el cliente no cambian el alcance: se determina desde el usuario activo en BD.
7. Descargar Excel: nombre Plantilla_Fiscalia_dd-MM-yyyy_al_dd-MM-yyyy.xlsx. Hojas Seguimiento, Catálogos e Instrucciones; originales en guinda y respuesta vacía en verde, con listas ministeriales existentes. Se conservan fechas y filas repetidas.
8. Usuarios de primer respondiente y consulta no ven la opción; las rutas de fiscalías devuelven 403 para esos perfiles.
9. Si no aparecen periodos, confirmar una carga con registros localizados de la entidad asignada. No alterar RND para simular coincidencias.

## Estado y pendientes

El catálogo temporal de FGR se eliminó. No se necesita catálogo de fiscalías: los usuarios se vinculan a una entidad o a alcance nacional. Las instituciones históricas en accesos existentes se ignoran para este módulo; al guardar un usuario de fiscalía se limpia su institución.

La carga, validación condicional y confirmación de respuestas ministeriales se implementarán después. Traslados/reasignación e identidad reservada/adolescentes siguen pendientes de definición.

No se consultan ni modifican fuentes RND en esta descarga: se usan los cruces guardados en EficienciaMinisterial. Excel se genera en memoria y se registra la descarga en bitácora.
