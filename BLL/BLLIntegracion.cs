using BE;
using DAL;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace BLL;

// Casos de uso CU14 (gestión), CU15 (nueva integración) y CU16 (editar credenciales).
// Los nombres de los métodos públicos son los de los diagramas de secuencia.
// Quedan para el próximo bloque: ObtenerEsquemaCampos y ActualizarInformacionIntegracion (necesitan los conectores reales).
public class BLLIntegracion
{
    public const string SecretoEnmascarado = "••••••••••";

    // Cada cuánto debería sincronizarse una integración activa (lo usa la métrica "próxima sincronización").
    public static readonly TimeSpan IntervaloSincronizacion = TimeSpan.FromMinutes(15);
    public static readonly IReadOnlyDictionary<string, string> CamposPlataforma = new Dictionary<string, string>
    {
        ["Nombre"] = "texto",
        ["Apellido"] = "texto",
        ["Email"] = "texto",
        ["Telefono"] = "texto",
        ["Plan"] = "texto",
        ["Sede"] = "texto",
        ["Estado"] = "texto",
        ["FechaInicio"] = "fecha"
    };

    private const string PrefijoCifrado = "enc:";
    private const string EmailUsuarioSistema = "sistema@mailtest.com";

    private readonly AccesoDatos _accesoDatos;
    private readonly IDataProtector _protector;

    public BLLIntegracion(AccesoDatos accesoDatos, IDataProtectionProvider proveedorProteccion)
    {
        _accesoDatos = accesoDatos;
        _protector = proveedorProteccion.CreateProtector("TFI.Integraciones.Credenciales");
    }

    // Filas auxiliares para leer resultados de conteo con AccesoDatos.Leer<T>.
    public class ConteoDependencias
    {
        public int OtrasActivas { get; set; }
        public int EsActiva { get; set; }
    }

    // ------------------------------------------------------------------------------------
    // CU14 paso 2: listado (sin credenciales).
    // ------------------------------------------------------------------------------------
    public async Task<List<Integracion>> ObtenerIntegraciones(CancellationToken ct = default)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            @"SELECT IdIntegracion, Nombre, FechaCreacion, Tipo, Estado, IdUsuario,
                     UltimaSincronizacion, CantidadRegistros, MensajeError
              FROM Integracion
              ORDER BY Nombre", ct: ct);
        return filas.ToList();
    }

    // ------------------------------------------------------------------------------------
    // CU16 paso 2: una integración con sus credenciales. El secreto SIEMPRE sale enmascarado.
    // ------------------------------------------------------------------------------------
    public async Task<Integracion?> ObtenerIntegracion(int idIntegracion, CancellationToken ct = default)
    {
        var integracion = await LeerCompleta(idIntegracion, ct);
        if (integracion is not null && !string.IsNullOrEmpty(integracion.Secreto))
            integracion.Secreto = SecretoEnmascarado;
        return integracion;
    }

    // CU16 paso 10: mapeo actual, un elemento por campo con el formato "externo=plataforma".
    public async Task<List<string>> ObtenerMapeoActual(int idIntegracion, CancellationToken ct = default)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            "SELECT Mapeo FROM Integracion WHERE IdIntegracion = @idIntegracion",
            new { idIntegracion }, ct: ct);

        var mapeo = filas.FirstOrDefault()?.Mapeo;
        return string.IsNullOrWhiteSpace(mapeo)
            ? new List<string>()
            : mapeo.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // Lo usa TestearConexion de la pantalla (CU15 paso 7 / CU16 paso 8).
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

    // CU15 paso 9. Devuelve "nombre|tipo" por campo, donde tipo es texto, fecha, numero u otro.
    public async Task<List<string>> ObtenerEsquemaCampos(string tipo, string credenciales,
        int? idIntegracion = null, CancellationToken ct = default)
    {
        var c = await ResolverCredenciales(credenciales, idIntegracion, ct);
        var campos = await ConectorFuente.ObtenerEsquema(tipo, c, ct);
        return campos.Select(f => $"{f.Nombre}|{f.Tipo}").ToList();
    }

    // En edición el secreto llega enmascarado: se usa el que está guardado (descifrado).
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

    // ------------------------------------------------------------------------------------
    // CU15 paso 14. 'credenciales' es un CredencialesIntegracion serializado (incluye el nombre).
    // 'mapeo' llega como "externo=plataforma;externo=plataforma". Se guarda todo en una sola
    // transacción (AccesoDatos.Escribir ya la abre). 'idUsuario' es opcional mientras no haya
    // sesión: si no se informa se usa el usuario del sistema, igual que el motor de reglas.
    // ------------------------------------------------------------------------------------
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
                servidor,
                puerto,
                usuario = c.Usuario,
                secreto = Cifrar(c.Secreto),
                baseDatos = c.BaseDatos
            }, ct: ct);
    }

    // ------------------------------------------------------------------------------------
    // CU16 paso 14. El tipo no se puede cambiar. Si el secreto llega vacío o enmascarado se
    // conserva el que ya estaba guardado.
    // ------------------------------------------------------------------------------------
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
                servidor,
                puerto,
                usuario = c.Usuario,
                secreto = secretoSinCambios ? null : Cifrar(c.Secreto),   // null = no tocar la columna
                baseDatos = c.BaseDatos
            }, ct: ct);
    }

    // ------------------------------------------------------------------------------------
    // CU14 paso 12 (camino alterno 12.1). true = POSEE dependencias activas, o sea NO se puede eliminar.
    // Regla del MVP: no se puede eliminar la única integración que está en "Conexión Activa".
    // ------------------------------------------------------------------------------------
    public async Task<bool> ValidarDependenciasActivas(int idIntegracion, CancellationToken ct = default)
    {
        var conteo = (await _accesoDatos.Leer<ConteoDependencias>(
            @"SELECT COUNT(CASE WHEN IdIntegracion <> @idIntegracion THEN 1 END) AS OtrasActivas,
                     COUNT(CASE WHEN IdIntegracion =  @idIntegracion THEN 1 END) AS EsActiva
              FROM Integracion
              WHERE Estado = @activa",
            new { idIntegracion, activa = Integracion.EstadoActiva }, ct: ct)).First();

        return conteo.EsActiva > 0 && conteo.OtrasActivas == 0;
    }

    // CU14 paso 12: borra las credenciales del tipo y la integración, todo en una transacción.
    public Task<int> EliminarIntegracion(int idIntegracion, CancellationToken ct = default)
        => _accesoDatos.Eliminar(
            @"DELETE FROM IntegracionBD  WHERE IntegracionID = @idIntegracion;
              DELETE FROM IntegracionAPI WHERE IntegracionID = @idIntegracion;
              DELETE FROM IntegracionCRM WHERE IntegracionID = @idIntegracion;
              DELETE FROM Integracion    WHERE IdIntegracion = @idIntegracion;",
            new { idIntegracion }, ct: ct);

    // CU14 paso 8 (diagrama: ActualizarInformacionIntegracion(): resultadoSincronizacion).
    // Reconecta con la fuente, cuenta los registros mapeados y actualiza la integración.
    // Si falla, la integración queda en "Error de Autenticación" con el motivo guardado
    // (la última sincronización exitosa se conserva).
    public async Task<ResultadoSincronizacion> ActualizarInformacionIntegracion(int idIntegracion, CancellationToken ct = default)
    {
        var integracion = await LeerCompleta(idIntegracion, ct)
            ?? throw new InvalidOperationException($"No existe la integración {idIntegracion}.");

        try
        {
            var c = new CredencialesIntegracion
            {
                Nombre = integracion.Nombre,
                Url = integracion.Url ?? "",
                Usuario = integracion.Usuario ?? "",
                Secreto = string.IsNullOrEmpty(integracion.Secreto) ? "" : Descifrar(integracion.Secreto),
                BaseDatos = integracion.BaseDatos
            };

            var prueba = await ConectorFuente.Probar(integracion.Tipo, c, ct);
            if (!prueba.Exito) return await RegistrarFalloSincronizacion(idIntegracion, prueba.Mensaje, ct);

            var camposMapeados = (integracion.Mapeo ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(par => par.Split('=')[0])
                .ToList();

            var registros = await ConectorFuente.ContarRegistros(integracion.Tipo, c, camposMapeados, ct);

            await _accesoDatos.Modificar(
                @"UPDATE Integracion
                 SET UltimaSincronizacion = GETDATE(), CantidadRegistros = @registros,
                     Estado = @activa, MensajeError = NULL
               WHERE IdIntegracion = @idIntegracion",
                new { idIntegracion, registros, activa = Integracion.EstadoActiva }, ct: ct);

            return new ResultadoSincronizacion(true, "Sincronización finalizada con éxito.", registros);
        }
        catch (CryptographicException)
        {
            return await RegistrarFalloSincronizacion(idIntegracion, "No se pudo leer la clave guardada. Volvé a ingresarla.", ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return await RegistrarFalloSincronizacion(idIntegracion, ConectorFuente.Describir(ex), ct);
        }
    }

    // Lo usa el servicio en segundo plano: sincroniza las integraciones activas cuya última sincronización venció.
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

    private async Task<ResultadoSincronizacion> RegistrarFalloSincronizacion(int idIntegracion, string motivo, CancellationToken ct)
    {
        var motivoCorto = motivo.Length > 500 ? motivo[..500] : motivo;
        await _accesoDatos.Modificar(
            "UPDATE Integracion SET Estado = @error, MensajeError = @motivo WHERE IdIntegracion = @idIntegracion",
            new { idIntegracion, error = Integracion.EstadoError, motivo = motivoCorto }, ct: ct);

        return new ResultadoSincronizacion(false, "No fue posible completar la sincronización: " + motivo, 0);
    }
    // ------------------------------------------------------------------------------------
    // Internos
    // ------------------------------------------------------------------------------------
    private async Task<Integracion?> LeerCompleta(int idIntegracion, CancellationToken ct)
    {
        var filas = await _accesoDatos.Leer<Integracion>(
            @"SELECT i.IdIntegracion, i.Nombre, i.FechaCreacion, i.Tipo, i.Estado, i.IdUsuario,
                     i.UltimaSincronizacion, i.CantidadRegistros, i.Mapeo, i.MensajeError,
                     COALESCE(bd.Servidor + CASE WHEN bd.Puerto IS NULL OR bd.Puerto = '' THEN '' ELSE ',' + bd.Puerto END,
                              api.Endpoint, crm.UrlServidor) AS Url,
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

    private static string SqlAltaCredenciales(string tipo) => tipo switch
    {
        Integracion.TipoBD =>
            "INSERT INTO IntegracionBD (BaseDatos, Puerto, Servidor, Usuario, IntegracionID, [Password]) VALUES (@baseDatos, @puerto, @servidor, @usuario, @id, @secreto);",
        Integracion.TipoAPI =>
            "INSERT INTO IntegracionAPI (ApiKey, IntegracionID, Endpoint) VALUES (@secreto, @id, @url);",
        Integracion.TipoCRM =>
            "INSERT INTO IntegracionCRM (ClienteID, ClientSecret, IntegracionID, UrlServidor) VALUES (@usuario, @secreto, @id, @url);",
        _ => throw new ArgumentException($"Tipo de integración desconocido: {tipo}")
    };

    // @secreto = NULL deja la columna como estaba (COALESCE con el valor actual).
    private static string SqlModificacionCredenciales(string tipo) => tipo switch
    {
        Integracion.TipoBD =>
            "UPDATE IntegracionBD SET BaseDatos = @baseDatos, Puerto = @puerto, Servidor = @servidor, Usuario = @usuario, [Password] = COALESCE(@secreto, [Password]) WHERE IntegracionID = @id;",
        Integracion.TipoAPI =>
            "UPDATE IntegracionAPI SET Endpoint = @url, ApiKey = COALESCE(@secreto, ApiKey) WHERE IntegracionID = @id;",
        Integracion.TipoCRM =>
            "UPDATE IntegracionCRM SET UrlServidor = @url, ClienteID = @usuario, ClientSecret = COALESCE(@secreto, ClientSecret) WHERE IntegracionID = @id;",
        _ => throw new ArgumentException($"Tipo de integración desconocido: {tipo}")
    };

    // "host,1433" -> ("host", "1433"). Si no termina en ",número" se devuelve todo como servidor.
    private static (string Servidor, string? Puerto) SepararPuerto(string url)
    {
        var i = url.LastIndexOf(',');
        if (i > 0 && int.TryParse(url[(i + 1)..], out _))
            return (url[..i], url[(i + 1)..]);
        return (url, null);
    }

    private string Cifrar(string valor) => PrefijoCifrado + _protector.Protect(valor);

    // Tolera valores sin prefijo (cargados a mano en la BD) para que los datos de prueba funcionen.
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
