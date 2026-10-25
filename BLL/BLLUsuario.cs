using BE;
using DAL;

namespace BLL
{
    public class BLLUsuario
    {
        private readonly AccesoDatos _dal;

        private const string Columnas = "IdUsuario AS Id, Nombre, Apellido, Email, Contrasena, IdRol, Estado";

        public BLLUsuario(AccesoDatos dal)
        {
            _dal = dal;
        }

        public Task<Usuario?> ObtenerUsuario(string email, string contraseña)
            => ObtenerUsuario(email);


        public async Task<Usuario?> ObtenerUsuario(string email)
        {
            var sql = $"SELECT {Columnas} FROM Usuario WHERE Email = @Email AND Estado <> @Eliminado";
            var filas = await _dal.Leer<Usuario>(sql, new { Email = (email ?? "").Trim(), Eliminado = EstadoUsuario.Eliminado });
            return filas.FirstOrDefault();
        }

        public async Task ActualizarContraseña(string email, string nuevaClave)
        {
            const string sql = "UPDATE Usuario SET Contrasena = @Contrasena WHERE Email = @Email";
            await _dal.Modificar(sql, new { Contrasena = nuevaClave, Email = (email ?? "").Trim() });
        }
        
        private const string EmailUsuarioSistema = "sistema@mailtest.com";

        private const string ColumnasListado = "IdUsuario AS Id, Nombre, Apellido, Email, IdRol, Estado";
        private const string FiltroListado = "Estado <> @Eliminado AND Email <> @EmailSistema";

        
        public async Task<List<Usuario>> ObtenerUsuarios()
        {
            var sql = $"SELECT {ColumnasListado} FROM Usuario WHERE {FiltroListado} ORDER BY Nombre, Apellido";
            var filas = await _dal.Leer<Usuario>(sql, new { Eliminado = EstadoUsuario.Eliminado, EmailSistema = EmailUsuarioSistema });
            return filas.ToList();
        }

        
        public async Task<List<Usuario>> ObtenerUsuariosPorRol(int idRol)
        {
            var sql = $"SELECT {ColumnasListado} FROM Usuario WHERE {FiltroListado} AND IdRol = @IdRol ORDER BY Nombre, Apellido";
            var filas = await _dal.Leer<Usuario>(sql, new { Eliminado = EstadoUsuario.Eliminado, EmailSistema = EmailUsuarioSistema, IdRol = idRol });
            return filas.ToList();
        }

        
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

        private const string NombreRolAdministrador = "Administrador";

        public async Task<ResultadoValidacion> ValidarEliminacion(int idUsuario, int idUsuarioSesion)
        {
            var usuario = await ObtenerUsuario(idUsuario);
            if (usuario is null)
                return ResultadoValidacion.No("El usuario ya no existe.");

            if (idUsuario == idUsuarioSesion)
                return ResultadoValidacion.No("Es el usuario con el que iniciaste sesión.");
                        
            const string sqlEsAdminActivo = @"
                SELECT COUNT(*) AS Valor FROM Usuario u
                INNER JOIN Rol r ON r.IdRol = u.IdRol
                WHERE u.IdUsuario = @IdUsuario AND u.Estado = @Activo AND r.Nombre = @Rol";
            var esAdminActivo = ((await _dal.Leer<ValorEscalar>(sqlEsAdminActivo,
                new { IdUsuario = idUsuario, Activo = EstadoUsuario.Activo, Rol = NombreRolAdministrador }))
                .FirstOrDefault()?.Valor ?? 0) > 0;

            if (esAdminActivo)
            {
                const string sqlTotalAdmins = @"
                    SELECT COUNT(*) AS Valor FROM Usuario u
                    INNER JOIN Rol r ON r.IdRol = u.IdRol
                    WHERE u.Estado = @Activo AND r.Nombre = @Rol";
                var adminsActivos = (await _dal.Leer<ValorEscalar>(sqlTotalAdmins,
                    new { Activo = EstadoUsuario.Activo, Rol = NombreRolAdministrador }))
                    .FirstOrDefault()?.Valor ?? 0;

                if (adminsActivos <= 1)
                    return ResultadoValidacion.No("Es el único administrador activo.");
            }

            return ResultadoValidacion.Ok();
        }

        private const string ContrasenaPendiente = "SIN_ACCESO";

        public async Task<Usuario?> ObtenerUsuario(int idUsuario)
        {
            var sql = $"SELECT {ColumnasListado} FROM Usuario WHERE IdUsuario = @IdUsuario AND Estado <> @Eliminado";
            var filas = await _dal.Leer<Usuario>(sql, new { IdUsuario = idUsuario, Eliminado = EstadoUsuario.Eliminado });
            return filas.FirstOrDefault();
        }
        public async Task<bool> VerificarEmailRegistrado(string email)
        {
            const string sql = "SELECT COUNT(*) AS Valor FROM Usuario WHERE Email = @Email";
            var filas = await _dal.Leer<ValorEscalar>(sql, new { Email = (email ?? "").Trim() });
            return (filas.FirstOrDefault()?.Valor ?? 0) > 0;
        }

        public async Task<int> CrearUsuarioInactivo(string nombreCompleto, string email, int rol)
        {
            var (nombre, apellido) = SepararNombreCompleto(nombreCompleto);
            const string sql = @"
            INSERT INTO Usuario (Nombre, Apellido, Email, Contrasena, IdRol, Estado)
            VALUES (@Nombre, @Apellido, @Email, @Contrasena, @IdRol, @Estado)";
            return await _dal.Escribir(sql, new
            {
                Nombre = nombre,
                Apellido = apellido,
                Email = (email ?? "").Trim(),
                Contrasena = ContrasenaPendiente,
                IdRol = rol,
                Estado = EstadoUsuario.Inactivo
            });
        }

        public async Task<int> ModificarUsuario(int idUsuario, string nombreCompleto, int rol)
        {
            var actual = await ObtenerUsuario(idUsuario)
                ?? throw new InvalidOperationException("El usuario no existe.");

            var (nombre, apellido) = Normalizar(nombreCompleto) == Normalizar(actual.NombreCompleto)
                ? (actual.Nombre, actual.Apellido)
                : SepararNombreCompleto(nombreCompleto);

            const string sql = @"
            UPDATE Usuario SET Nombre = @Nombre, Apellido = @Apellido, IdRol = @IdRol
            WHERE IdUsuario = @IdUsuario AND Estado <> @Eliminado";
            return await _dal.Modificar(sql, new
            {
                Nombre = nombre,
                Apellido = apellido,
                IdRol = rol,
                IdUsuario = idUsuario,
                Eliminado = EstadoUsuario.Eliminado
            });
        }

        public async Task ActivarUsuario(string email)
        {
            const string sql = "UPDATE Usuario SET Estado = @Activo WHERE Email = @Email AND Estado = @Inactivo";
            await _dal.Modificar(sql, new
            {
                Activo = EstadoUsuario.Activo,
                Inactivo = EstadoUsuario.Inactivo,
                Email = (email ?? "").Trim()
            });
        }

        public static (string Nombre, string Apellido) SepararNombreCompleto(string nombreCompleto)
        {
            var partes = (nombreCompleto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partes.Length < 2) return (partes.FirstOrDefault() ?? "", "");
            return (string.Join(' ', partes[..^1]), partes[^1]);
        }

        private static string Normalizar(string texto)
            => string.Join(' ', (texto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();
    }
    public record ResultadoValidacion(bool Valido, string Motivo = "")
    {
        public static ResultadoValidacion Ok() => new(true);
        public static ResultadoValidacion No(string motivo) => new(false, motivo);
    }
}
