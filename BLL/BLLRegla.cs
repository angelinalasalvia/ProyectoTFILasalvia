using System.Text;
using BE;
using DAL;

namespace BLL;

public class BLLRegla
{
    private readonly AccesoDatos _accesoDatos;
    private readonly BLLHistorialAcciones _historial;

    public BLLRegla(AccesoDatos accesoDatos, BLLHistorialAcciones historial)
    {
        _accesoDatos = accesoDatos;
        _historial = historial;
    }

    // VecesEjecutada: acciones de HistorialAcciones generadas por esta regla (IdRegla).
    // Las acciones anteriores al motor no tienen IdRegla, por eso no se cuentan.
    private const string SelectBase = @"
        SELECT r.IdRegla, r.Estado, r.FechaCreacion, r.IdCampaña AS IdCampania, r.Nombre, r.Prioridad,
               r.IdSede, r.IdPlan,
               camp.Asunto AS NombreCampania, ca.Nombre AS Canal,
               sede.Nombre AS NombreSede, pl.Nombre AS NombrePlan,
               (SELECT COUNT(*) FROM HistorialAcciones h WHERE h.IdRegla = r.IdRegla) AS VecesEjecutada
        FROM Regla r
        JOIN Campaña camp ON camp.IdCampaña = r.IdCampaña
        JOIN Canal ca ON ca.IdCanal = camp.IdCanal
        LEFT JOIN Sede sede ON sede.IdSede = r.IdSede
        LEFT JOIN [Plan] pl ON pl.IdPlan = r.IdPlan";

    // CU10, pasos 2-3: listado completo de reglas, más recientes primero.
    public async Task<List<Regla>> ObtenerReglas(CancellationToken ct = default)
    {
        var reglas = (await _accesoDatos.Leer<Regla>($"{SelectBase} ORDER BY r.FechaCreacion DESC", ct: ct)).ToList();
        return await CompletarCondiciones(reglas, ct);
    }

    // CU10, pasos 5-6: búsqueda por nombre. Respeta el criterio de orden elegido en "Filtrar por".
    public async Task<List<Regla>> BuscarReglaPorNombre(string nombre, string criterio = "Más recientes", CancellationToken ct = default)
    {
        var reglas = (await _accesoDatos.Leer<Regla>(
            $"{SelectBase} WHERE r.Nombre LIKE @nombre ORDER BY {OrdenSql(criterio)}",
            new { nombre = $"%{nombre}%" }, ct: ct)).ToList();
        return await CompletarCondiciones(reglas, ct);
    }

    // CU10, pasos 7-8: "Filtrar por" (Más recientes / Más ejecutadas / Alfabético).
    public async Task<List<Regla>> ObtenerReglasOrdenadas(string criterio, CancellationToken ct = default)
    {
        var reglas = (await _accesoDatos.Leer<Regla>($"{SelectBase} ORDER BY {OrdenSql(criterio)}", ct: ct)).ToList();
        return await CompletarCondiciones(reglas, ct);
    }

    // Whitelist: nunca se concatena texto de la UI en el ORDER BY.
    private static string OrdenSql(string criterio) => criterio switch
    {
        "Alfabético" => "r.Nombre ASC",
        "Más ejecutadas" => "VecesEjecutada DESC, r.FechaCreacion DESC",
        _ => "r.FechaCreacion DESC" // "Más recientes"
    };

    // CU10, paso 4: la UI puede marcar una regla como "requiere acción
    // adicional" (ej: falta configurar la integración del canal). Por ahora
    // no tenemos ese backend (Integraciones sigue mockeado), así que queda
    // en false. Cuando se conecte Integraciones, acá se valida el canal
    // real de la campaña asociada.
    public bool RequiereAccionAdicional(Regla regla) => false;

    // CU10, alternos 11.1 / 11.2: pausar / activar.
    public Task CambiarEstado(int idRegla, string nuevoEstado, CancellationToken ct = default)
        => _accesoDatos.Modificar(
            "UPDATE Regla SET Estado = @nuevoEstado WHERE IdRegla = @idRegla",
            new { nuevoEstado, idRegla }, ct: ct);

    // CU10, alterno 11.3: eliminar. Borra primero el vínculo Regla_Condicion y después la Regla y
    // sus Condicion propias, porque la base no tiene ON DELETE CASCADE. Las acciones ya registradas
    // en HistorialAcciones se conservan (FK con ON DELETE SET NULL). Todo en un solo Eliminar()
    // para que quede dentro de la misma transacción.
    public Task EliminarRegla(int idRegla, CancellationToken ct = default)
        => _accesoDatos.Eliminar(
            @"DECLARE @condiciones TABLE (IdCondicion INT);
              INSERT INTO @condiciones SELECT CondicionID FROM Regla_Condicion WHERE ReglaID = @idRegla;
              DELETE FROM Regla_Condicion WHERE ReglaID = @idRegla;
              DELETE FROM Regla WHERE IdRegla = @idRegla;
              DELETE FROM Condicion WHERE IdCondicion IN (SELECT IdCondicion FROM @condiciones);",
            new { idRegla }, ct: ct);

    // Convención de Condicion.Operador: conector (Y/O) de esa condición con la SIGUIENTE.
    // La última siempre lleva "Y". Los valores múltiples de una condición van separados por '|'.
    public Task CrearRegla(string nombre, int idCampania, List<Condicion> condiciones, int? idSede = null, int? idPlan = null,
                           int prioridad = Regla.PrioridadPorDefecto, CancellationToken ct = default)
    {
        ValidarNombreYPrioridad(nombre, prioridad);
        var filas = ValidarYNormalizarCondiciones(condiciones);

        var sql = new StringBuilder();
        var parametros = new Dictionary<string, object?>
        {
            ["nombre"] = nombre.Trim(),
            ["idCampania"] = idCampania,
            ["fechaCreacion"] = DateTime.Now,
            ["estado"] = EstadosRegla.Activa,
            ["idSede"] = idSede,
            ["idPlan"] = idPlan,
            ["prioridad"] = prioridad,
        };

        sql.AppendLine(@"
            DECLARE @NuevoIdRegla INT;
            INSERT INTO Regla (Estado, FechaCreacion, IdCampaña, Nombre, IdSede, IdPlan, Prioridad)
            VALUES (@estado, @fechaCreacion, @idCampania, @nombre, @idSede, @idPlan, @prioridad);
            SET @NuevoIdRegla = SCOPE_IDENTITY();");

        AgregarInsertsCondicion(sql, parametros, filas, "@NuevoIdRegla");
        return _accesoDatos.Escribir(sql.ToString(), parametros, ct: ct);
    }

    // Para CrearRegla.razor en modo edición: actualiza nombre, campaña, sede/plan y prioridad, y reemplaza
    // todas las condiciones (se borran las viejas y se insertan las nuevas). Todo en un solo Escribir()
    // para que sea una sola transacción.
    public Task ModificarRegla(int idRegla, string nombre, int idCampania, List<Condicion> condiciones, int? idSede = null, int? idPlan = null,
                               int prioridad = Regla.PrioridadPorDefecto, CancellationToken ct = default)
    {
        ValidarNombreYPrioridad(nombre, prioridad);
        var filas = ValidarYNormalizarCondiciones(condiciones);

        var sql = new StringBuilder();
        var parametros = new Dictionary<string, object?>
        {
            ["idRegla"] = idRegla,
            ["nombre"] = nombre.Trim(),
            ["idCampania"] = idCampania,
            ["idSede"] = idSede,
            ["idPlan"] = idPlan,
            ["prioridad"] = prioridad,
        };

        sql.AppendLine(@"
            UPDATE Regla SET Nombre = @nombre, IdCampaña = @idCampania, IdSede = @idSede, IdPlan = @idPlan, Prioridad = @prioridad
            WHERE IdRegla = @idRegla;

            DECLARE @condiciones TABLE (IdCondicion INT);
            INSERT INTO @condiciones SELECT CondicionID FROM Regla_Condicion WHERE ReglaID = @idRegla;
            DELETE FROM Regla_Condicion WHERE ReglaID = @idRegla;
            DELETE FROM Condicion WHERE IdCondicion IN (SELECT IdCondicion FROM @condiciones);");

        AgregarInsertsCondicion(sql, parametros, filas, "@idRegla");
        return _accesoDatos.Escribir(sql.ToString(), parametros, ct: ct);
    }

    // Para CrearRegla.razor en modo edición (/automatizacion/editar-regla/{id}):
    // trae nombre, campaña, prioridad y condiciones actuales para precargar el formulario.
    public async Task<Regla?> ObtenerReglaPorId(int idRegla, CancellationToken ct = default)
    {
        var reglas = await _accesoDatos.Leer<Regla>($"{SelectBase} WHERE r.IdRegla = @idRegla", new { idRegla }, ct: ct);
        var regla = reglas.FirstOrDefault();
        if (regla is null) return null;

        regla.Condiciones = await ObtenerCondicionesDeRegla(idRegla, ct);
        regla.CondicionResumen = ArmarResumen(regla.Condiciones);
        return regla;
    }

    // Texto legible de las condiciones. El Y se evalúa antes que el O, y los grupos unidos por O
    // que tienen más de una condición se muestran entre paréntesis:
    //   Inactividad > 14 días Y Nivel de Riesgo es Medio O Historial de Pagos es Vencido
    //   => (Inactividad > 14 días Y Nivel de Riesgo es Medio) O Historial de Pagos es Vencido
    public static string ArmarResumen(List<Condicion> condiciones)
    {
        if (condiciones.Count == 0) return string.Empty;

        var grupos = new List<List<string>> { new() };
        for (int i = 0; i < condiciones.Count; i++)
        {
            grupos[^1].Add(DescribirCondicion(condiciones[i]));
            if (i < condiciones.Count - 1 && condiciones[i].Operador == Condicion.O)
                grupos.Add(new());
        }

        if (grupos.Count == 1) return string.Join(" Y ", grupos[0]);

        return string.Join(" O ", grupos.Select(g => g.Count > 1 ? $"({string.Join(" Y ", g)})" : g[0]));
    }

    // ------------------------------------------------------------------------------------
    // Motor de reglas
    // ------------------------------------------------------------------------------------
    private const int DiasEntreEnvios = 30;         // máximo 1 envío por cliente cada 30 días (de cualquier regla)
    private const int DiasBloqueoPostRescate = 60;  // el modelo tarda ~60 días en reflejar una recuperación
    private const int MaxIntentosPorEpisodio = 3;   // episodio = desde el último "Rescatado"; al llegar a 3 pasa a gestión manual
    private const int DiasPagoVencidoVigente = 60;

    // Evalúa las reglas activas sobre las predicciones vigentes y registra un envío simulado por cada cliente
    // que corresponda. Devuelve los envíos realizados (cliente, regla, campaña).
    //
    // Por cada cliente activo con predicción:
    //   1) Si recibió una campaña hace menos de 30 días, se lo saltea.
    //   2) Si fue rescatado hace menos de 60 días, se lo saltea.
    //   3) Si ya tuvo 3 intentos en el episodio actual, se lo saltea (gestión manual).
    //   4) Gana la primera regla que cumple, de menor a mayor Prioridad (a igual prioridad, la más antigua).
    // 'fechaReferencia' permite simular otra fecha (pruebas); por defecto es ahora.
    public async Task<(List<(int IdCliente, int IdRegla, int IdCampania)> Enviados, int Cerrados)> EjecutarReglas(
    DateTime? fechaReferencia = null, CancellationToken ct = default)
    {
        var ahora = fechaReferencia ?? DateTime.Now;
        var enviados = new List<(int IdCliente, int IdRegla, int IdCampania)>();

        // Primero se cierran los envíos cuya ventana de 7 días terminó (define Rescatado / Sin acción),
        // porque de eso depende el bloqueo y el conteo de intentos.
        var cerrados = await _historial.CerrarAccionesPendientes(ahora, ct);

        var reglas = (await ObtenerReglas(ct))
            .Where(r => r.Estado == EstadosRegla.Activa)
            .OrderBy(r => r.Prioridad).ThenBy(r => r.IdRegla)
            .ToList();
        if (reglas.Count == 0) return (enviados, cerrados);

        var idUsuarioSistema = await _historial.ObtenerIdUsuarioSistema(ct);

        // --- Datos (una consulta por tema; se cruzan en memoria) ---
        var predicciones = (await _accesoDatos.Leer<Prediccion>(
            @"SELECT p.IdCliente, p.NivelRiesgo
              FROM Prediccion p
              JOIN Cliente c ON c.IdCliente = p.IdCliente
              WHERE c.EstadoRegistro = N'Socio Activo'", ct: ct)).ToList();

        var clientes = (await _accesoDatos.Leer<Cliente>(
            "SELECT IdCliente, PlanSocio, Sede FROM Cliente WHERE EstadoRegistro = N'Socio Activo'", ct: ct))
            .ToDictionary(c => c.IdCliente);

        var ultimaVisita = await UltimaFechaPorCliente(TipoEvento.VisitaGimnasio, null, ct);
        var ultimoPagoVencido = await UltimaFechaPorCliente(TipoEvento.PagoVencido, ahora.AddDays(-(DiasPagoVencidoVigente + 2)), ct);
        var ultimoPago = await UltimaFechaPorCliente(TipoEvento.PagoRegistrado, null, ct);

        // Solo si alguna regla usa "Actividad" (caída de visitas: últimos 30 días vs 30 anteriores).
        var usaActividad = reglas.Any(r => r.Condiciones.Any(c => c.Atributo == Condicion.Actividad));
        var visitas60 = usaActividad
            ? (await _accesoDatos.Leer<EventoCliente>(
                "SELECT IdCliente, Fecha FROM EventosCliente WHERE Evento = @evento AND Fecha >= @desde",
                new { evento = TipoEvento.VisitaGimnasio, desde = ahora.AddDays(-60) }, ct: ct))
                .ToLookup(v => v.IdCliente, v => v.Fecha)
            : null;

        var acciones = (await _accesoDatos.Leer<HistorialAccion>(
            "SELECT IdCliente, IdCampaña AS IdCampania, FechaEnvio, Resultado, FechaConversion FROM HistorialAcciones", ct: ct))
            .ToLookup(a => a.IdCliente);

        foreach (var prediccion in predicciones.OrderBy(p => p.IdCliente))
        {
            if (!clientes.TryGetValue(prediccion.IdCliente, out var cliente)) continue;

            var historial = acciones[prediccion.IdCliente].OrderBy(a => a.FechaEnvio).ToList();

            // 1) Tope de frecuencia
            if (historial.Count > 0 && (ahora - historial[^1].FechaEnvio).Days < DiasEntreEnvios) continue;

            // 2) Bloqueo posterior a un rescate
            var ultimoRescate = historial.LastOrDefault(a => a.Resultado == HistorialAccion.ResultadoRescatado);
            if (ultimoRescate is not null &&
                (ahora - (ultimoRescate.FechaConversion ?? ultimoRescate.FechaEnvio)).Days < DiasBloqueoPostRescate) continue;

            // 3) Intentos del episodio actual (campañas enviadas después del último rescate)
            var intentos = historial.Count(a => ultimoRescate is null || a.FechaEnvio > ultimoRescate.FechaEnvio);
            if (intentos >= MaxIntentosPorEpisodio) continue;

            // 4) Atributos del cliente y evaluación de reglas
            var inactividad = ultimaVisita.TryGetValue(prediccion.IdCliente, out var fechaVisita) ? (ahora - fechaVisita).Days : 9999;

            var vencido = ultimoPagoVencido.TryGetValue(prediccion.IdCliente, out var fechaVencido)
                          && (ahora - fechaVencido).Days <= DiasPagoVencidoVigente
                          && !(ultimoPago.TryGetValue(prediccion.IdCliente, out var fechaPago) && fechaPago > fechaVencido);

            var caidaActividad = 0;
            if (visitas60 is not null)
            {
                var fechas = visitas60[prediccion.IdCliente].ToList();
                var recientes = fechas.Count(f => f >= ahora.AddDays(-30));
                var anteriores = fechas.Count - recientes;
                caidaActividad = anteriores > 0 ? (int)Math.Round(100.0 * (anteriores - recientes) / anteriores) : 0;
            }

            var textoIntentos = intentos switch { 0 => "Ninguno", 1 => "Uno", _ => "Dos" };

            var regla = reglas.FirstOrDefault(r =>
                CumpleRegla(r, cliente, prediccion.NivelRiesgo, inactividad, vencido, caidaActividad, textoIntentos));
            if (regla is null) continue;

            // 5) Envío simulado
            await _historial.RegistrarAccion(prediccion.IdCliente, regla.IdCampania, regla.IdRegla, idUsuarioSistema,
                HistorialAccion.TipoEnvioAutomatico, ahora, ct);
            enviados.Add((prediccion.IdCliente, regla.IdRegla, regla.IdCampania));
        }

        return (enviados, cerrados);
    }

    // Fecha del último evento de un tipo por cliente (opcionalmente desde una fecha).
    private async Task<Dictionary<int, DateTime>> UltimaFechaPorCliente(string evento, DateTime? desde, CancellationToken ct)
    {
        var filtroDesde = desde is null ? string.Empty : " AND Fecha >= @desde";
        var filas = await _accesoDatos.Leer<EventoCliente>(
            $"SELECT IdCliente, MAX(Fecha) AS Fecha FROM EventosCliente WHERE Evento = @evento{filtroDesde} GROUP BY IdCliente",
            new { evento, desde }, ct: ct);
        return filas.ToDictionary(f => f.IdCliente, f => f.Fecha);
    }

    // Una regla se cumple si el cliente está en el plan / sede de la segmentación (si tiene) Y se cumplen sus condiciones.
    private static bool CumpleRegla(Regla regla, Cliente cliente, string nivelRiesgo, int inactividad, bool vencido, int caidaActividad, string intentos)
    {
        if (regla.NombrePlan is not null && regla.NombrePlan != cliente.PlanSocio) return false;
        if (regla.NombreSede is not null && regla.NombreSede != cliente.Sede) return false;

        // El Y se evalúa antes que el O: las condiciones consecutivas unidas por Y forman un grupo,
        // y la regla se cumple si se cumple algún grupo. Operador = conector con la SIGUIENTE condición.
        var grupoCumple = true;
        for (int i = 0; i < regla.Condiciones.Count; i++)
        {
            var c = regla.Condiciones[i];
            grupoCumple &= c.Atributo switch
            {
                Condicion.NivelRiesgo => Condicion.SepararValores(c.Valor).Contains(nivelRiesgo),
                Condicion.Inactividad => inactividad > int.Parse(c.Valor),
                Condicion.HistorialPagos => (vencido ? "Vencido" : "Al día") == c.Valor,
                Condicion.Actividad => caidaActividad > int.Parse(c.Valor),
                Condicion.IntentosPrevios => Condicion.SepararValores(c.Valor).Contains(intentos),
                _ => false // atributo desconocido: la condición no se cumple
            };

            var cierraGrupo = i == regla.Condiciones.Count - 1 || c.Operador == Condicion.O;
            if (cierraGrupo)
            {
                if (grupoCumple) return true;
                grupoCumple = true;
            }
        }

        return false;
    }

    // --- Helpers privados ---

    private static string DescribirCondicion(Condicion c)
    {
        var def = Condicion.BuscarAtributo(c.Atributo);
        if (def.Nombre is null) return $"{c.Atributo}: {c.Valor}"; // atributo desconocido (dato viejo): se muestra tal cual

        if (def.EsNumerico) return $"{c.Atributo} > {c.Valor} {def.Unidad}".TrimEnd();

        return $"{c.Atributo} es {string.Join(" o ", Condicion.SepararValores(c.Valor))}";
    }

    private static void ValidarNombreYPrioridad(string nombre, int prioridad)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la regla es obligatorio.");
        if (nombre.Trim().Length > Regla.LargoMaximoNombre)
            throw new ArgumentException($"El nombre no puede superar los {Regla.LargoMaximoNombre} caracteres.");
        if (prioridad < Regla.PrioridadMinima || prioridad > Regla.PrioridadMaxima)
            throw new ArgumentException($"La prioridad debe estar entre {Regla.PrioridadMinima} y {Regla.PrioridadMaxima}.");
    }

    // Valida cada condición contra el vocabulario de BE.Condicion y devuelve las filas ya normalizadas
    // (valores múltiples ordenados y sin repetidos; conector de la última = "Y").
    private static List<Condicion> ValidarYNormalizarCondiciones(List<Condicion> condiciones)
    {
        if (condiciones is null || condiciones.Count == 0)
            throw new ArgumentException("Una regla necesita al menos una condición.");

        var filas = new List<Condicion>();
        for (int i = 0; i < condiciones.Count; i++)
        {
            var c = condiciones[i];
            var def = Condicion.BuscarAtributo(c.Atributo);
            if (def.Nombre is null)
                throw new ArgumentException($"Atributo de condición inválido: '{c.Atributo}'.");

            var valor = (c.Valor ?? string.Empty).Trim();
            string valorFinal;

            if (def.EsNumerico)
            {
                if (!int.TryParse(valor, out var n) || n <= 0)
                    throw new ArgumentException($"La condición {i + 1} ({def.Nombre}) requiere un número mayor a 0.");
                valorFinal = n.ToString();
            }
            else
            {
                var elegidos = Condicion.SepararValores(valor).Distinct().ToList();
                if (elegidos.Count == 0)
                    throw new ArgumentException($"La condición {i + 1} ({def.Nombre}) no tiene ningún valor seleccionado.");
                if (elegidos.Any(v => !def.Opciones.Contains(v)))
                    throw new ArgumentException($"La condición {i + 1} ({def.Nombre}) tiene un valor inválido: '{valor}'.");
                if (elegidos.Count > 1 && !def.PermiteMultiples)
                    throw new ArgumentException($"La condición {i + 1} ({def.Nombre}) admite un solo valor.");

                valorFinal = Condicion.UnirValores(def.Opciones.Where(elegidos.Contains)); // orden canónico
            }

            var esUltima = i == condiciones.Count - 1;
            filas.Add(new Condicion
            {
                Atributo = def.Nombre,
                Valor = valorFinal,
                Operador = !esUltima && c.Operador == Condicion.O ? Condicion.O : Condicion.Y
            });
        }

        return filas;
    }

    private static void AgregarInsertsCondicion(StringBuilder sql, Dictionary<string, object?> parametros, List<Condicion> filas, string variableIdRegla)
    {
        for (int i = 0; i < filas.Count; i++)
        {
            parametros[$"atributo{i}"] = filas[i].Atributo;
            parametros[$"operador{i}"] = filas[i].Operador;
            parametros[$"valor{i}"] = filas[i].Valor;

            sql.AppendLine($@"
                DECLARE @IdCond{i} INT;
                INSERT INTO Condicion (Atributo, Operador, Valor)
                VALUES (@atributo{i}, @operador{i}, @valor{i});
                SET @IdCond{i} = SCOPE_IDENTITY();
                INSERT INTO Regla_Condicion (ReglaID, CondicionID) VALUES ({variableIdRegla}, @IdCond{i});");
        }
    }

    private async Task<List<Regla>> CompletarCondiciones(List<Regla> reglas, CancellationToken ct)
    {
        foreach (var regla in reglas)
        {
            regla.Condiciones = await ObtenerCondicionesDeRegla(regla.IdRegla, ct);
            regla.CondicionResumen = ArmarResumen(regla.Condiciones);
        }

        return reglas;
    }

    private async Task<List<Condicion>> ObtenerCondicionesDeRegla(int idRegla, CancellationToken ct)
    {
        var condiciones = await _accesoDatos.Leer<Condicion>(
            @"SELECT con.IdCondicion, con.Atributo, con.Operador, con.Valor
              FROM Condicion con
              JOIN Regla_Condicion rc ON rc.CondicionID = con.IdCondicion
              WHERE rc.ReglaID = @idRegla
              ORDER BY con.IdCondicion",
            new { idRegla }, ct: ct);

        return condiciones.ToList();
    }
}