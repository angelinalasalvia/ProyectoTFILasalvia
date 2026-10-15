using BE;
using DAL;
using Microsoft.Extensions.Configuration;

namespace BLL;

public class BLLTokenActivacion
{
    /// <summary>CU21 paso 12: el enlace vale 24 horas.</summary>
    public static readonly TimeSpan ValidezEnlace = TimeSpan.FromHours(24);

    private readonly AccesoDatos _dal;
    private readonly string _clave;

    public BLLTokenActivacion(AccesoDatos dal, IConfiguration config)
    {
        _dal = dal;
        var clave = config["Seguridad:ClaveTokens"];
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 32)
            throw new InvalidOperationException(
                "Falta 'Seguridad:ClaveTokens' en appsettings (mínimo 32 caracteres).");
        _clave = clave;
    }

    // DS: GenerarTokenActivacion(string email): string  -> devuelve el tokenLink.
    // Invalida los enlaces de activación pendientes anteriores (sirve para el reenvío).
    public async Task<string> GenerarTokenActivacion(string email, string urlBase)
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
            Tipo = TipoToken.Activacion,
            FechaExpiracion = DateTime.Now.Add(ValidezEnlace),
            Email = (email ?? "").Trim(),
            Eliminado = EstadoUsuario.Eliminado
        });

        var fila = filas.FirstOrDefault()
            ?? throw new InvalidOperationException("No se pudo generar el token: no existe el usuario.");

        var codigo = CodigoRecuperacion.Crear(fila.Valor, _clave);
        return $"{urlBase.TrimEnd('/')}/restablecer-contrasena?token={Uri.EscapeDataString(codigo)}";
    }
}
