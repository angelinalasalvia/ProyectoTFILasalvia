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

    // CU13 - ObtenerMetricasEfectividad. Trae el período pedido y el anterior (misma duración) en una sola consulta.
    // Las acciones "Pendiente" (programadas que todavía no salieron) no cuentan.
    public async Task<MetricasEfectividad> ObtenerMetricasEfectividad(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var desdeInclusive = desde.Date;
        var hastaExclusive = hasta.Date.AddDays(1);
        var desdeAnterior = desdeInclusive - (hastaExclusive - desdeInclusive);

        var filas = (await _accesoDatos.Leer<HistorialAccion>(
            @"SELECT ha.IdCliente, ha.EstadoEnvio, ha.Resultado, ha.FechaEnvio, ca.Nombre AS Canal
          FROM HistorialAcciones ha
          JOIN Campaña camp ON camp.IdCampaña = ha.IdCampaña
          JOIN Canal ca ON ca.IdCanal = camp.IdCanal
          WHERE ha.FechaEnvio >= @desdeAnterior AND ha.FechaEnvio < @hastaExclusive
            AND ha.EstadoEnvio <> @pendiente",
            new { desdeAnterior, hastaExclusive, pendiente = HistorialAccion.EnvioPendiente }, ct: ct)).ToList();

        var actuales = filas.Where(f => f.FechaEnvio >= desdeInclusive).ToList();
        var anteriores = filas.Where(f => f.FechaEnvio < desdeInclusive).ToList();

        static bool Entregada(HistorialAccion h) =>
            h.EstadoEnvio == HistorialAccion.EnvioEntregado || h.EstadoEnvio == HistorialAccion.EnvioLeido;
        static bool Rescatada(HistorialAccion h) => h.Resultado == HistorialAccion.ResultadoRescatado;

        return new MetricasEfectividad
        {
            TotalAcciones = actuales.Count,
            TotalAccionesAnterior = anteriores.Count,
            Entregadas = actuales.Count(Entregada),
            EntregadasAnterior = anteriores.Count(Entregada),
            Rescatados = actuales.Count(Rescatada),
            RescatadosAnterior = anteriores.Count(Rescatada),
            Canales = actuales.GroupBy(h => h.Canal).OrderBy(g => g.Key)
                .Select(g => new MetricasCanal
                {
                    Canal = g.Key,
                    Entregadas = g.Count(Entregada),
                    Leidas = g.Count(h => h.EstadoEnvio == HistorialAccion.EnvioLeido),
                    Clics = g.Count(h => h.Resultado == HistorialAccion.ResultadoClic),
                    Rescatados = g.Count(Rescatada)
                }).ToList()
        };
    }

    // CU14 - ObtenerMetricasRetencion(periodo, segmento). Período = últimos "dias" días (incluye hoy) contra los "dias" anteriores.
    // Clientes recuperados = clientes distintos con Resultado = Rescatado. MRR salvado = precio del plan de esos clientes.
    public async Task<MetricasRetencion> ObtenerMetricasRetencion(int dias, string? segmento = null, CancellationToken ct = default)
    {
        var hastaExclusivo = DateTime.Today.AddDays(1);
        var desde = hastaExclusivo.AddDays(-dias);
        var desdeAnterior = desde.AddDays(-dias);
        var (condicion, valor) = SegmentoRetencion.Resolver(segmento);

        var rescates = (await _accesoDatos.Leer<RescateRetencion>(
            $@"SELECT ha.IdCliente, ha.FechaEnvio, ha.FechaConversion, p.Precio
           FROM HistorialAcciones ha
           JOIN Cliente c ON c.IdCliente = ha.IdCliente
           JOIN [Plan] p ON p.Nombre = c.PlanSocio
           WHERE ha.Resultado = @rescatado
             AND ha.FechaEnvio >= @desdeAnterior AND ha.FechaEnvio < @hastaExclusivo
             AND {condicion}",
            new { rescatado = HistorialAccion.ResultadoRescatado, desdeAnterior, hastaExclusivo, segmentoValor = valor },
            ct: ct)).ToList();

        var actuales = rescates.Where(r => r.FechaEnvio >= desde).ToList();
        var anteriores = rescates.Where(r => r.FechaEnvio < desde).ToList();

        static int Clientes(List<RescateRetencion> l) => l.Select(r => r.IdCliente).Distinct().Count();
        static decimal Mrr(List<RescateRetencion> l) => l.GroupBy(r => r.IdCliente).Sum(g => g.First().Precio);

        return new MetricasRetencion
        {
            ClientesRecuperados = Clientes(actuales),
            ClientesRecuperadosAnterior = Clientes(anteriores),
            RecuperadosHoy = actuales.Where(r => r.FechaConversion?.Date == DateTime.Today).Select(r => r.IdCliente).Distinct().Count(),
            MrrSalvado = Mrr(actuales),
            MrrSalvadoAnterior = Mrr(anteriores)
        };
    }

    private class RescateRetencion
    {
        public int IdCliente { get; set; }
        public DateTime FechaEnvio { get; set; }
        public DateTime? FechaConversion { get; set; }
        public decimal Precio { get; set; }
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

    // Envía el mensaje y registra la acción. Devuelve el resultado del envío (los envíos fallidos
    // también quedan registrados, con EstadoEnvio = Fallido).
    public async Task<ResultadoEnvio> RegistrarAccion(int idCliente, int idCampania, int? idRegla, int idUsuario, string tipoAccion, DateTime fechaEnvio,
                                      CancellationToken ct = default)
    {
        var envio = await EnviarMensajeCampania(idCliente, idCampania, ct);

        await _accesoDatos.Escribir(
            @"INSERT INTO HistorialAcciones (EstadoEnvio, FechaEnvio, TipoAccion, IdCampaña, IdCliente, IdUsuario, Estado, IdRegla, Resultado)
          VALUES (@estadoEnvio, @fechaEnvio, @tipoAccion, @idCampania, @idCliente, @idUsuario, @estado, @idRegla, @resultado);

          UPDATE Campaña SET ClientesAlcanzados =
              (SELECT COUNT(DISTINCT h.IdCliente) FROM HistorialAcciones h
               WHERE h.IdCampaña = @idCampania AND h.EstadoEnvio NOT IN (@fallido, @pendiente))
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
                fallido = HistorialAccion.EnvioFallido,
                pendiente = HistorialAccion.EnvioPendiente
            }, ct: ct);

        return envio;
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

        if (campania.Canal == "Telegram")
            return await _envio.EnviarTelegram(null, texto, ct);

        return new ResultadoEnvio(false, $"Canal no soportado: {campania.Canal}");
    }

    // ------------------------------------------------------------------------------------
    // Acciones de retención manuales (CU03)
    // ------------------------------------------------------------------------------------

    // Paso 2: historial de acciones de retención del cliente, la más reciente primero.
    public async Task<List<HistorialAccion>> ObtenerHistorialCliente(int idCliente, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<HistorialAccion>(
            @"SELECT ha.IdHistorial, ha.EstadoEnvio, ha.FechaEnvio, ha.TipoAccion, ha.IdCampaña AS IdCampania,
                     ha.IdCliente, ha.IdUsuario, ha.Resultado, ha.Estado,
                     camp.Asunto AS NombreCampania, ca.Nombre AS Canal
              FROM HistorialAcciones ha
              JOIN Campaña camp ON camp.IdCampaña = ha.IdCampaña
              JOIN Canal ca ON ca.IdCanal = camp.IdCanal
              WHERE ha.IdCliente = @idCliente
              ORDER BY ha.FechaEnvio DESC",
            new { idCliente }, ct: ct);
        return resultado.ToList();
    }

    // Pasos 6 y 12: fecha del último envío efectivo de la campaña a ese cliente (null si nunca se envió).
    // No cuentan los envíos fallidos ni los programados que todavía no salieron.
    public DateTime? ObtenerFechaEnvioPrevio(List<HistorialAccion> historial, int idCampania)
        => historial
            .Where(h => h.IdCampania == idCampania
                     && h.Estado != HistorialAccion.EstadoProgramada
                     && h.EstadoEnvio != HistorialAccion.EnvioFallido)
            .Select(h => (DateTime?)h.FechaEnvio)
            .Max();

    // Pasos 13 a 15. Sin fecha: envío inmediato (se envía y se registra). Con fecha: se registra como
    // "Programada" y la envía EnvioProgramadoBackgroundService cuando llegue la hora.
    public async Task<ResultadoAccionManual> RegistrarAccionManual(
        int idCliente, int idCampania, int idUsuario, DateTime? fechaProgramada = null, CancellationToken ct = default)
    {
        if (fechaProgramada is null)
        {
            var envio = await RegistrarAccion(idCliente, idCampania, null, idUsuario,
                HistorialAccion.TipoAccionManual, DateTime.Now, ct);

            return envio.Exito
                ? new ResultadoAccionManual(true, "Enviada", null)
                : new ResultadoAccionManual(false, "Fallida", envio.Mensaje);
        }

        if (fechaProgramada <= DateTime.Now)
            throw new ArgumentException("La fecha y hora programadas deben ser posteriores al momento actual.");

        await _accesoDatos.Escribir(
            @"INSERT INTO HistorialAcciones (EstadoEnvio, FechaEnvio, TipoAccion, IdCampaña, IdCliente, IdUsuario, Estado, IdRegla, Resultado)
              VALUES (@estadoEnvio, @fechaEnvio, @tipoAccion, @idCampania, @idCliente, @idUsuario, @estado, NULL, NULL)",
            new
            {
                estadoEnvio = HistorialAccion.EnvioPendiente,
                fechaEnvio = fechaProgramada,
                tipoAccion = HistorialAccion.TipoAccionManual,
                idCampania,
                idCliente,
                idUsuario,
                estado = HistorialAccion.EstadoProgramada
            }, ct: ct);

        return new ResultadoAccionManual(true, HistorialAccion.EstadoProgramada, null);
    }

    // Envía las acciones programadas cuya fecha y hora ya llegó (lo ejecuta EnvioProgramadoBackgroundService).
    // Devuelve cuántas procesó. El "AND Estado = @programada" del UPDATE evita procesar dos veces la misma acción.
    public async Task<int> EnviarAccionesProgramadas(DateTime ahora, CancellationToken ct = default)
    {
        var pendientes = (await _accesoDatos.Leer<HistorialAccion>(
            @"SELECT IdHistorial, IdCliente, IdCampaña AS IdCampania
              FROM HistorialAcciones
              WHERE Estado = @programada AND FechaEnvio <= @ahora
              ORDER BY FechaEnvio",
            new { programada = HistorialAccion.EstadoProgramada, ahora }, ct: ct)).ToList();

        foreach (var accion in pendientes)
        {
            var envio = await EnviarMensajeCampania(accion.IdCliente, accion.IdCampania, ct);

            await _accesoDatos.Modificar(
                @"UPDATE HistorialAcciones
                  SET EstadoEnvio = @estadoEnvio, Estado = @estado, Resultado = @resultado
                  WHERE IdHistorial = @idHistorial AND Estado = @programada;

                  UPDATE Campaña SET ClientesAlcanzados =
                      (SELECT COUNT(DISTINCT h.IdCliente) FROM HistorialAcciones h
                       WHERE h.IdCampaña = @idCampania AND h.EstadoEnvio NOT IN (@fallido, @pendiente))
                  WHERE IdCampaña = @idCampania;",
                new
                {
                    estadoEnvio = envio.Exito ? HistorialAccion.EnvioEntregado : HistorialAccion.EnvioFallido,
                    estado = envio.Exito ? HistorialAccion.EstadoActivo : HistorialAccion.EstadoFinalizada,
                    resultado = envio.Exito ? null : HistorialAccion.ResultadoErrorEnvio,
                    idHistorial = accion.IdHistorial,
                    idCampania = accion.IdCampania,
                    programada = HistorialAccion.EstadoProgramada,
                    fallido = HistorialAccion.EnvioFallido,
                    pendiente = HistorialAccion.EnvioPendiente
                }, ct: ct);
        }

        return pendientes.Count;
    }

    // Cierra los envíos (automáticos y manuales) cuya ventana de observación (7 días) ya terminó y devuelve cuántos cerró.
    //  - Rescatado: el cliente tuvo una visita o un pago registrado después del envío, dentro de la ventana
    //    (sale de EventosCliente, no de una API).
    //  - Si no volvió, la lectura y el clic se simulan (mientras no haya API real). La semilla es el IdHistorial,
    //    así que el mismo envío siempre da el mismo resultado y las pruebas son repetibles.
    public async Task<int> CerrarAccionesPendientes(DateTime ahora, CancellationToken ct = default)
    {
        var pendientes = (await _accesoDatos.Leer<HistorialAccion>(
            @"SELECT IdHistorial, IdCliente, FechaEnvio
              FROM HistorialAcciones
              WHERE Estado = @activo AND Resultado IS NULL AND FechaEnvio <= @limite",
            new
            {
                activo = HistorialAccion.EstadoActivo,
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

// Resultado de ejecutar una acción de retención manual (CU03).
// Estado: "Enviada", "Programada" o "Fallida" (en ese caso, Error trae el motivo).
public record ResultadoAccionManual(bool Exito, string Estado, string? Error);
