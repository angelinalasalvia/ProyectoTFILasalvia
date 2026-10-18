using BE;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace UI.Services;

// Vive en la UI, no en la BLL: coincide con tu diagrama, donde
// GenerarArchivoPDF() es un mensaje que la GUI se manda a sí misma.
public class GeneradorPdf
{
    public byte[] GenerarInformePredicciones(List<Prediccion> predicciones, string filtroAplicado)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Informe de Predicciones de Abandono").FontSize(18).Bold();
                    col.Item().Text($"Filtro aplicado: {filtroAplicado}").FontSize(10);
                    col.Item().Text($"Generado el: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(9);
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingTop(15).Table(tabla =>
                {
                    tabla.ColumnsDefinition(columnas =>
                    {
                        columnas.RelativeColumn(3);
                        columnas.RelativeColumn(2);
                        columnas.RelativeColumn(2);
                        columnas.RelativeColumn(4);
                    });
                    tabla.Header(header =>
                    {
                        header.Cell().Text("Cliente").Bold();
                        header.Cell().Text("Probabilidad").Bold();
                        header.Cell().Text("Nivel").Bold();
                        header.Cell().Text("Factor principal").Bold();
                    });
                    foreach (var p in predicciones)
                    {
                        tabla.Cell().Text($"{p.Cliente?.Nombre} {p.Cliente?.Apellido}");
                        tabla.Cell().Text($"{p.ProbabilidadAbandono}%");
                        tabla.Cell().Text(p.NivelRiesgo);
                        tabla.Cell().Text(p.FactorRiesgo?.Nombre ?? "-");
                    }
                });

                page.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });

        return documento.GeneratePdf();
    }
    public byte[] GenerarInformeCampana(Campana campana, List<HistorialAccion> historial,
    int entregados, double tasaApertura, int clics, double tasaClics, int recuperados, double tasaConversion)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Informe de Campaña: {campana.Nombre}").FontSize(18).Bold();
                    col.Item().Text($"Canal: {campana.Canal}").FontSize(10);
                    col.Item().Text($"Generado el: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(9);
                    col.Item().PaddingTop(8).Text($"Tasa de apertura: {tasaApertura:0.0}% ({entregados} entregados)  |  Clics: {tasaClics:0.0}% ({clics})  |  Recuperados: {recuperados}/{campana.Meta ?? 0} ({tasaConversion:0.0}%)").FontSize(10).Bold();
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingTop(15).Table(tabla =>
                {
                    tabla.ColumnsDefinition(columnas =>
                    {
                        columnas.RelativeColumn(3);
                        columnas.RelativeColumn(2);
                        columnas.RelativeColumn(2);
                        columnas.RelativeColumn(2);
                        columnas.RelativeColumn(3);
                    });
                    tabla.Header(header =>
                    {
                        header.Cell().Text("Cliente").Bold();
                        header.Cell().Text("Plan").Bold();
                        header.Cell().Text("Estado").Bold();
                        header.Cell().Text("Resultado").Bold();
                        header.Cell().Text("Fecha acción").Bold();
                    });
                    foreach (var h in historial)
                    {
                        tabla.Cell().Text($"{h.NombreCliente} {h.ApellidoCliente}");
                        tabla.Cell().Text(h.PlanCliente);
                        tabla.Cell().Text(h.EstadoEnvio);
                        tabla.Cell().Text(h.Resultado);
                        tabla.Cell().Text(h.FechaAccion.ToString("dd/MM/yyyy HH:mm"));
                    }
                });

                page.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });

        return documento.GeneratePdf();
    }
    public byte[] GenerarReporteEfectividad(DateTime desde, DateTime hasta,
    List<KpiReporte> kpis, List<CanalReporte> canales, List<IncentivoReporte> incentivos,
    decimal costo, decimal valorRecuperado, string margenNeto)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Reporte de Efectividad").FontSize(18).Bold();
                    col.Item().Text($"Período: {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}").FontSize(10);
                    col.Item().Text($"Generado el: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(9);
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(4); });
                        tabla.Header(h =>
                        {
                            h.Cell().Text("Indicador").Bold();
                            h.Cell().Text("Valor").Bold();
                            h.Cell().Text("Variación").Bold();
                            h.Cell().Text("Detalle").Bold();
                        });
                        foreach (var k in kpis)
                        {
                            tabla.Cell().Text(k.Label);
                            tabla.Cell().Text(k.Valor);
                            tabla.Cell().Text(k.Badge);
                            tabla.Cell().Text(k.Sub);
                        }
                    });

                    col.Item().Text("Rendimiento por canal").FontSize(13).Bold();
                    if (canales.Count == 0)
                        col.Item().Text("Sin datos en el período.");
                    foreach (var c in canales)
                    {
                        col.Item().Text(t =>
                        {
                            t.Span($"{c.Nombre}: ").Bold();
                            t.Span(string.Join("  |  ", c.Metricas.Select(m => $"{m.Label} {m.Valor}")));
                        });
                    }

                    col.Item().Text("Mejores incentivos").FontSize(13).Bold();
                    if (incentivos.Count == 0)
                        col.Item().Text("Sin datos en el período.");
                    foreach (var i in incentivos)
                        col.Item().Text($"{i.Nombre}: {i.Clientes} clientes");

                    col.Item().Text("ROI de retención").FontSize(13).Bold();
                    col.Item().Text($"Costo de incentivos: ${costo:N0}");
                    col.Item().Text($"Valor recuperado (LTV): ${valorRecuperado:N0}");
                    col.Item().Text($"Margen neto: {margenNeto}");
                });

                page.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });

        return documento.GeneratePdf();
    }
    public byte[] GenerarReporteRetencion(DateTime generado, string periodo, string segmento,
    MetricasRetencion metricas, TendenciaChurn tendencia, List<CohorteRetencion> cohortes)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Reporte de Retención").FontSize(18).Bold();
                    col.Item().Text($"Período: {periodo}  |  Segmento: {segmento}").FontSize(10);
                    col.Item().Text($"Generado el: {generado:dd/MM/yyyy HH:mm}").FontSize(9);
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingTop(15).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(3); c.RelativeColumn(3); });
                        t.Header(h =>
                        {
                            h.Cell().Text("Indicador").Bold();
                            h.Cell().Text("Período actual").Bold();
                            h.Cell().Text("Período anterior").Bold();
                        });
                        t.Cell().Text("Churn histórico");
                        t.Cell().Text($"{tendencia.ChurnHistorico:0.0}% ({tendencia.Bajas} bajas)");
                        t.Cell().Text($"{tendencia.ChurnHistoricoAnterior:0.0}% ({tendencia.BajasAnterior} bajas)");
                        t.Cell().Text("Clientes recuperados");
                        t.Cell().Text($"{metricas.ClientesRecuperados}");
                        t.Cell().Text($"{metricas.ClientesRecuperadosAnterior}");
                        t.Cell().Text("Ingresos salvados (MRR)");
                        t.Cell().Text($"${metricas.MrrSalvado:N0}");
                        t.Cell().Text($"${metricas.MrrSalvadoAnterior:N0}");
                    });

                    col.Item().Text("Tendencias de churn").FontSize(13).Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(5); c.RelativeColumn(2); c.RelativeColumn(2); });
                        t.Header(h =>
                        {
                            h.Cell().Text("Tramo").Bold();
                            h.Cell().Text("Churn actual").Bold();
                            h.Cell().Text("Churn anterior").Bold();
                        });
                        for (var i = 0; i < tendencia.Puntos.Count; i++)
                        {
                            var p = tendencia.Puntos[i];
                            var ant = i < tendencia.PuntosAnterior.Count ? tendencia.PuntosAnterior[i] : null;
                            t.Cell().Text($"{p.Desde:dd/MM/yyyy} - {p.Hasta:dd/MM/yyyy}");
                            t.Cell().Text($"{p.Churn:0.0}%");
                            t.Cell().Text(ant is null ? "-" : $"{ant.Churn:0.0}%");
                        }
                    });

                    col.Item().Text("Análisis de cohortes (retención %)").FontSize(13).Bold();
                    if (cohortes.Count == 0)
                    {
                        col.Item().Text("Sin cohortes en el período.");
                    }
                    else
                    {
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3); c.RelativeColumn(2);
                                for (var i = 0; i < 6; i++) c.RelativeColumn(1);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Text("Cohorte").Bold();
                                h.Cell().Text("Clientes").Bold();
                                for (var i = 0; i < 6; i++) h.Cell().Text($"Mes {i}").Bold();
                            });
                            foreach (var fila in cohortes)
                            {
                                t.Cell().Text(fila.Nombre);
                                t.Cell().Text(fila.Tamano.ToString());
                                foreach (var v in fila.Valores)
                                    t.Cell().Text(v.HasValue ? $"{v}%" : "-");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });

        return documento.GeneratePdf();
    }
}