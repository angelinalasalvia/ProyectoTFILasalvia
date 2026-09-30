namespace BE;

// Mapea la tabla Plan (catálogo de referencia). Se llama "PlanSocio" y no
// "Plan" para que coincida con el nombre que ya usa Cliente.PlanSocio, que
// es el campo contra el que se va a comparar en el motor de evaluación.
public class PlanSocio
{
    public int IdPlan { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
