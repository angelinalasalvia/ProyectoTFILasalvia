namespace BE
{
    public class Rol
    {
        public int IdRol { get; set; }
        public string Nombre { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public List<Permiso> Permisos { get; set; } = new();
    }
}
