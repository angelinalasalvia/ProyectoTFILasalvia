using BE;
using DAL;

namespace BLL;

public class BLLHistorialAcciones
{
    private readonly AccesoDatos _accesoDatos;

    // Umbral asumido para considerar que una campaña "alcanzó su KPI objetivo".
    // La tabla Campaña no tiene un campo de meta numérica explícito; ajustar si
    // definen un criterio distinto (por ejemplo, un campo MetaTasaExito).
    private const decimal UmbralKpi = 50m;

    public BLLHistorialAcciones(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public Task<MetricasCampana> ObtenerMetricasGlobales(CancellationToken ct = default)
        => ObtenerMetricasGlobalesXPeriodo(DateTime.Now.AddDays(-30), DateTime.Now, ct);

    public async Task<MetricasCampana> ObtenerMetricasGlobalesXPeriodo(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var campanasDelPeriodo = await _accesoDatos.Leer<CampanaConCanal>(
            @"SELECT DISTINCT camp.IdCampaña AS IdCampania, camp.TasaExito, ca.Nombre AS Canal
              FROM Campaña camp
              JOIN Canal ca ON ca.IdCanal = camp.IdCanal
              JOIN HistorialAcciones ha ON ha.IdCampaña = camp.IdCampaña
              WHERE ha.FechaEnvio BETWEEN @desde AND @hasta",
            new { desde, hasta }, ct: ct);

        var lista = campanasDelPeriodo.Where(c => c.TasaExito.HasValue).ToList();

        if (lista.Count == 0)
        {
            return new MetricasCampana
            {
                ExitoGlobal = 0,
                PorcentajeCampanasConKpiAlcanzado = 0,
                CanalMasEfectivo = "Sin datos",
                TasaCanalMasEfectivo = 0,
                ImpactoReduccionChurn = 0
            };
        }

        var exitoGlobal = Math.Round(lista.Average(c => c.TasaExito!.Value), 1);
        var porcentajeKpi = Math.Round((decimal)lista.Count(c => c.TasaExito!.Value >= UmbralKpi) / lista.Count * 100, 1);

        var mejorCanal = lista
            .GroupBy(c => c.Canal)
            .Select(g => new { Canal = g.Key, Tasa = g.Average(x => x.TasaExito!.Value) })
            .OrderByDescending(g => g.Tasa)
            .First();

        return new MetricasCampana
        {
            ExitoGlobal = exitoGlobal,
            PorcentajeCampanasConKpiAlcanzado = porcentajeKpi,
            CanalMasEfectivo = mejorCanal.Canal,
            TasaCanalMasEfectivo = Math.Round(mejorCanal.Tasa, 1),
            // Ver nota en BE.MetricasCampana: no hay historial de Prediccion para calcular esto todavía.
            ImpactoReduccionChurn = 0
        };
    }

    private class CampanaConCanal
    {
        public int IdCampania { get; set; }
        public decimal? TasaExito { get; set; }
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

    // Registra un envío (simulado: todavía no hay API de WhatsApp / Email). Queda "Entregado", en observación
    // (Estado = Activo, sin Resultado) y actualiza ClientesAlcanzados de la campaña, todo en una transacción.
    public Task RegistrarAccion(int idCliente, int idCampania, int? idRegla, int idUsuario, string tipoAccion, DateTime fechaEnvio,
                                CancellationToken ct = default)
        => _accesoDatos.Escribir(
            @"INSERT INTO HistorialAcciones (EstadoEnvio, FechaEnvio, TipoAccion, IdCampaña, IdCliente, IdUsuario, Estado, IdRegla)
              VALUES (@estadoEnvio, @fechaEnvio, @tipoAccion, @idCampania, @idCliente, @idUsuario, @estado, @idRegla);

              UPDATE Campaña SET ClientesAlcanzados =
                  (SELECT COUNT(DISTINCT h.IdCliente) FROM HistorialAcciones h WHERE h.IdCampaña = @idCampania)
              WHERE IdCampaña = @idCampania;",
            new
            {
                estadoEnvio = HistorialAccion.EnvioEntregado,
                fechaEnvio,
                tipoAccion,
                idCampania,
                idCliente,
                idUsuario,
                estado = HistorialAccion.EstadoActivo,
                idRegla
            }, ct: ct);

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

