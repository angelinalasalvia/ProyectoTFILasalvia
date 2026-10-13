namespace BE
{
    public class Bitacora
    {
        public int IdBitacora { get; set; }
        public string Accion { get; set; } = "";
        public DateTime Fecha { get; set; }
        public int IdUsuario { get; set; }
    }

    /// <summary>
    /// Textos de Bitacora.Accion. No hay columna contador: los intentos fallidos consecutivos son las
    /// filas con Accion = IntentoFallido. "Reiniciar el contador" las pasa a IntentoFallidoReiniciado
    /// (se conserva el registro de auditoría, pero deja de contar).
    /// </summary>
    public static class AccionBitacora
    {
        public const string InicioSesion = "Inicio de sesión";
        public const string IntentoFallido = "Intento fallido de inicio de sesión";
        public const string IntentoFallidoReiniciado = "Intento fallido de inicio de sesión (reiniciado)";
    }
}
