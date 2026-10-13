using BE;
using DAL;

namespace BLL
{
    /// <summary>Lifeline ":Usuario" de los DS.</summary>
    public class BLLUsuario
    {
        private readonly AccesoDatos _dal;

        private const string Columnas = "IdUsuario AS Id, Nombre, Apellido, Email, Contrasena, IdRol, Estado";

        public BLLUsuario(AccesoDatos dal)
        {
            _dal = dal;
        }

        /// <summary>
        /// DS CU18: ObtenerUsuario(string email, string contraseña): Usuario.
        /// La contraseña NO filtra la consulta: el DS la valida después (ValidarContraseña), una vez
        /// descartados usuario inexistente / inactivo / cuenta bloqueada, y como el hash lleva salt
        /// no se puede comparar en SQL. El parámetro se conserva solo para respetar la firma del DS.
        /// </summary>
        public Task<Usuario?> ObtenerUsuario(string email, string contraseña)
            => ObtenerUsuario(email);

        /// <summary>DS CU19: ObtenerUsuario(string email): Usuario.</summary>
        public async Task<Usuario?> ObtenerUsuario(string email)
        {
            var sql = $"SELECT {Columnas} FROM Usuario WHERE Email = @Email";
            var filas = await _dal.Leer<Usuario>(sql, new { Email = (email ?? "").Trim() });
            return filas.FirstOrDefault();
        }

        /// <summary>DS CU19: ActualizarContraseña(string email, string nuevaClave). Guarda el hash, nunca el texto plano.</summary>
        /// <summary>nuevaClave llega YA hasheada desde la UI (CifradoService.Hashear).</summary>
        public async Task ActualizarContraseña(string email, string nuevaClave)
        {
            const string sql = "UPDATE Usuario SET Contrasena = @Contrasena WHERE Email = @Email";
            await _dal.Modificar(sql, new { Contrasena = nuevaClave, Email = (email ?? "").Trim() });
        }
    }
}
