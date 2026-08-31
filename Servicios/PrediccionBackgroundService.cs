using BE;
using BLL;
using DAL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Servicios;
public class PrediccionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrediccionBackgroundService> _logger;

    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    public PrediccionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PrediccionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var periodicTimer = new PeriodicTimer(Intervalo);

        await EjecutarSiCorrespondeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested &&
               await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarCicloAsync(stoppingToken);
        }
    }

    // Se llama solo al arrancar la app: chequea si ya pasó suficiente tiempo
    // desde la última corrida antes de reentrenar. Evita reentrenar de más
    // cada vez que reiniciás la app durante el desarrollo.
    private async Task EjecutarSiCorrespondeAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repositorio = scope.ServiceProvider.GetRequiredService<IChurnRepository>();
            var ultimaEjecucion = await repositorio.ObtenerUltimaEjecucionAsync(NombresModelo.RandomForestChurn, stoppingToken);

            bool haceFalta = ultimaEjecucion == null || (DateTime.Now - ultimaEjecucion.Value) >= Intervalo;

            if (haceFalta)
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            else
            {
                _logger.LogInformation(
                    "Se omite el ciclo de arranque: el modelo ya corrió hace menos de {Horas}hs (última vez: {Fecha}).",
                    Intervalo.TotalHours, ultimaEjecucion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verificando la última ejecución. Se ejecuta el ciclo de todas formas.");
            await EjecutarCicloAsync(stoppingToken);
        }
    }

    /*protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var periodicTimer = new PeriodicTimer(Intervalo);

        await EjecutarCicloAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested &&
               await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarCicloAsync(stoppingToken);
        }
    }*/

    private async Task EjecutarCicloAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var churnService = scope.ServiceProvider.GetRequiredService<ChurnModelService>();
            await churnService.EntrenarYPredecirAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el ciclo automático de predicción de churn.");
        }
    }
}

