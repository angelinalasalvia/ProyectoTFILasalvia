using System.Text;
using BE;
using DAL;

namespace BLL
{
    public class BLLRol
    {
        private readonly AccesoDatos _dal;

        public BLLRol(AccesoDatos dal)
        {
            _dal = dal;
        }
        public async Task<List<Permiso>> TraerPermisosRol(int idRol)
        {
            const string sql = @"
            SELECT p.IdPermiso AS Id, p.Nombre, p.Descripcion
            FROM Permiso p
            INNER JOIN Rol_Permiso rp ON rp.PermisoID = p.IdPermiso
            WHERE rp.RolID = @IdRol
            ORDER BY p.IdPermiso";
            var filas = await _dal.Leer<Permiso>(sql, new { IdRol = idRol });
            return filas.ToList();
        }
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

        public async Task<List<Permiso>> ObtenerPermisosAsociados()
        {
            var filas = await _dal.Leer<Permiso>(
                "SELECT IdPermiso AS Id, Nombre, Descripcion FROM Permiso ORDER BY IdPermiso");
            return filas.ToList();
        }

        public async Task<int> ActualizarPermisosRoles(Dictionary<int, List<Permiso>> listaModificaciones)
        {
            if (listaModificaciones.Count == 0) return 0;

            var parametros = new Dictionary<string, object?>();
            var sql = new StringBuilder("SET XACT_ABORT ON;\n");

            var i = 0;
            foreach (var (idRol, permisos) in listaModificaciones)
            {
                var pRol = $"Rol{i}";
                parametros[pRol] = idRol;
                sql.AppendLine($"DELETE FROM Rol_Permiso WHERE RolID = @{pRol};");

                var j = 0;
                foreach (var idPermiso in permisos.Select(p => p.Id).Distinct())
                {
                    var pPer = $"Per{i}_{j++}";
                    parametros[pPer] = idPermiso;
                    sql.AppendLine($"INSERT INTO Rol_Permiso (RolID, PermisoID) VALUES (@{pRol}, @{pPer});");
                }
                i++;
            }

            return await _dal.Modificar(sql.ToString(), parametros);
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

