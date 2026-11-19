# Eliminar cuentas

Ejecutar 011_eliminar_usuarios.sql del repositorio BD en desarrollo antes de iniciar la nueva API. El script agrega eliminado con valor inicial 0, no elimina cuentas existentes.

1. Crear una cuenta de prueba como superusuario. Pulsar Eliminar junto a Editar.
2. Cancelar: la cuenta debe permanecer.
3. Confirmar: desaparece de Usuarios y de sus contadores. Su login y tokens previos dejan de funcionar. Las cargas y bitácora se conservan.
4. La propia cuenta tiene Eliminar deshabilitado y la API rechaza DELETE a su propio id con 409.
5. Sólo SUPER_USUARIO puede usar DELETE /api/usuarios/{id}. Otros perfiles reciben 403.
6. Una cuenta ya eliminada devuelve 404 al repetir la operación, y no se puede reactivar por PUT.
7. Último superusuario activo protegido. Eliminación usa el mismo bloqueo transaccional que edición para evitar cambios simultáneos que dejen sin administrador.
8. Verificar seguridad.bitacora: accion ELIMINAR_USUARIO, actor e id_usuario_objetivo.

Borrado lógico recuperable desde la interfaz; el nombre de usuario queda reservado para conservar su identidad histórica. No confundir con desactivar: una cuenta inactiva sigue disponible para editar/reactivar, una eliminada se recupera con Reactivar.


## Mostrar inactivos, reactivación y orden

- Por defecto se muestran sólo cuentas activas no eliminadas. Marcar **Mostrar inactivos y eliminados** para incluir las demás; desmarcar las oculta.
- Nombre, Usuario, Accesos asignados y Estado alternan ascendente/descendente desde sus encabezados, con indicador visual. Ordena todo el resultado antes de paginar; cambiar orden o check vuelve a página 1.
- Eliminar una cuenta de prueba, marcar el check y verificar estado Eliminado. Cancelar Reactivar no modifica la cuenta. Confirmar restaura activo=1, eliminado=0 y conserva accesos y contraseña.
- Desmarcar el check: la cuenta reactivada sigue visible como Activo. Puede iniciar sesión de nuevo. Tokens previos a eliminar o reactivar siguen rechazados por versión de credenciales.
- Cuenta sólo inactiva también tiene Reactivar. La edición de cuentas eliminadas sigue bloqueada hasta su reactivación.
- PUT /api/usuarios/{id}/reactivar sólo SUPER_USUARIO; inexistente devuelve 404, ya activa 409. Repetir no crea accesos ni entradas de bitácora duplicadas.
- Verificar acción REACTIVAR_USUARIO en bitácora con actor y objetivo. Eliminación, reactivación y edición comparten bloqueo transaccional de administración.
- Usa la columna eliminado del script 011; esta entrega no requiere otro script.
