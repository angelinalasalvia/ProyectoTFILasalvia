using BE;
using DAL;

namespace BLL;

public class BLLRegla
{
    private readonly AccesoDatos _accesoDatos;

    public BLLRegla(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    // VecesEjecutada: HistorialAcciones no tiene FK directa a Regla (ver DER),
    // así que se aproxima contando las acciones de la campaña asociada a la
    // regla. Si en algún momento agregás una columna IdRegla a
    // HistorialAcciones, se puede reemplazar por un conteo exacto.
    private const string SelectBase = @"
        SELECT r.IdRegla, r.Estado, r.FechaCreacion, r.IdCampaña AS IdCampania, r.Nombre,
               r.IdSede, r.IdPlan,
               camp.Asunto AS NombreCampania, ca.Nombre AS Canal,
               sede.Nombre AS NombreSede, pl.Nombre AS NombrePlan,
               (SELECT COUNT(*) FROM HistorialAcciones h WHERE h.IdCampaña = r.IdCampaña) AS VecesEjecutada
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

    // CU10, pasos 5-6: búsqueda por nombre.
    public async Task<List<Regla>> BuscarReglaPorNombre(string nombre, CancellationToken ct = default)
    {
        var reglas = (await _accesoDatos.Leer<Regla>(
            $"{SelectBase} WHERE r.Nombre LIKE @nombre ORDER BY r.FechaCreacion DESC",
            new { nombre = $"%{nombre}%" }, ct: ct)).ToList();
        return await CompletarCondiciones(reglas, ct);
    }

    // CU10, pasos 7-8: "Filtrar por" (Más recientes / Más ejecutadas / Alfabético).
    public async Task<List<Regla>> ObtenerReglasOrdenadas(string criterio, CancellationToken ct = default)
    {
        var orderBy = criterio switch
        {
            "Alfabético" => "r.Nombre ASC",
            _ => "r.FechaCreacion DESC" // "Más recientes" (default) y "Más ejecutadas" (se reordena abajo)
        };

        var reglas = (await _accesoDatos.Leer<Regla>($"{SelectBase} ORDER BY {orderBy}", ct: ct)).ToList();
        var completas = await CompletarCondiciones(reglas, ct);

        if (criterio == "Más ejecutadas")
            completas = completas.OrderByDescending(r => r.VecesEjecutada).ToList();

        return completas;
    }

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

    // CU10, alterno 11.3: eliminar. Borra primero las Condicion propias de la
    // regla (y el vínculo Regla_Condicion) porque la base no tiene ON DELETE
    // CASCADE configurado, y recién después la Regla. Todo en un solo
    // Eliminar() para que quede dentro de la misma transacción.
    public Task EliminarRegla(int idRegla, CancellationToken ct = default)
        => _accesoDatos.Eliminar(
              @"DECLARE @condiciones TABLE (IdCondicion INT);
              INSERT INTO @condiciones SELECT CondicionID FROM Regla_Condicion WHERE ReglaID = @idRegla;
              DELETE FROM Regla_Condicion WHERE ReglaID = @idRegla;
              DELETE FROM Regla WHERE IdRegla = @idRegla;
              DELETE FROM Condicion WHERE IdCondicion IN (SELECT IdCondicion FROM @condiciones);",
            new { idRegla }, ct: ct);

    public Task CrearRegla(string nombre, int idCampania, List<Condicion> condiciones, int? idSede = null, int? idPlan = null, CancellationToken ct = default)
    {
        if (condiciones.Count == 0)
            throw new ArgumentException("Una regla necesita al menos una condición.", nameof(condiciones));

        var sql = new System.Text.StringBuilder();
        var parametros = new Dictionary<string, object?>
        {
            ["nombre"] = nombre,
            ["idCampania"] = idCampania,
            ["fechaCreacion"] = DateTime.Now,
            ["estado"] = EstadosRegla.Activa,
            ["idSede"] = idSede,
            ["idPlan"] = idPlan,
        };

        sql.AppendLine(@"
            DECLARE @NuevoIdRegla INT;
            INSERT INTO Regla (Estado, FechaCreacion, IdCampaña, Nombre, IdSede, IdPlan)
            VALUES (@estado, @fechaCreacion, @idCampania, @nombre, @idSede, @idPlan);
            SET @NuevoIdRegla = SCOPE_IDENTITY();");

        for (int i = 0; i < condiciones.Count; i++)
        {
            var c = condiciones[i];
            parametros[$"atributo{i}"] = c.Atributo;
            parametros[$"operador{i}"] = c.Operador;
            parametros[$"valor{i}"] = c.Valor;

            sql.AppendLine($@"
                DECLARE @IdCond{i} INT;
                INSERT INTO Condicion (Atributo, Operador, Valor)
                VALUES (@atributo{i}, @operador{i}, @valor{i});
                SET @IdCond{i} = SCOPE_IDENTITY();
                INSERT INTO Regla_Condicion (ReglaID, CondicionID) VALUES (@NuevoIdRegla, @IdCond{i});");
        }

        return _accesoDatos.Escribir(sql.ToString(), parametros, ct: ct);
    }

    // Para CrearRegla.razor en modo edición: actualiza nombre, campaña,
    // sede/plan y reemplaza todas las condiciones (se borran las viejas y
    // se insertan las nuevas). Todo en un solo Escribir() para que sea una
    // sola transacción.
    public Task ModificarRegla(int idRegla, string nombre, int idCampania, List<Condicion> condiciones, int? idSede = null, int? idPlan = null, CancellationToken ct = default)
    {
        if (condiciones.Count == 0)
            throw new ArgumentException("Una regla necesita al menos una condición.", nameof(condiciones));

        var sql = new System.Text.StringBuilder();
        var parametros = new Dictionary<string, object?>
        {
            ["idRegla"] = idRegla,
            ["nombre"] = nombre,
            ["idCampania"] = idCampania,
            ["idSede"] = idSede,
            ["idPlan"] = idPlan,
        };

        sql.AppendLine(@"
            UPDATE Regla SET Nombre = @nombre, IdCampaña = @idCampania, IdSede = @idSede, IdPlan = @idPlan
            WHERE IdRegla = @idRegla;

            DECLARE @condiciones TABLE (IdCondicion INT);
            INSERT INTO @condiciones SELECT CondicionID FROM Regla_Condicion WHERE ReglaID = @idRegla;
            DELETE FROM Regla_Condicion WHERE ReglaID = @idRegla;
            DELETE FROM Condicion WHERE IdCondicion IN (SELECT IdCondicion FROM @condiciones);");

        for (int i = 0; i < condiciones.Count; i++)
        {
            var c = condiciones[i];
            parametros[$"atributo{i}"] = c.Atributo;
            parametros[$"operador{i}"] = c.Operador;
            parametros[$"valor{i}"] = c.Valor;

            sql.AppendLine($@"
                DECLARE @IdCond{i} INT;
                INSERT INTO Condicion (Atributo, Operador, Valor)
                VALUES (@atributo{i}, @operador{i}, @valor{i});
                SET @IdCond{i} = SCOPE_IDENTITY();
                INSERT INTO Regla_Condicion (ReglaID, CondicionID) VALUES (@idRegla, @IdCond{i});");
        }

        return _accesoDatos.Escribir(sql.ToString(), parametros, ct: ct);
    }

    // Para CrearRegla.razor en modo edición (/automatizacion/editar-regla/{id}):
    // trae nombre, campaña y condiciones actuales para precargar el formulario.
    public async Task<Regla?> ObtenerReglaPorId(int idRegla, CancellationToken ct = default)
    {
        var reglas = await _accesoDatos.Leer<Regla>($"{SelectBase} WHERE r.IdRegla = @idRegla", new { idRegla }, ct: ct);
        var regla = reglas.FirstOrDefault();
        if (regla is null) return null;

        regla.Condiciones = await ObtenerCondicionesDeRegla(idRegla, ct);
        regla.CondicionResumen = ArmarResumen(regla.Condiciones);
        return regla;
    }

    // --- Helpers privados (arman datos de BE.Condicion, no crean clases nuevas) ---

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

    private static string ArmarResumen(List<Condicion> condiciones)
    {
        if (condiciones.Count == 0) return string.Empty;

        var partes = new List<string> { $"{condiciones[0].Atributo}: {condiciones[0].Valor}" };
        for (int i = 1; i < condiciones.Count; i++)
        {
            // El conector (Y/O) que precede a esta condición es el Operador
            // de la condición ANTERIOR (ver nota en BE.Condicion).
            partes.Add($"{condiciones[i - 1].Operador} {condiciones[i].Atributo}: {condiciones[i].Valor}");
        }
        return string.Join(" ", partes);
    }
}