using System.Security.Cryptography;

namespace Servicios
{
    /// <summary>
    /// Hash de contraseñas con PBKDF2-SHA256 + salt aleatoria (RNF: contraseñas cifradas).
    /// Formato guardado en Usuario.Contrasena: PBKDF2$iteraciones$salt(base64)$hash(base64)
    /// </summary>
    public static class CifradoService
    {
        private const string Prefijo = "PBKDF2";
        private const int Iteraciones = 100_000;
        private const int TamanoSalt = 16;
        private const int TamanoHash = 32;

        public static string Hashear(string contrasena)
        {
            if (string.IsNullOrEmpty(contrasena))
                throw new ArgumentException("La contraseña no puede estar vacía.", nameof(contrasena));

            var salt = RandomNumberGenerator.GetBytes(TamanoSalt);
            var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, salt, Iteraciones, HashAlgorithmName.SHA256, TamanoHash);
            return $"{Prefijo}${Iteraciones}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verificar(string contrasena, string almacenado)
        {
            if (string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(almacenado)) return false;

            var partes = almacenado.Split('$');
            if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones))
                return false;   // p. ej. 'HASH_TEMPORAL' del seed: nunca valida

            byte[] salt, esperado;
            try
            {
                salt = Convert.FromBase64String(partes[2]);
                esperado = Convert.FromBase64String(partes[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena, salt, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
    }
}

