using System.Security.Cryptography;
using System.Text;

namespace BLL
{
    /// <summary>
    /// Arma y valida el código del enlace de recuperación: "{TokenID}.{firma}".
    /// La tabla Token no tiene una columna para un código secreto, así que el secreto
    /// es una firma HMAC-SHA256 del TokenID con una clave del servidor (Seguridad:ClaveTokens).
    /// Sin la clave no se puede fabricar un enlace válido aunque se adivine el TokenID.
    /// </summary>
    public static class CodigoRecuperacion
    {
        public static string Crear(int tokenId, string clave)
            => $"{tokenId}.{Firmar(tokenId, clave)}";

        /// <summary>
        /// El DS habla de "tokenLink". Acepta el enlace completo (.../restablecer-contrasena?token=XYZ)
        /// o solo el valor del parámetro token, y devuelve el código.
        /// </summary>
        public static string Extraer(string? tokenLink)
        {
            if (string.IsNullOrWhiteSpace(tokenLink)) return "";
            var i = tokenLink.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return tokenLink.Trim();

            var resto = tokenLink[(i + "token=".Length)..];
            var fin = resto.IndexOf('&');
            if (fin >= 0) resto = resto[..fin];
            return Uri.UnescapeDataString(resto);
        }

        public static bool TryLeer(string? codigo, string clave, out int tokenId)
        {
            tokenId = 0;
            if (string.IsNullOrWhiteSpace(codigo)) return false;

            var partes = codigo.Split('.');
            if (partes.Length != 2 || !int.TryParse(partes[0], out var id) || id <= 0) return false;

            var recibida = Encoding.UTF8.GetBytes(partes[1]);
            var esperada = Encoding.UTF8.GetBytes(Firmar(id, clave));
            if (!CryptographicOperations.FixedTimeEquals(recibida, esperada)) return false;

            tokenId = id;
            return true;
        }

        private static string Firmar(int tokenId, string clave)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"recuperacion|{tokenId}"));
            // base64 seguro para URL
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
