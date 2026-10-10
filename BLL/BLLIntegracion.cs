using System.Security.Cryptography;
using BE;
using DAL;
using Microsoft.AspNetCore.DataProtection;

namespace BLL;

public class BLLIntegracion
{
    public const string SecretoEnmascarado = "••••••••••";

    private const string PrefijoCifrado = "enc:";
    private const string EmailUsuarioSistema = "sistema@mailtest.com";

    private readonly AccesoDatos _accesoDatos;
    private readonly IDataProtector _protector;
    private readonly ImportadorDatos _importador;

    public BLLIntegracion(AccesoDatos accesoDatos, IDataProtectionProvider proveedorProteccion)
    {
        _accesoDatos = accesoDatos;
        _protector = proveedorProteccion.CreateProtector("TFI.Integraciones.Credenciales");
        _importador = new ImportadorDatos(accesoDatos);
    }

    public async Task<List<Integracion>> ObtenerIntegraciones(CancellationToken ct = default)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            @"SELECT IdIntegracion, Nombre, FechaCreacion, Tipo, Estado, IdUsuario,
                     UltimaSincronizacion, CantidadRegistros, MensajeError
              FROM Integracion
              ORDER BY Nombre", ct: ct);
        return filas.ToList();
    }

    public async Task<Integracion?> ObtenerIntegracion(int idIntegracion, CancellationToken ct = default)
    {
        var integracion = await LeerCompleta(idIntegracion, ct);
        if (integracion is not null && !string.IsNullOrEmpty(integracion.Secreto))
            integracion.Secreto = SecretoEnmascarado;
        return integracion;
    }
    public async Task<MapeoIntegracion> ObtenerMapeoActual(int idIntegracion, CancellationToken ct = default)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            "SELECT Mapeo FROM Integracion WHERE IdIntegracion = @idIntegracion",
            new { idIntegracion }, ct: ct);

        return MapeoIntegracion.Deserializar(filas.FirstOrDefault()?.Mapeo);
    }

    public async Task<ResultadoConexion> ProbarConexion(string tipo, string credenciales,
        int? idIntegracion = null, CancellationToken ct = default)
    {
        try
        {
            var c = await ResolverCredenciales(credenciales, idIntegracion, ct);
            return await ConectorFuente.Probar(tipo, c, ct);
        }
        catch (CryptographicException)
        {
            return new ResultadoConexion(false, "No se pudo leer la clave guardada. Volvé a ingresarla.");
        }
    }

    public async Task<List<string>> ObtenerEsquemaCampos(string tipo, string credenciales, int? idIntegracion = null, CancellationToken ct = default)
    {
        var c = await ResolverCredenciales(credenciales, idIntegracion, ct);
        var campos = await ConectorFuente.ObtenerEsquema(tipo, c, ct);
        return campos.Select(f => $"{f.Nombre}|{f.Tipo}").ToList();
    }

    public async Task<List<string>> ObtenerValoresDistintos(string tipo, string credenciales, string campoExterno,
        int? idIntegracion = null, CancellationToken ct = default)
    {
        var c = await ResolverCredenciales(credenciales, idIntegracion, ct);
        return await ConectorFuente.ObtenerValoresDistintos(tipo, c, campoExterno, ct);
    }

    public async Task<int> RegistrarNuevaIntegracion(string tipo, string credenciales, string mapeo, string estado,
        int? idUsuario = null, CancellationToken ct = default)
    {
        var c = CredencialesIntegracion.Deserializar(credenciales);
        var idCreador = idUsuario ?? await ObtenerIdUsuarioSistema(ct);

        string? servidor = c.Url;
        string? puerto = null;
        if (tipo == Integracion.TipoBD) (servidor, puerto) = SepararPuerto(c.Url);

        return await _accesoDatos.Escribir($@"
            INSERT INTO Integracion (Nombre, FechaCreacion, Tipo, Estado, IdUsuario, Mapeo)
            VALUES (@nombre, GETDATE(), @tipo, @estado, @idUsuario, @mapeo);
            DECLARE @id int = SCOPE_IDENTITY();
            {SqlAltaCredenciales(tipo)}",
            new
            {
                nombre = c.Nombre,
                tipo,
                estado,
                idUsuario = idCreador,
                mapeo,
                url = c.Url,
                urlEventos = c.UrlEventos,
                servidor,
                puerto,
                usuario = c.Usuario,
                secreto = Cifrar(c.Secreto),
                baseDatos = c.BaseDatos
            }, ct: ct);
    }

    public async Task<int> ActualizarConfiguracion(int id, string credenciales, string mapeo, string estado,
        CancellationToken ct = default)
    {
        var actual = await LeerCompleta(id, ct)
            ?? throw new InvalidOperationException($"No existe la integración {id}.");
        var c = CredencialesIntegracion.Deserializar(credenciales);

        string? servidor = c.Url;
        string? puerto = null;
        if (actual.Tipo == Integracion.TipoBD) (servidor, puerto) = SepararPuerto(c.Url);

        bool secretoSinCambios = string.IsNullOrEmpty(c.Secreto) || c.Secreto == SecretoEnmascarado;

        return await _accesoDatos.Modificar($@"
            UPDATE Integracion
               SET Nombre = @nombre, Estado = @estado, Mapeo = @mapeo,
                   MensajeError = CASE WHEN @estado = '{Integracion.EstadoActiva}' THEN NULL ELSE MensajeError END
             WHERE IdIntegracion = @id;
            {SqlModificacionCredenciales(actual.Tipo)}",
            new
            {
                id,
                nombre = c.Nombre,
                estado,
                mapeo,
                url = c.Url,
                urlEventos = c.UrlEventos,
                servidor,
                puerto,
                usuario = c.Usuario,
                secreto = secretoSinCambios ? null : Cifrar(c.Secreto),   // null = no tocar la columna
                baseDatos = c.BaseDatos
            }, ct: ct);
    }

    public Task<int> EliminarIntegracion(int idIntegracion, CancellationToken ct = default)
        => _accesoDatos.Eliminar(
            @"UPDATE Cliente SET IdIntegracion = NULL WHERE IdIntegracion = @idIntegracion;
              DELETE FROM IntegracionBD  WHERE IntegracionID = @idIntegracion;
              DELETE FROM IntegracionAPI WHERE IntegracionID = @idIntegracion;
              DELETE FROM IntegracionCRM WHERE IntegracionID = @idIntegracion;
              DELETE FROM Integracion    WHERE IdIntegracion = @idIntegracion;",
            new { idIntegracion }, ct: ct);

    public async Task<ResultadoSincronizacion> ActualizarInformacionIntegracion(int idIntegracion, CancellationToken ct = default)
    {
        var integracion = await LeerCompleta(idIntegracion, ct)
            ?? throw new InvalidOperationException($"No existe la integración {idIntegracion}.");

        var mapeo = MapeoIntegracion.Deserializar(integracion.Mapeo);
        if (string.IsNullOrEmpty(mapeo.OrigenClientes) || string.IsNullOrEmpty(mapeo.OrigenEventos))
            return new ResultadoSincronizacion(false,
                "No fue posible completar la sincronización: la integración no tiene mapeo de clientes y eventos. Editala y guardá el mapeo.", 0);

        // ---- 1) lectura de la fuente
        List<Dictionary<string, string?>> filasClientes, filasEventos;
        try
        {
            var c = new CredencialesIntegracion
            {
                Nombre = integracion.Nombre,
                Url = integracion.Url ?? "",
                UrlEventos = integracion.UrlEventos,
                Usuario = integracion.Usuario ?? "",
                Secreto = string.IsNullOrEmpty(integracion.Secreto) ? "" : Descifrar(integracion.Secreto),
                BaseDatos = integracion.BaseDatos
            };

            var prueba = await ConectorFuente.Probar(integracion.Tipo, c, ct);
            if (!prueba.Exito) return await RegistrarFalloSincronizacion(idIntegracion, prueba.Mensaje, ct);

            filasClientes = await ConectorFuente.LeerFilas(integracion.Tipo, c, mapeo.OrigenClientes, mapeo.Clientes.Values.ToList(), ct);
            filasEventos = await ConectorFuente.LeerFilas(integracion.Tipo, c, mapeo.OrigenEventos, mapeo.Eventos.Values.ToList(), ct);
        }
        catch (CryptographicException)
        {
            return await RegistrarFalloSincronizacion(idIntegracion, "No se pudo leer la clave guardada. Volvé a ingresarla.", ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return await RegistrarFalloSincronizacion(idIntegracion, ConectorFuente.Describir(ex), ct);
        }

        // ---- 2) traducción e importación
        try
        {
            var datos = TransformadorMapeo.Normalizar(mapeo, filasClientes, filasEventos);
            var importado = await _importador.Importar(idIntegracion, mapeo, datos, ct);

            // ---- 3) estado de la integración
            var registros = filasClientes.Count + filasEventos.Count;
            await _accesoDatos.Modificar(
                @"UPDATE Integracion
                     SET UltimaSincronizacion = GETDATE(), CantidadRegistros = @registros,
                         Estado = @activa, MensajeError = NULL
                   WHERE IdIntegracion = @idIntegracion",
                new { idIntegracion, registros, activa = Integracion.EstadoActiva }, ct: ct);

            var descartados = datos.EventosInvalidos + importado.EventosSinCliente;
            var mensaje = $"Sincronización finalizada con éxito. Clientes: {importado.ClientesNuevos} nuevos, " +
                          $"{importado.ClientesActualizados} actualizados. Eventos: {importado.EventosNuevos} nuevos" +
                          (descartados > 0 ? $" ({descartados} descartados por no tener cliente o una fecha válida)." : ".");

            return new ResultadoSincronizacion(true, mensaje, registros);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ResultadoSincronizacion(false,
                "No fue posible completar la sincronización: no se pudieron guardar los datos importados. " + ex.Message, 0);
        }
    }

    
    /*
    public async Task<int> SincronizarPendientes(CancellationToken ct = default)
    {
        var vencidas = (await ObtenerIntegraciones(ct))
            .Where(i => i.Estado == Integracion.EstadoActiva &&
                        (i.UltimaSincronizacion == null || DateTime.Now - i.UltimaSincronizacion.Value >= IntervaloSincronizacion))
            .ToList();

        foreach (var integracion in vencidas)
            await ActualizarInformacionIntegracion(integracion.IdIntegracion, ct);

        return vencidas.Count;
    }
    */
    private async Task<ResultadoSincronizacion> RegistrarFalloSincronizacion(int idIntegracion, string motivo, CancellationToken ct)
    {
        var motivoCorto = motivo.Length > 500 ? motivo[..500] : motivo;
        await _accesoDatos.Modificar(
            "UPDATE Integracion SET Estado = @error, MensajeError = @motivo WHERE IdIntegracion = @idIntegracion",
            new { idIntegracion, error = Integracion.EstadoError, motivo = motivoCorto }, ct: ct);

        return new ResultadoSincronizacion(false, "No fue posible completar la sincronización: " + motivo, 0);
    }

    private async Task<Integracion?> LeerCompleta(int idIntegracion, CancellationToken ct)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            @"SELECT i.IdIntegracion, i.Nombre, i.FechaCreacion, i.Tipo, i.Estado, i.IdUsuario,
                     i.UltimaSincronizacion, i.CantidadRegistros, i.Mapeo, i.MensajeError,
                     COALESCE(bd.Servidor + CASE WHEN bd.Puerto IS NULL OR bd.Puerto = '' THEN '' ELSE ',' + bd.Puerto END,
                              api.Endpoint, crm.UrlServidor) AS Url,
                     api.EndpointEventos AS UrlEventos,
                     COALESCE(bd.Usuario, crm.ClienteID) AS Usuario,
                     COALESCE(bd.[Password], api.ApiKey, crm.ClientSecret) AS Secreto,
                     bd.BaseDatos AS BaseDatos
              FROM Integracion i
              LEFT JOIN IntegracionBD  bd  ON bd.IntegracionID  = i.IdIntegracion
              LEFT JOIN IntegracionAPI api ON api.IntegracionID = i.IdIntegracion
              LEFT JOIN IntegracionCRM crm ON crm.IntegracionID = i.IdIntegracion
              WHERE i.IdIntegracion = @idIntegracion",
            new { idIntegracion }, ct: ct);
        return filas.FirstOrDefault();
    }

    private async Task<CredencialesIntegracion> ResolverCredenciales(string credenciales, int? idIntegracion, CancellationToken ct)
    {
        var c = CredencialesIntegracion.Deserializar(credenciales);
        if (idIntegracion is not null && (string.IsNullOrEmpty(c.Secreto) || c.Secreto == SecretoEnmascarado))
        {
            var guardada = await LeerCompleta(idIntegracion.Value, ct);
            c.Secreto = string.IsNullOrEmpty(guardada?.Secreto) ? "" : Descifrar(guardada.Secreto);
        }
        return c;
    }

    private static string SqlAltaCredenciales(string tipo) => tipo switch
    {
        Integracion.TipoBD =>
            "INSERT INTO IntegracionBD (BaseDatos, Puerto, Servidor, Usuario, IntegracionID, [Password]) VALUES (@baseDatos, @puerto, @servidor, @usuario, @id, @secreto);",
        Integracion.TipoAPI =>
            "INSERT INTO IntegracionAPI (ApiKey, IntegracionID, Endpoint, EndpointEventos) VALUES (@secreto, @id, @url, @urlEventos);",
        Integracion.TipoCRM =>
            "INSERT INTO IntegracionCRM (ClienteID, ClientSecret, IntegracionID, UrlServidor) VALUES (@usuario, @secreto, @id, @url);",
        _ => throw new ArgumentException($"Tipo de integración desconocido: {tipo}")
    };

    private static string SqlModificacionCredenciales(string tipo) => tipo switch
    {
        Integracion.TipoBD =>
            "UPDATE IntegracionBD SET BaseDatos = @baseDatos, Puerto = @puerto, Servidor = @servidor, Usuario = @usuario, [Password] = COALESCE(@secreto, [Password]) WHERE IntegracionID = @id;",
        Integracion.TipoAPI =>
            "UPDATE IntegracionAPI SET Endpoint = @url, EndpointEventos = @urlEventos, ApiKey = COALESCE(@secreto, ApiKey) WHERE IntegracionID = @id;",
        Integracion.TipoCRM =>
            "UPDATE IntegracionCRM SET UrlServidor = @url, ClienteID = @usuario, ClientSecret = COALESCE(@secreto, ClientSecret) WHERE IntegracionID = @id;",
        _ => throw new ArgumentException($"Tipo de integración desconocido: {tipo}")
    };

    private static (string Servidor, string? Puerto) SepararPuerto(string url)
    {
        var i = url.LastIndexOf(',');
        if (i > 0 && int.TryParse(url[(i + 1)..], out _))
            return (url[..i], url[(i + 1)..]);
        return (url, null);
    }

    private string Cifrar(string valor) => PrefijoCifrado + _protector.Protect(valor);

    private string Descifrar(string valor) =>
        valor.StartsWith(PrefijoCifrado) ? _protector.Unprotect(valor[PrefijoCifrado.Length..]) : valor;

    private async Task<int> ObtenerIdUsuarioSistema(CancellationToken ct)
    {
        var usuario = (await _accesoDatos.Leer<Integracion>(
            "SELECT IdUsuario FROM Usuario WHERE Email = @email",
            new { email = EmailUsuarioSistema }, ct: ct)).FirstOrDefault();

        return usuario?.IdUsuario
            ?? throw new InvalidOperationException($"Falta el usuario del sistema ({EmailUsuarioSistema}).");
    }
}
