namespace CatCar.AuthFunction.Tests;

using CatCar.AuthFunction;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

public sealed class CorrelationMiddlewareTests
{
    [Fact]
    public void ApplyCorrelation_ValidHeader_PreservesAndEchoesGuid()
    {
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = id.ToString();

        CorrelationMiddleware.ApplyCorrelation(context);

        context.Items["CorrelationId"].Should().Be(id);
        context.Response.Headers["X-Correlation-Id"].ToString().Should().Be(id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void ApplyCorrelation_InvalidHeader_GeneratesAndEchoesGuid(string header)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = header;

        CorrelationMiddleware.ApplyCorrelation(context);

        var id = (Guid)context.Items["CorrelationId"]!;
        id.Should().NotBeEmpty();
        context.Response.Headers["X-Correlation-Id"].ToString().Should().Be(id.ToString());
    }
}
