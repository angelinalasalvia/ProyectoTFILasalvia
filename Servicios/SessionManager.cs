using BE;

namespace Servicios
{
    /// <summary>
    /// Singleton con la sesión del usuario autenticado (CU18).
    /// Los pasos van separados como en el DS:
    /// </summary>
    public sealed class SessionManager
    {
        private static SessionManager? _instancia;
        private static readonly object _candado = new();

        private SessionManager() { }

        public static SessionManager GetInstance()
        {
            if (_instancia is null)
            {
                lock (_candado)
                {
                    _instancia ??= new SessionManager();
                }
            }
            return _instancia;
        }

        public Usuario? Usuario { get; private set; }
        public Rol? Rol { get; private set; }
        public DateTime? FechaInicio { get; private set; }
        
        /// <summary>Se dispara al iniciar o cerrar sesión (útil para refrescar el NavMenu).</summary>
        public event Action? SesionCambiada;

        public bool EstaLogueado => Usuario is not null;

        public void Login(Usuario usuario)
        {
            ArgumentNullException.ThrowIfNull(usuario);
            lock (_candado)
            {
                Usuario = usuario;
                Rol = null;
                FechaInicio = DateTime.Now;
            }
        }

        public void CargarPermisos(Rol rol)
        {
            ArgumentNullException.ThrowIfNull(rol);
            lock (_candado)
            {
                Rol = rol;
            }
            SesionCambiada?.Invoke();
        }

        public void Logout()
        {
            lock (_candado)
            {
                Usuario = null;
                Rol = null;
                FechaInicio = null;
            }
            SesionCambiada?.Invoke();
        }

        public bool TienePermiso(string nombrePermiso)
        {
            if (!EstaLogueado || Rol is null) return false;
            return Rol.Permisos.Any(p =>
                string.Equals(p.Nombre, nombrePermiso, StringComparison.OrdinalIgnoreCase));
        }
    }
}

