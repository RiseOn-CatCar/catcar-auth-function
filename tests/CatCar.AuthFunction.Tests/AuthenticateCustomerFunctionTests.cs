namespace CatCar.AuthFunction.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CatCar.AuthFunction;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

public sealed class AuthenticateCustomerFunctionTests
{
    private const string SigningKey = "test-signing-key-with-at-least-thirty-two-characters";
    private const string ValidCpf = "52998224725";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Run_WithInvalidDocument_ReturnsBadRequest()
    {
        var customerLookup = Substitute.For<ICustomerLookupService>();
        var function = CreateFunction(customerLookup);

        var response = await function.Run(CreatePostRequest("{\"documentNumber\":\"invalid\"}"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _ = customerLookup.DidNotReceive().FindByDocumentNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WithUnknownCustomer_ReturnsNotFoundProblem()
    {
        var customerLookup = Substitute.For<ICustomerLookupService>();
        customerLookup.FindByDocumentNumberAsync(Arg.Is<string>(ValidCpf), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerLookupResult?>(null));
        var function = CreateFunction(customerLookup);

        var response = await function.Run(CreatePostRequest($"{{\"documentNumber\":\"{ValidCpf}\"}}"), CancellationToken.None);
        var problem = await ReadBodyAsync<ProblemDetailsResponse>(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem.Title.Should().Be("Customer not found");
        problem.Status.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Run_WithInactiveCustomer_ReturnsForbiddenProblem()
    {
        var customerLookup = Substitute.For<ICustomerLookupService>();
        customerLookup.FindByDocumentNumberAsync(Arg.Is<string>(ValidCpf), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerLookupResult?>(new CustomerLookupResult(Guid.NewGuid(), false)));
        var function = CreateFunction(customerLookup);

        var response = await function.Run(CreatePostRequest($"{{\"documentNumber\":\"{ValidCpf}\"}}"), CancellationToken.None);
        var problem = await ReadBodyAsync<ProblemDetailsResponse>(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        problem.Title.Should().Be("Customer is inactive");
        problem.Status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Run_WithActiveCustomer_IssuesTokenWithPersistedCustomerIdAndClaims()
    {
        var customerId = Guid.NewGuid();
        var customerLookup = Substitute.For<ICustomerLookupService>();
        customerLookup.FindByDocumentNumberAsync(Arg.Is<string>(ValidCpf), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerLookupResult?>(new CustomerLookupResult(customerId, true)));
        var function = CreateFunction(customerLookup);

        var response = await function.Run(CreatePostRequest($"{{\"documentNumber\":\"{ValidCpf}\"}}"), CancellationToken.None);
        var authentication = await ReadBodyAsync<CustomerAuthenticationResponse>(response);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            authentication.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "CatCar",
                ValidateAudience = true,
                ValidAudience = "CatCar.Api",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ClockSkew = TimeSpan.Zero,
            },
            out var validatedToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        authentication.ExpiresIn.Should().Be(3600);
        authentication.TokenType.Should().Be("Bearer");
        validatedToken.Should().BeOfType<JwtSecurityToken>();
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(ValidCpf);
        principal.FindFirst("customer_id")?.Value.Should().Be(customerId.ToString());
        principal.FindFirst("role")?.Value.Should().Be("Customer");
    }

    [Fact]
    public async Task Run_GetWithDocumentNumber_ReturnsDocumentationWithoutIssuingToken()
    {
        var customerLookup = Substitute.For<ICustomerLookupService>();
        var function = CreateFunction(customerLookup);

        var response = await function.Run(CreateGetRequest($"?documentNumber={ValidCpf}"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Type").Should().Contain("text/html; charset=utf-8");
        _ = customerLookup.DidNotReceive().FindByDocumentNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Issue_WithSigningKeyShorterThanThirtyTwoCharacters_Throws()
    {
        var issuer = new CustomerTokenIssuer(Options.Create(new CustomerJwtOptions { SigningKey = "short-signing-key" }));

        var issue = () => issuer.Issue(Guid.NewGuid(), ValidCpf);

        issue.Should().Throw<InvalidOperationException>()
            .WithMessage("CustomerJwt:SigningKey must contain at least 32 characters.");
    }

    [Theory]
    [InlineData("12345678900")]
    [InlineData("04252011000111")]
    [InlineData("11111111111")]
    [InlineData("52998224725ABC")]
    [InlineData("not-a-document")]
    public void IsValid_WithInvalidCpfOrCnpj_ReturnsFalse(string documentNumber)
    {
        DocumentNumberValidator.IsValid(documentNumber).Should().BeFalse();
    }

    [Theory]
    [InlineData("52998224725")]
    [InlineData("04252011000110")]
    public void IsValid_WithCpfOrCnpjUsingCorrectCheckDigits_ReturnsTrue(string documentNumber)
    {
        DocumentNumberValidator.IsValid(documentNumber).Should().BeTrue();
    }

    private static AuthenticateCustomerFunction CreateFunction(ICustomerLookupService customerLookup) =>
        new(
            new CustomerTokenIssuer(Options.Create(new CustomerJwtOptions { SigningKey = SigningKey })),
            customerLookup,
            Options.Create(new CustomerJwtOptions { SigningKey = SigningKey }));

    private static HttpRequestData CreatePostRequest(string body)
    {
        var context = Substitute.For<FunctionContext>();
        var request = Substitute.For<HttpRequestData>(context);
        request.Method.Returns("POST");
        request.Url.Returns(new Uri("https://localhost/api/auth/customer"));
        request.Body.Returns(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        request.CreateResponse().Returns(_ => new TestHttpResponseData(context));

        return request;
    }

    private static HttpRequestData CreateGetRequest(string query)
    {
        var context = Substitute.For<FunctionContext>();
        var request = Substitute.For<HttpRequestData>(context);
        request.Method.Returns("GET");
        request.Url.Returns(new Uri($"https://localhost/api/auth/customer{query}"));
        request.CreateResponse().Returns(_ => new TestHttpResponseData(context));

        return request;
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseData response)
    {
        response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync<T>(response.Body, SerializerOptions))
            ?? throw new InvalidOperationException("Response body did not contain JSON.");
    }

    private sealed class TestHttpResponseData(FunctionContext functionContext) : HttpResponseData(functionContext)
    {
        public override Stream Body { get; set; } = new MemoryStream();

        public override HttpCookies Cookies { get; } = Substitute.For<HttpCookies>();

        public override HttpHeadersCollection Headers { get; set; } = new();

        public override HttpStatusCode StatusCode { get; set; }
    }
}
