namespace CatCar.AuthFunction.Tests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

public sealed class CustomerLookupServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindByDocumentNumberAsync_CustomerExists_ReturnsCustomer(bool active)
    {
        var id = Guid.NewGuid();
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id, isActive = active }),
        });
        using var client = CreateClient(handler);
        var result = await new CustomerLookupService(client).FindByDocumentNumberAsync("123", CancellationToken.None);
        result.Should().Be(new CustomerLookupResult(id, active));
    }

    [Fact]
    public async Task FindByDocumentNumberAsync_NotFound_ReturnsNull()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = CreateClient(handler);
        var result = await new CustomerLookupService(client).FindByDocumentNumberAsync("123", CancellationToken.None);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FindByDocumentNumberAsync_ErrorStatus_Throws(HttpStatusCode status)
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(status));
        using var client = CreateClient(handler);
        var act = () => new CustomerLookupService(client).FindByDocumentNumberAsync("123", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FindByDocumentNumberAsync_NetworkFailure_Throws()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("network"));
        using var client = CreateClient(handler);
        var act = () => new CustomerLookupService(client).FindByDocumentNumberAsync("123", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FindByDocumentNumberAsync_Timeout_Throws()
    {
        using var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        using var client = CreateClient(handler);
        var act = () => new CustomerLookupService(client).FindByDocumentNumberAsync("123", CancellationToken.None);
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task FindByDocumentNumberAsync_SpecialCharacters_EscapesPathAndSendsKey()
    {
        string? path = null;
        string? key = null;
        using var handler = new StubHandler(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            key = request.Headers.GetValues("X-Internal-Api-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = CreateClient(handler);
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", "test-key");
        await new CustomerLookupService(client).FindByDocumentNumberAsync("12/34?5", CancellationToken.None);
        path.Should().Be("/internal/v1/customers/by-document/12%2F34%3F5");
        key.Should().Be("test-key");
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, false)
    {
        BaseAddress = new Uri("http://localhost"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
