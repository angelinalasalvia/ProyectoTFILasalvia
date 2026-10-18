namespace UI.Services;

public record KpiReporte(string Icono, string Label, string Valor, string Sub, string Badge, string BadgeClase);
public record MetricaCanalReporte(string Label, string Valor, int Pct, string ColorClase);
public record CanalReporte(string Nombre, string Icono, List<MetricaCanalReporte> Metricas);
public record IncentivoReporte(string Nombre, int Clientes, int PctBarra);