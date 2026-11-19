namespace CatCar.AuthFunction;

using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

public sealed class CorrelationMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is not null)
        {
            ApplyCorrelation(httpContext);
        }

        await next(context).ConfigureAwait(false);
    }

    public static void ApplyCorrelation(HttpContext context)
    {
        const string header = "X-Correlation-Id";
        var values = context.Request.Headers[header];
        var id = values.Count == 1 && Guid.TryParse(values[0], out var parsed) ? parsed : Guid.NewGuid();
        context.Items["CorrelationId"] = id;
        context.Response.Headers[header] = id.ToString();
    }
}
