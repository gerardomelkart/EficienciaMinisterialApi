# Prueba de plantilla para fiscalías

1. Actualizar y reiniciar API y Front. No requiere migración adicional: usa scripts existentes 001–009.
2. Entrar como superusuario. Abrir **Plantilla de fiscalías** en el menú.
3. Seleccionar FGR, año y quincena. Sólo se ofrecen periodos con registros confirmados, vigentes, localizados en RND y fuero FF reportado.
4. Descargar. Nombre: `Plantilla_Fiscalia_dd-MM-yyyy_al_dd-MM-yyyy.xlsx`, sin hashes ni identificadores técnicos.
5. Revisar hojas Seguimiento, Catálogos e Instrucciones. Columnas guinda: originales; columnas verdes: vacías para respuesta. Fechas originales incluso anteriores al periodo, filas repetidas y folios vacíos se conservan. Listas Sí/No y motivos desde catálogo ministerial activo.
6. Confirmar que no aparecen registros NO_LOCALIZADO, PENDIENTE, FA, fuero FC ni cargas sin confirmar o reemplazadas.
7. Crear usuario desde Usuarios con perfil FISCALIA, institución FGR, ámbito NACIONAL, sin entidad. Iniciar sesión, cambiar contraseña inicial y repetir descarga. Debe ver la misma plantilla federal nacional.
8. Con primer respondiente o consulta no debe aparecer la opción; las rutas `GET /api/fiscalias/catalogos` y `GET /api/fiscalias/plantilla` deben rechazar su acceso con 403. Una fiscalía estatal tampoco tiene acceso a la plantilla FGR.
9. Si no hay periodos, confirmar una carga PR con al menos un registro FF localizado en RND. No inventar coincidencias ni cambiar RND para probar.

## Pendientes explícitos

- Catálogo de fiscalías temporal hardcodeado únicamente FGR en PlantillaFiscaliaService.Fiscalias por instrucción del responsable. Sustituir por catálogo definitivo en BD y agregar estatales con reglas por entidad.
- Traslados/reasignación e identidad reservada/adolescentes siguen pendientes de definición.
- Esta entrega descarga la plantilla; todavía no importa respuestas, valida reglas condicionales ministeriales ni confirma entrega de fiscalía. Es el siguiente paso.
- Excel se genera en memoria y sólo se guarda bitácora de descarga; no escribe en RND ni guarda archivos en BD.
