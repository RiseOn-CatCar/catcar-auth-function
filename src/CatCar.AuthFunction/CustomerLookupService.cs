namespace CatCar.AuthFunction;

using System.Net;
using System.Net.Http.Json;

public interface ICustomerLookupService
{
    Task<CustomerLookupResult?> FindByDocumentNumberAsync(string documentNumber, CancellationToken cancellationToken);
}


public sealed class CustomerLookupService(HttpClient client) : ICustomerLookupService
{
    public async Task<CustomerLookupResult?> FindByDocumentNumberAsync(
        string documentNumber,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/internal/v1/customers/by-document/{Uri.EscapeDataString(documentNumber)}",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerLookupResult>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Customer lookup returned an empty response.");
    }
}

public sealed record CustomerLookupResult(Guid Id, bool IsActive);
