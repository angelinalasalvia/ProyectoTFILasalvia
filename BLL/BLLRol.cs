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
    }
}

