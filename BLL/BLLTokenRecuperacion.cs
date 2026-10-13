using BE;
using DAL;
using Microsoft.Extensions.Configuration;

namespace BLL
{
    /// <summary>Lifeline ":TokenRecuperacion" del DS de CU19.</summary>
    public class BLLTokenRecuperacion
    {
        /// <summary>CU19 paso 6: el enlace vale 30 minutos.</summary>
        public static readonly TimeSpan ValidezEnlace = TimeSpan.FromMinutes(30);

        private readonly AccesoDatos _dal;
        private readonly string _clave;

        public BLLTokenRecuperacion(AccesoDatos dal, IConfiguration config)
        {
            _dal = dal;
            var clave = config["Seguridad:ClaveTokens"];
            if (string.IsNullOrWhiteSpace(clave) || clave.Length < 32)
                throw new InvalidOperationException(
                    "Falta 'Seguridad:ClaveTokens' en appsettings (mínimo 32 caracteres).");
            _clave = clave;
        }

        /// <summary>
        /// DS: GenerarTokenRecuperacion(email): int. Crea el token (Pendiente, 30 min), invalida los
        /// pendientes anteriores de ese usuario y devuelve el TokenID.
        /// Usa Leer con OUTPUT en lugar de Escribir porque Escribir devuelve filas afectadas, no el id.
        /// </summary>
        public async Task<int> GenerarTokenRecuperacion(string email)
        {
            const string sql = @"
            SET NOCOUNT ON;
            UPDATE t SET t.Estado = @Invalidado
            FROM Token t
            INNER JOIN Usuario u ON u.IdUsuario = t.UsuarioID
            WHERE u.Email = @Email AND t.Tipo = @Tipo AND t.Estado = @Pendiente;

            INSERT INTO Token (Estado, FechaExpiracion, Tipo, UsuarioID)
            OUTPUT INSERTED.TokenID AS Valor
            SELECT @Pendiente, @FechaExpiracion, @Tipo, IdUsuario FROM Usuario WHERE Email = @Email;";
            var filas = await _dal.Leer<ValorEscalar>(sql, new
            {
                Invalidado = EstadoToken.Invalidado,
                Pendiente = EstadoToken.Pendiente,
                Tipo = TipoToken.Recuperacion,
                FechaExpiracion = DateTime.Now.Add(ValidezEnlace),
                Email = (email ?? "").Trim()
            });

            var fila = filas.FirstOrDefault()
                ?? throw new InvalidOperationException("No se pudo generar el token: no existe el usuario.");
            return fila.Valor;
        }

        /// <summary>Arma el enlace que va en el mail: {urlBase}/restablecer-contrasena?token={TokenID.firma}</summary>
        public string ConstruirTokenLink(int tokenId, string urlBase)
        {
            var codigo = CodigoRecuperacion.Crear(tokenId, _clave);
            return $"{urlBase.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(codigo)}";
        }

        /// <summary>DS: ValidarToken(tokenLink): bool. False si la firma no es válida, expiró o ya se usó.</summary>
        public async Task<bool> ValidarToken(string tokenLink)
        {
            if (!TryObtenerId(tokenLink, out var tokenId)) return false;

            const string sql = @"
            SELECT COUNT(*) AS Valor
            FROM Token
            WHERE TokenID = @TokenID AND Tipo = @Tipo AND Estado = @Pendiente AND FechaExpiracion > @Ahora";
            var filas = await _dal.Leer<ValorEscalar>(sql, new
            {
                TokenID = tokenId,
                Tipo = TipoToken.Recuperacion,
                Pendiente = EstadoToken.Pendiente,
                Ahora = DateTime.Now
            });
            return (filas.FirstOrDefault()?.Valor ?? 0) > 0;
        }

        /// <summary>
        /// Extra (el DS no lo dibuja): ActualizarContraseña necesita el email y en el enlace solo viaja
        /// el token. Devuelve null si el enlace no es auténtico.
        /// </summary>
        public async Task<string?> ObtenerEmailDeToken(string tokenLink)
        {
            if (!TryObtenerId(tokenLink, out var tokenId)) return null;

            const string sql = @"
            SELECT u.Email
            FROM Token t
            INNER JOIN Usuario u ON u.IdUsuario = t.UsuarioID
            WHERE t.TokenID = @TokenID";
            var fila = (await _dal.Leer<Usuario>(sql, new { TokenID = tokenId })).FirstOrDefault();
            return fila?.Email;
        }

        /// <summary>DS: MarcarTokenComoUtilizado(tokenLink). Devuelve filas afectadas (0 si ya no estaba pendiente).</summary>
        public async Task<int> MarcarTokenComoUtilizado(string tokenLink)
        {
            if (!TryObtenerId(tokenLink, out var tokenId)) return 0;

            const string sql = @"
            UPDATE Token SET Estado = @Utilizado
            WHERE TokenID = @TokenID AND Estado = @Pendiente";
            return await _dal.Modificar(sql, new
            {
                Utilizado = EstadoToken.Utilizado,
                Pendiente = EstadoToken.Pendiente,
                TokenID = tokenId
            });
        }

        private bool TryObtenerId(string tokenLink, out int tokenId)
            => CodigoRecuperacion.TryLeer(CodigoRecuperacion.Extraer(tokenLink), _clave, out tokenId);
    }
}
