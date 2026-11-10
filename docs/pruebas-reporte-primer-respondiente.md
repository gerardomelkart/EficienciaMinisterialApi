# Probar Reporte primer respondiente en desarrollo

Actualizar API y Front con git pull y reiniciar ambos. No requiere nuevas migraciones; deben estar
aplicadas las migraciones previas hasta 009. El reporte usa EficienciaMinisterial_DEV y no consulta RND.

1. Iniciar sesión con superusuario y abrir Informes > Reporte primer respondiente.
2. Comprobar que sólo aparecen entregas confirmadas y vigentes. Los borradores y versiones sustituidas no cuentan.
3. Elegir una quincena con entregas y una corporación y presionar Consultar. La quincena debe poder ser cerrada.
4. Verificar que Registros reportados = Localizados + No localizados + Pendientes de cruce.
   Duplicados es una bandera dentro de ese universo, no una cantidad adicional. Folios informados distintos
   excluye vacíos y no constituye por sí mismo un indicador de eficiencia ministerial.
5. Cambiar de página y el tamaño 25/50/100; totales y resumen deben permanecer iguales.
6. Descargar Excel. Debe tener Resumen y Registros, todas las filas filtradas y un nombre basado en fechas,
   sin IDs ni hashes. La fecha de detención puede ser anterior al periodo de entrega.
7. Entrar con primer respondiente SSPC. Catálogos, consulta y Excel sólo deben mostrar SSPC. Con un acceso
   estatal, verificar que el detalle contiene únicamente esa entidad, incluso si la entrega original era nacional.
8. Con usuario Consulta, revisar que se conserve su institución/entidad. Cambiar el acceso activo o volver a
   iniciar sesión debe actualizar las opciones. Los permisos se evalúan en la API, no sólo en la interfaz.
9. Elegir filtros sin resultados: mensaje de ausencia de entregas, ceros y descarga desactivada, sin datos de ejemplo.
10. Reporte completo sigue en preparación hasta implementar las respuestas de fiscalías; no sirve datos ficticios.

Los endpoints nuevos son GET /api/informes/primer-respondiente/catalogos,
GET /api/informes/primer-respondiente y GET /api/informes/primer-respondiente/excel.
Filtros opcionales: inicio/fin (quincena exacta), corporacion, entidad; pagina y tamano (1–100).
Un cambio manual de filtros no amplía los permisos del acceso activo.
Se registran CONSULTAR_REPORTE_PR y DESCARGAR_REPORTE_PR en seguridad.bitacora.

Validación local: compilación API y Angular; verificaciones/Reglas.cs prueba filtros y Excel con datos
sintéticos exclusivamente de pruebas. Pendiente verificar consultas, permisos y tiempos contra SQL real.

## Filtros, ordenamiento y descargas

- Año lista sólo años con entregas accesibles; periodo muestra las quincenas del año seleccionado. Cambiar año limpia el periodo.
- Primer respondiente: corporación fija según su acceso activo. Acceso estatal: entidad fija. Acceso nacional: permite seleccionar entidad. SQL intersecta siempre los filtros con el acceso, también al descargar.
- En ambas tablas, pulsar cada encabezado alterna ascendente/descendente. En registros se ordena toda la consulta antes de paginar. Cambiar orden regresa a la primera página.
- Cada tabla tiene descarga propia; el botón general conserva ambas hojas. Se exportan todos los registros filtrados en el orden elegido, no sólo la página visible.
- Probar con Guardia Nacional estatal y nacional, y con superusuario. Manipular los parámetros corporacion/entidad desde Network no debe ampliar el acceso.
