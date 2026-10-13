namespace DAL
{
    /// <summary>
    /// AccesoDatos.Leer&lt;T&gt; mapea columnas a propiedades por nombre. Para un COUNT(*) o un id
    /// devuelto por OUTPUT se usa esta clase con una columna llamada "Valor".
    /// </summary>
    public class ValorEscalar
    {
        public int Valor { get; set; }
    }
}