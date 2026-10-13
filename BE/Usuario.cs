namespace BE
{
    public class Usuario
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Apellido { get; set; } = "";
        public string Email { get; set; } = "";
        public string Contrasena { get; set; } = "";   // columna Contrasena (hash PBKDF2)
        public int IdRol { get; set; }
        public string Estado { get; set; } = "";

        public string NombreCompleto => $"{Nombre} {Apellido}";
    }

    /// <summary>Valores de Usuario.Estado. El "bloqueo" no es un estado: se deduce de los intentos fallidos (ver DS).</summary>
    public static class EstadoUsuario
    {
        public const string Activo = "Activo";
        public const string Inactivo = "Inactivo";
    }
}