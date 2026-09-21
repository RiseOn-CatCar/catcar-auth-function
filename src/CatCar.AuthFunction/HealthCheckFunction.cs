namespace CatCar.AuthFunction;

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

public sealed partial class HealthCheckFunction(NpgsqlDataSource dataSource, ILogger<HealthCheckFunction> logger)
{
    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            var response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync("Healthy", cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (NpgsqlException ex)
        {
            LogHealthCheckFailed(logger, ex);
            var response = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync("Unhealthy", cancellationToken).ConfigureAwait(false);
            return response;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Auth Function readiness check failed to query PostgreSQL.")]
    private static partial void LogHealthCheckFailed(ILogger logger, Exception exception);
}
