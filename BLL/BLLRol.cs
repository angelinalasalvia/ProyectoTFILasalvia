using BE;
using DAL;

namespace BLL
{
    /// <summary>Lifeline ":Rol" del DS de CU18.</summary>
    public class BLLRol
    {
        private readonly AccesoDatos _dal;

        public BLLRol(AccesoDatos dal)
        {
            _dal = dal;
        }

        /// <summary>DS: ObtenerRolYPermisos(idUsuario): Rol — el rol del usuario con sus permisos.</summary>
        public async Task<Rol> ObtenerRolYPermisos(int idUsuario)
        {
            const string sqlRol = @"
            SELECT r.IdRol, r.Nombre, r.Descripcion
            FROM Rol r
            INNER JOIN Usuario u ON u.IdRol = r.IdRol
            WHERE u.IdUsuario = @IdUsuario";
            var rol = (await _dal.Leer<Rol>(sqlRol, new { IdUsuario = idUsuario })).FirstOrDefault()
                      ?? new Rol();

            const string sqlPermisos = @"
            SELECT p.IdPermiso AS Id, p.Nombre, p.Descripcion
            FROM Permiso p
            INNER JOIN Rol_Permiso rp ON rp.PermisoID = p.IdPermiso
            WHERE rp.RolID = @IdRol";
            rol.Permisos = (await _dal.Leer<Permiso>(sqlPermisos, new { rol.IdRol })).ToList();
            return rol;
        }
        // DS: ObtenerRoles(): List<Rol>  -> cada rol viene con sus permisos asignados
        public async Task<List<Rol>> ObtenerRoles()
        {
            var roles = (await _dal.Leer<Rol>(
                "SELECT IdRol, Nombre, Descripcion FROM Rol ORDER BY IdRol")).ToList();

            var asignaciones = (await _dal.Leer<AsignacionRolPermiso>(@"
        SELECT rp.RolID, p.IdPermiso AS PermisoID, p.Nombre, p.Descripcion
        FROM Rol_Permiso rp
        INNER JOIN Permiso p ON p.IdPermiso = rp.PermisoID
        ORDER BY p.IdPermiso")).ToList();

            foreach (var rol in roles)
                rol.Permisos = asignaciones
                    .Where(a => a.RolID == rol.IdRol)
                    .Select(a => new Permiso { Id = a.PermisoID, Nombre = a.Nombre, Descripcion = a.Descripcion })
                    .ToList();

            return roles;
        }

        // DS: ObtenerPermisosAsociados(): List<Permiso>  -> catálogo completo (las filas de la matriz)
        public async Task<List<Permiso>> ObtenerPermisosAsociados()
        {
            var filas = await _dal.Leer<Permiso>(
                "SELECT IdPermiso AS Id, Nombre, Descripcion FROM Permiso ORDER BY IdPermiso");
            return filas.ToList();
        }

        // DS: ActualizarPermisosRol(int idRol, List<Permiso> listaPermisosModificados): int
        // Reemplaza las asignaciones del rol en una sola transacción. Los ids son int (no hay texto del usuario en el SQL).
        public async Task<int> ActualizarPermisosRol(int idRol, List<Permiso> listaPermisosModificados)
        {
            var ids = listaPermisosModificados.Select(p => p.Id).Distinct().ToList();

            var sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DELETE FROM Rol_Permiso WHERE RolID = @IdRol;";

            if (ids.Count > 0)
                sql += $@"
            INSERT INTO Rol_Permiso (RolID, PermisoID)
            SELECT @IdRol, IdPermiso FROM Permiso WHERE IdPermiso IN ({string.Join(",", ids)});";

            sql += @"
            COMMIT TRANSACTION;";

            return await _dal.Modificar(sql, new { IdRol = idRol });
        }

        private class AsignacionRolPermiso
        {
            public int RolID { get; set; }
            public int PermisoID { get; set; }
            public string Nombre { get; set; } = "";
            public string Descripcion { get; set; } = "";
        }
    }
}

