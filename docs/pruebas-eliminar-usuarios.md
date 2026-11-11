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

Borrado lógico permanente desde la interfaz; el nombre de usuario queda reservado para conservar su identidad histórica. No confundir con desactivar: una cuenta inactiva sigue disponible para editar/reactivar, una eliminada no.
