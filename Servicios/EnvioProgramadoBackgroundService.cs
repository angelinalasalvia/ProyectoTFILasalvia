using BLL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Servicios;

// Envía las acciones de retención manuales que quedaron "Programadas" (CU03) cuando llega su fecha y hora.
public class EnvioProgramadoBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnvioProgramadoBackgroundService> _logger;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    public EnvioProgramadoBackgroundService(IServiceScopeFactory scopeFactory, ILogger<EnvioProgramadoBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            await EjecutarCicloAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EjecutarCicloAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var historial = scope.ServiceProvider.GetRequiredService<BLLHistorialAcciones>();
            var enviadas = await historial.EnviarAccionesProgramadas(DateTime.Now, stoppingToken);

            if (enviadas > 0)
                _logger.LogInformation("Se procesaron {Cantidad} acciones de retención programadas.", enviadas);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // apagado de la aplicación
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando las acciones de retención programadas.");
        }
    }
}

