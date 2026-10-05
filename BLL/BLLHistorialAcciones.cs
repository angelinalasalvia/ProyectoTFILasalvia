using BE;
using DAL;

namespace BLL;

public class BLLHistorialAcciones
{
    private readonly AccesoDatos _accesoDatos;

    //public BLLHistorialAcciones(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    private readonly ServicioEnvio _envio;

    public BLLHistorialAcciones(AccesoDatos accesoDatos, ServicioEnvio envio)
    {
        _accesoDatos = accesoDatos;
        _envio = envio;
    }

    // CU04 - Métricas globales de las campañas con acciones enviadas dentro del período [desde, hasta].
    // El rango es inclusivo por día: se consulta hasta el final del día "hasta" (si no, un "hasta" elegido
    // desde un selector de fecha, que llega a las 00:00, dejaría afuera todas las acciones de ese día).
    // - Éxito global y canal más efectivo se calculan sobre las campañas que ya tienen TasaExito.
    // - KPI objetivo: una campaña lo alcanza cuando ClientesAlcanzados >= Meta (solo cuenta las que tienen Meta).
    public async Task<MetricasCampana> ObtenerMetricasGlobalesXPeriodo(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var desdeInclusive = desde.Date;
        var hastaExclusive = hasta.Date.AddDays(1);

        var campanasDelPeriodo = (await _accesoDatos.Leer<CampanaConCanal>(
            @"SELECT DISTINCT camp.IdCampaña AS IdCampania, camp.TasaExito, camp.ClientesAlcanzados, camp.Meta, ca.Nombre AS Canal
              FROM Campaña camp
              JOIN Canal ca ON ca.IdCanal = camp.IdCanal
              JOIN HistorialAcciones ha ON ha.IdCampaña = camp.IdCampaña
              WHERE ha.FechaEnvio >= @desdeInclusive AND ha.FechaEnvio < @hastaExclusive",
            new { desdeInclusive, hastaExclusive }, ct: ct)).ToList();

        if (campanasDelPeriodo.Count == 0)
            return new MetricasCampana { HayCampanasEnPeriodo = false };

        var conMeta = campanasDelPeriodo.Where(c => c.Meta is > 0).ToList();
        var porcentajeKpi = conMeta.Count == 0
            ? 0m
            : Math.Round((decimal)conMeta.Count(c => (c.ClientesAlcanzados ?? 0) >= c.Meta!.Value) / conMeta.Count * 100, 1);

        var conTasa = campanasDelPeriodo.Where(c => c.TasaExito.HasValue).ToList();
        if (conTasa.Count == 0)
        {
            // Hubo campañas, pero todavía ninguna tiene tasa de éxito calculada.
            return new MetricasCampana
            {
                HayCampanasEnPeriodo = true,
                PorcentajeCampanasConKpiAlcanzado = porcentajeKpi
            };
        }

        var mejorCanal = conTasa
            .GroupBy(c => c.Canal)
            .Select(g => new { Canal = g.Key, Tasa = g.Average(x => x.TasaExito!.Value) })
            .OrderByDescending(g => g.Tasa)
            .First();

        return new MetricasCampana
        {
            HayCampanasEnPeriodo = true,
            ExitoGlobal = Math.Round(conTasa.Average(c => c.TasaExito!.Value), 1),
            PorcentajeCampanasConKpiAlcanzado = porcentajeKpi,
            CanalMasEfectivo = mejorCanal.Canal,
            TasaCanalMasEfectivo = Math.Round(mejorCanal.Tasa, 1)
        };
    }

    private class CampanaConCanal
    {
        public int IdCampania { get; set; }
        public decimal? TasaExito { get; set; }
        public int? ClientesAlcanzados { get; set; }
        public int? Meta { get; set; }
        public string Canal { get; set; } = string.Empty;
    }

    private const string SelectHistorialConCliente = @"
        SELECT ha.IdHistorial, ha.EstadoEnvio, ha.FechaEnvio, ha.FechaLectura, ha.FechaConversion,
               ha.TipoAccion, ha.IdCampaña AS IdCampania, ha.IdCliente, ha.IdUsuario, ha.Resultado, ha.Estado,
               COALESCE(ha.FechaConversion, ha.FechaLectura, ha.FechaEnvio) AS FechaAccion,
               c.Nombre AS NombreCliente, c.Apellido AS ApellidoCliente, c.PlanSocio AS PlanCliente
        FROM HistorialAcciones ha
        JOIN Cliente c ON c.IdCliente = ha.IdCliente";

    // Paso 2 y 6-7: historial completo de la campaña, ordenado por fecha de acción (más reciente primero).
    public async Task<List<HistorialAccion>> ObtenerHistorialCampania(int idCampania, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente} WHERE ha.IdCampaña = @idCampania ORDER BY FechaAccion DESC",
            new { idCampania }, ct: ct);
        return resultado.ToList();
    }

    // Whitelist de columnas ordenables - evita concatenar texto arbitrario del usuario
    // directo en el ORDER BY (eso sería una puerta abierta a SQL injection).
    private static readonly Dictionary<string, string> ColumnasOrdenables = new()
    {
        ["nombre"] = "c.Nombre",
        ["plan"] = "c.PlanSocio",
        ["resultado"] = "ha.Resultado",
        ["fecha"] = "FechaAccion",
    };

    // Pasos 8-9: reordenar según la columna elegida.
    public async Task<List<HistorialAccion>> OrdenarHistorialCampania(int idCampania, string columna, CancellationToken ct = default)
    {
        var columnaSql = ColumnasOrdenables.GetValueOrDefault(columna, "FechaAccion");
        var resultado = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente} WHERE ha.IdCampaña = @idCampania ORDER BY {columnaSql} DESC",
            new { idCampania }, ct: ct);
        return resultado.ToList();
    }

    // Pasos 10-11: filtro por resultado y/o plan (agregué idCampania, ver nota arriba).
    public async Task<List<HistorialAccion>> FiltrarPorClientes(int idCampania, string? resultado, string? plan, CancellationToken ct = default)
    {
        var condiciones = new List<string> { "ha.IdCampaña = @idCampania" };
        var parametros = new Dictionary<string, object?> { ["idCampania"] = idCampania };

        if (!string.IsNullOrWhiteSpace(resultado) && resultado != "Todos")
        {
            condiciones.Add("ha.Resultado = @resultado");
            parametros["resultado"] = resultado;
        }
        if (!string.IsNullOrWhiteSpace(plan) && plan != "Todos")
        {
            condiciones.Add("c.PlanSocio = @plan");
            parametros["plan"] = plan;
        }

        var where = " WHERE " + string.Join(" AND ", condiciones);
        var resultadoLista = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente}{where} ORDER BY FechaAccion DESC",
            parametros, ct: ct);
        return resultadoLista.ToList();
    }

    // ------------------------------------------------------------------------------------
    // Envíos automáticos (motor de reglas)
    // ------------------------------------------------------------------------------------

    private const string EmailUsuarioSistema = "sistema@mailtest.com";
    private const int DiasVentanaResultado = 7; // igual a la ventana que muestra CampanaDetalle

    // Los envíos automáticos se registran con el usuario "Sistema" (HistorialAcciones.IdUsuario es obligatorio).
    // Se lee sobre HistorialAccion porque solo interesa IdUsuario (todavía no hay clase Usuario en BE).
    public async Task<int> ObtenerIdUsuarioSistema(CancellationToken ct = default)
    {
        var usuario = (await _accesoDatos.Leer<HistorialAccion>(
            "SELECT IdUsuario FROM Usuario WHERE Email = @email",
            new { email = EmailUsuarioSistema }, ct: ct)).FirstOrDefault();

        return usuario?.IdUsuario
            ?? throw new InvalidOperationException($"Falta el usuario del sistema ({EmailUsuarioSistema}). Ejecutá el script de datos.");
    }

    // Envía el mensaje de la campaña por su canal y registra el resultado, todo en una transacción.
    //  - Email: envío real (ServicioEnvio).
    //  - WhatsApp: todavía simulado (queda "Entregado"); se reemplaza en el próximo bloque.
    // Si el envío falla queda como "Fallido" (Finalizada, resultado "Error de envío") y no suma a ClientesAlcanzados.
    public async Task RegistrarAccion(int idCliente, int idCampania, int? idRegla, int idUsuario, string tipoAccion, DateTime fechaEnvio,
                                      CancellationToken ct = default)
    {
        var envio = await EnviarMensajeCampania(idCliente, idCampania, ct);

        await _accesoDatos.Escribir(
            @"INSERT INTO HistorialAcciones (EstadoEnvio, FechaEnvio, TipoAccion, IdCampaña, IdCliente, IdUsuario, Estado, IdRegla, Resultado)
          VALUES (@estadoEnvio, @fechaEnvio, @tipoAccion, @idCampania, @idCliente, @idUsuario, @estado, @idRegla, @resultado);

          UPDATE Campaña SET ClientesAlcanzados =
              (SELECT COUNT(DISTINCT h.IdCliente) FROM HistorialAcciones h
               WHERE h.IdCampaña = @idCampania AND h.EstadoEnvio <> @fallido)
          WHERE IdCampaña = @idCampania;",
            new
            {
                estadoEnvio = envio.Exito ? HistorialAccion.EnvioEntregado : HistorialAccion.EnvioFallido,
                fechaEnvio,
                tipoAccion,
                idCampania,
                idCliente,
                idUsuario,
                estado = envio.Exito ? HistorialAccion.EstadoActivo : HistorialAccion.EstadoFinalizada,
                idRegla,
                resultado = envio.Exito ? null : HistorialAccion.ResultadoErrorEnvio,
                fallido = HistorialAccion.EnvioFallido
            }, ct: ct);
    }

    private async Task<ResultadoEnvio> EnviarMensajeCampania(int idCliente, int idCampania, CancellationToken ct)
    {
        var cliente = (await _accesoDatos.Leer<Cliente>(
            "SELECT IdCliente, Nombre, Apellido, Email, Telefono FROM Cliente WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct)).FirstOrDefault();

        var campania = (await _accesoDatos.Leer<Campana>(
            @"SELECT camp.IdCampaña AS IdCampania, camp.Asunto AS Nombre, ca.Nombre AS Canal, ca.IdCanal,
                 camp.AsuntoEmail, camp.Mensaje
          FROM Campaña camp JOIN Canal ca ON ca.IdCanal = camp.IdCanal
          WHERE camp.IdCampaña = @idCampania",
            new { idCampania }, ct: ct)).FirstOrDefault();

        if (cliente is null || campania is null)
            return new ResultadoEnvio(false, "No se encontró el cliente o la campaña.");

        var texto = campania.Mensaje.Replace("{nombre_cliente}", cliente.Nombre);

        if (campania.Canal == "Email")
            return await _envio.EnviarEmail(cliente.Email, campania.AsuntoEmail ?? campania.Nombre, texto, ct);

        if (campania.Canal == "WhatsApp")
            return await _envio.EnviarWhatsApp(cliente.Telefono ?? string.Empty, texto, ct);

        return new ResultadoEnvio(false, $"Canal no soportado: {campania.Canal}");
    }

    // Cierra los envíos automáticos cuya ventana de observación (7 días) ya terminó y devuelve cuántos cerró.
    //  - Rescatado: el cliente tuvo una visita o un pago registrado después del envío, dentro de la ventana
    //    (sale de EventosCliente, no de una API).
    //  - Si no volvió, la lectura y el clic se simulan (mientras no haya API real). La semilla es el IdHistorial,
    //    así que el mismo envío siempre da el mismo resultado y las pruebas son repetibles.
    public async Task<int> CerrarAccionesPendientes(DateTime ahora, CancellationToken ct = default)
    {
        var pendientes = (await _accesoDatos.Leer<HistorialAccion>(
            @"SELECT IdHistorial, IdCliente, FechaEnvio
              FROM HistorialAcciones
              WHERE Estado = @activo AND TipoAccion = @tipo AND Resultado IS NULL AND FechaEnvio <= @limite",
            new
            {
                activo = HistorialAccion.EstadoActivo,
                tipo = HistorialAccion.TipoEnvioAutomatico,
                limite = ahora.AddDays(-DiasVentanaResultado)
            }, ct: ct)).ToList();

        foreach (var accion in pendientes)
        {
            // MIN() devuelve una fila con NULL si no hubo eventos: Fecha queda en su valor por defecto.
            var vuelta = (await _accesoDatos.Leer<EventoCliente>(
                @"SELECT MIN(Fecha) AS Fecha
                  FROM EventosCliente
                  WHERE IdCliente = @idCliente AND Evento IN (@visita, @pago)
                    AND Fecha > @envio AND Fecha <= @fin",
                new
                {
                    idCliente = accion.IdCliente,
                    visita = TipoEvento.VisitaGimnasio,
                    pago = TipoEvento.PagoRegistrado,
                    envio = accion.FechaEnvio,
                    fin = accion.FechaEnvio.AddDays(DiasVentanaResultado)
                }, ct: ct)).FirstOrDefault();

            string resultado, estadoEnvio;
            DateTime? fechaLectura = null, fechaConversion = null;

            if (vuelta is not null && vuelta.Fecha != default)
            {
                resultado = HistorialAccion.ResultadoRescatado;
                estadoEnvio = HistorialAccion.EnvioLeido;
                fechaLectura = accion.FechaEnvio.AddHours(1);
                fechaConversion = vuelta.Fecha;
            }
            else
            {
                var azar = new Random(accion.IdHistorial).NextDouble();
                if (azar < 0.15)      // 15 %: hizo clic
                {
                    resultado = HistorialAccion.ResultadoClic;
                    estadoEnvio = HistorialAccion.EnvioLeido;
                    fechaLectura = accion.FechaEnvio.AddHours(2);
                }
                else if (azar < 0.60) // 45 %: lo leyó pero no hizo nada
                {
                    resultado = HistorialAccion.ResultadoSinAccion;
                    estadoEnvio = HistorialAccion.EnvioLeido;
                    fechaLectura = accion.FechaEnvio.AddHours(3);
                }
                else                  // 40 %: solo llegó a entregarse
                {
                    resultado = HistorialAccion.ResultadoSinAccion;
                    estadoEnvio = HistorialAccion.EnvioEntregado;
                }
            }

            await _accesoDatos.Modificar(
                @"UPDATE HistorialAcciones
                  SET Resultado = @resultado, EstadoEnvio = @estadoEnvio, FechaLectura = @fechaLectura,
                      FechaConversion = @fechaConversion, Estado = @estado
                  WHERE IdHistorial = @idHistorial",
                new
                {
                    resultado,
                    estadoEnvio,
                    fechaLectura,
                    fechaConversion,
                    estado = HistorialAccion.EstadoFinalizada,
                    idHistorial = accion.IdHistorial
                }, ct: ct);
        }

        return pendientes.Count;
    }
}

