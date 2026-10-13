namespace BE
{
    public class Token
    {
        public int TokenID { get; set; }
        public string Estado { get; set; } = "";
        public DateTime FechaExpiracion { get; set; }
        public string Tipo { get; set; } = "";
        public int UsuarioID { get; set; }
    }

    public static class EstadoToken
    {
        public const string Pendiente = "Pendiente";
        public const string Utilizado = "Utilizado";
        public const string Invalidado = "Invalidado";   // reemplazado por un enlace más nuevo
    }

    public static class TipoToken
    {
        public const string Recuperacion = "Recuperacion";
    }
}
