using BE;
using DAL;

namespace BLL
{
    /// <summary>Lifeline ":Bitácora" del DS de CU18.</summary>
    public class BLLBitacora
    {
        /// <summary>Con 3 intentos fallidos consecutivos la cuenta queda bloqueada.</summary>
        public const int MaxIntentosFallidos = 3;

        private readonly AccesoDatos _dal;

        public BLLBitacora(AccesoDatos dal)
        {
            _dal = dal;
        }

        public async Task<int> ObtenerIntentosFallidosConsecutivos(string email)
        {
            const string sql = @"
SELECT COUNT(*) AS Valor
FROM Bitacora b
INNER JOIN Usuario u ON u.IdUsuario = b.IdUsuario
WHERE u.Email = @Email AND b.Accion = @Accion";
            var filas = await _dal.Leer<ValorEscalar>(sql, new
            {
                Email = (email ?? "").Trim(),
                Accion = AccionBitacora.IntentoFallido
            });
            return filas.FirstOrDefault()?.Valor ?? 0;
        }

        /// <summary>Registra el intento y devuelve cuántos intentos consecutivos lleva (DS: Escribir + :int).</summary>
        public async Task<int> RegistrarIntentoFallido(string email)
        {
            const string sql = @"
INSERT INTO Bitacora (Accion, Fecha, IdUsuario)
SELECT @Accion, @Fecha, IdUsuario FROM Usuario WHERE Email = @Email";
            await _dal.Escribir(sql, new
            {
                Accion = AccionBitacora.IntentoFallido,
                Fecha = DateTime.Now,
                Email = (email ?? "").Trim()
            });
            return await ObtenerIntentosFallidosConsecutivos(email!);
        }

        /// <summary>DS: Modificar(...) : int. Los intentos fallidos dejan de contar, pero siguen en la bitácora.</summary>
        public async Task ReiniciarContadorIntentosFallidos(string email)
        {
            const string sql = @"
UPDATE b SET b.Accion = @Reiniciado
FROM Bitacora b
INNER JOIN Usuario u ON u.IdUsuario = b.IdUsuario
WHERE u.Email = @Email AND b.Accion = @Fallido";
            await _dal.Modificar(sql, new
            {
                Reiniciado = AccionBitacora.IntentoFallidoReiniciado,
                Fallido = AccionBitacora.IntentoFallido,
                Email = (email ?? "").Trim()
            });
        }

        /// <summary>Está en el diagrama de clases y en el CU18 (paso 12), aunque el DS no lo dibuja.</summary>
        public async Task RegistrarInicioSesion(int idUsuario, string accion, DateTime fecha)
        {
            const string sql = "INSERT INTO Bitacora (Accion, Fecha, IdUsuario) VALUES (@Accion, @Fecha, @IdUsuario)";
            await _dal.Escribir(sql, new { Accion = accion, Fecha = fecha, IdUsuario = idUsuario });
        }
    }
}
