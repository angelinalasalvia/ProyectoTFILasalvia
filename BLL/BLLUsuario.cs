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
            var sql = $"SELECT {Columnas} FROM Usuario WHERE Email = @Email AND Estado <> @Eliminado";
            var filas = await _dal.Leer<Usuario>(sql, new { Email = (email ?? "").Trim(), Eliminado = EstadoUsuario.Eliminado });
            return filas.FirstOrDefault();
        }

        /// <summary>DS CU19: ActualizarContraseña(string email, string nuevaClave). Guarda el hash, nunca el texto plano.</summary>
        /// <summary>nuevaClave llega YA hasheada desde la UI (CifradoService.Hashear).</summary>
        public async Task ActualizarContraseña(string email, string nuevaClave)
        {
            const string sql = "UPDATE Usuario SET Contrasena = @Contrasena WHERE Email = @Email";
            await _dal.Modificar(sql, new { Contrasena = nuevaClave, Email = (email ?? "").Trim() });
        }
        // Usuario interno de los envíos automáticos: no se lista.
        private const string EmailUsuarioSistema = "sistema@mailtest.com";

        private const string ColumnasListado = "IdUsuario AS Id, Nombre, Apellido, Email, IdRol, Estado";
        private const string FiltroListado = "Estado <> @Eliminado AND Email <> @EmailSistema";

        // DS: ObtenerUsuarios(): List<Usuario>  (orden alfabético por nombre completo, CU20 paso 4)
        public async Task<List<Usuario>> ObtenerUsuarios()
        {
            var sql = $"SELECT {ColumnasListado} FROM Usuario WHERE {FiltroListado} ORDER BY Nombre, Apellido";
            var filas = await _dal.Leer<Usuario>(sql, new { Eliminado = EstadoUsuario.Eliminado, EmailSistema = EmailUsuarioSistema });
            return filas.ToList();
        }

        // DS: ObtenerUsuariosPorRol(int idRol): List<Usuario>
        public async Task<List<Usuario>> ObtenerUsuariosPorRol(int idRol)
        {
            var sql = $"SELECT {ColumnasListado} FROM Usuario WHERE {FiltroListado} AND IdRol = @IdRol ORDER BY Nombre, Apellido";
            var filas = await _dal.Leer<Usuario>(sql, new { Eliminado = EstadoUsuario.Eliminado, EmailSistema = EmailUsuarioSistema, IdRol = idRol });
            return filas.ToList();
        }

        // DS: BuscarUsuario(string dato): List<Usuario>  (por nombre, apellido o correo)
        public async Task<List<Usuario>> BuscarUsuario(string dato)
        {
            var patron = "%" + (dato ?? "").Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            var sql = $@"SELECT {ColumnasListado} FROM Usuario
                 WHERE {FiltroListado}
                   AND (Nombre LIKE @Patron OR Apellido LIKE @Patron
                        OR (Nombre + ' ' + Apellido) LIKE @Patron OR Email LIKE @Patron)
                 ORDER BY Nombre, Apellido";
            var filas = await _dal.Leer<Usuario>(sql, new { Eliminado = EstadoUsuario.Eliminado, EmailSistema = EmailUsuarioSistema, Patron = patron });
            return filas.ToList();
        }

        // DS: EliminarUsuario(int idUsuario) -> DAL.Eliminar(...): int  (baja lógica + invalida enlaces pendientes)
        public async Task<int> EliminarUsuario(int idUsuario)
        {
            const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE Usuario SET Estado = @Eliminado WHERE IdUsuario = @IdUsuario AND Estado <> @Eliminado;
            UPDATE Token SET Estado = @Invalidado WHERE UsuarioID = @IdUsuario AND Estado = @Pendiente;
            COMMIT TRANSACTION;";
            return await _dal.Eliminar(sql, new
            {
                Eliminado = EstadoUsuario.Eliminado,
                Invalidado = EstadoToken.Invalidado,
                Pendiente = EstadoToken.Pendiente,
                IdUsuario = idUsuario
            });
        }
    }
}
