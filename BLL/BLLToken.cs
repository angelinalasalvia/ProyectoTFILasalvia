using BE;
using DAL;
using Microsoft.Extensions.Configuration;

namespace BLL
{
    
    public class BLLToken
    {        
        public static readonly TimeSpan ValidezRecuperacion = TimeSpan.FromMinutes(30);

        public static readonly TimeSpan ValidezActivacion = TimeSpan.FromHours(24);

        private readonly AccesoDatos _dal;
        private readonly string _clave;

        public BLLToken(AccesoDatos dal, IConfiguration config)
        {
            _dal = dal;
            var clave = config["Seguridad:ClaveTokens"];
            if (string.IsNullOrWhiteSpace(clave) || clave.Length < 32)
                throw new InvalidOperationException(
                    "Falta 'Seguridad:ClaveTokens' en appsettings (mínimo 32 caracteres).");
            _clave = clave;
        }

        private async Task<int> GenerarToken(string email, string tipo, TimeSpan validez)
        {
            const string sql = @"
            SET NOCOUNT ON;
            UPDATE t SET t.Estado = @Invalidado
            FROM Token t
            INNER JOIN Usuario u ON u.IdUsuario = t.UsuarioID
            WHERE u.Email = @Email AND u.Estado <> @Eliminado AND t.Tipo = @Tipo AND t.Estado = @Pendiente;

            INSERT INTO Token (Estado, FechaExpiracion, Tipo, UsuarioID)
            OUTPUT INSERTED.TokenID AS Valor
            SELECT @Pendiente, @FechaExpiracion, @Tipo, IdUsuario
            FROM Usuario WHERE Email = @Email AND Estado <> @Eliminado;";

            var filas = await _dal.Leer<ValorEscalar>(sql, new
            {
                Invalidado = EstadoToken.Invalidado,
                Pendiente = EstadoToken.Pendiente,
                Tipo = tipo,
                FechaExpiracion = DateTime.Now.Add(validez),
                Email = (email ?? "").Trim(),
                Eliminado = EstadoUsuario.Eliminado
            });

            var fila = filas.FirstOrDefault()
                ?? throw new InvalidOperationException("No se pudo generar el token: no existe el usuario.");
            return fila.Valor;
        }

        public Task<int> GenerarTokenRecuperacion(string email)
            => GenerarToken(email, TipoToken.Recuperacion, ValidezRecuperacion);

        public async Task<string> GenerarTokenActivacion(string email, string urlBase)
        {
            var tokenId = await GenerarToken(email, TipoToken.Activacion, ValidezActivacion);
            return ConstruirTokenLink(tokenId, urlBase);
        }

        public string ConstruirTokenLink(int tokenId, string urlBase)
        {
            var codigo = CodigoRecuperacion.Crear(tokenId, _clave);
            return $"{urlBase.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(codigo)}";
        }

        public async Task<bool> ValidarToken(string tokenLink)
        {
            if (!TryObtenerId(tokenLink, out var tokenId)) return false;

            const string sql = @"
            SELECT COUNT(*) AS Valor
            FROM Token
            WHERE TokenID = @TokenID AND Tipo IN (@Recuperacion, @Activacion)
              AND Estado = @Pendiente AND FechaExpiracion > @Ahora";
            var filas = await _dal.Leer<ValorEscalar>(sql, new
            {
                TokenID = tokenId,
                Recuperacion = TipoToken.Recuperacion,
                Activacion = TipoToken.Activacion,
                Pendiente = EstadoToken.Pendiente,
                Ahora = DateTime.Now
            });
            return (filas.FirstOrDefault()?.Valor ?? 0) > 0;
        }

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
