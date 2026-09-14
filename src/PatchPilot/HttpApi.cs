using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PatchPilot;

/// <summary>HTTPS-only clients with redirects disabled so credentials cannot follow remote redirects.</summary>
public static class HttpApi
{
    public static HttpClient Client(string endpoint, string? token = null, string? basicUser = null)
    {
        var uri = new Uri(endpoint);
        if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("A credential-free HTTPS endpoint is required.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(120), MaxResponseContentBufferSize = 4_000_000 };
        if (token != null)
            client.DefaultRequestHeaders.Authorization = basicUser == null
                ? new AuthenticationHeaderValue("Bearer", token)
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basicUser + ":" + token)));
        return client;
    }
    public static async Task<JsonElement> Send(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Remote API returned HTTP " + (int)response.StatusCode +
                ". No response body or credentials were logged.");
        var raw = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
    public static HttpRequestMessage Post(string path, object body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
}
