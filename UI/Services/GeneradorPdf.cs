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
}