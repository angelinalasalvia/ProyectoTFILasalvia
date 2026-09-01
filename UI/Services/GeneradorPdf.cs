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
}