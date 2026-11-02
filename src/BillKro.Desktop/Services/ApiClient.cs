using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BillKro.Shared;

namespace BillKro.Desktop.Services;

public class ApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Thin JSON client for BillKro.Server. Converts error responses into readable <see cref="ApiException"/>s.</summary>
public class ApiClient(Session session)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private HttpClient _http = new() { Timeout = Timeout };

    /// <summary>Raised when a signed-in request is rejected with 401 (expired or revoked session).</summary>
    public event Action? SessionRejected;

    public Uri? ServerUri => _http.BaseAddress;

    public void SetServer(string url)
    {
        if (!Uri.TryCreate(url.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new ApiException("Server address must look like http://SERVER-PC:5080");

        if (uri == _http.BaseAddress) return;
        _http.Dispose();
        _http = new HttpClient { BaseAddress = uri, Timeout = Timeout };
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Get, path, null, ct), ct);

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Post, path, body, ct), ct);

    public async Task PostAsync(string path, object body, CancellationToken ct = default) =>
        (await SendAsync(HttpMethod.Post, path, body, ct)).Dispose();

    public async Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Put, path, body, ct), ct);

    public async Task DeleteAsync(string path, CancellationToken ct = default) =>
        (await SendAsync(HttpMethod.Delete, path, null, ct)).Dispose();

    /// <summary>Uploads a file as multipart form field "file".</summary>
    public async Task<T> UploadAsync<T>(string path, string filePath, CancellationToken ct = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await System.IO.File.ReadAllBytesAsync(filePath, ct)), "file", System.IO.Path.GetFileName(filePath));
        return await ReadAsync<T>(await SendAsync(HttpMethod.Post, path, form, ct), ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        using (res)
            return (await res.Content.ReadFromJsonAsync<T>(PosJson.Options, ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (_http.BaseAddress is null) throw new ApiException("Server address is not set.");

        using var req = new HttpRequestMessage(method, path.TrimStart('/'));
        req.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, body.GetType(), options: PosJson.Options),
        };
        var token = session.Token;
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException($"Cannot reach the server at {_http.BaseAddress}. Check that BillKro Server is running and the address is correct.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException("The server took too long to respond. Please try again.");
        }

        if (res.IsSuccessStatusCode) return res;

        using (res)
        {
            var message = await ReadProblemAsync(res, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && token is not null)
                SessionRejected?.Invoke();
            throw new ApiException(message, res.StatusCode);
        }
    }

    /// <summary>Builds a user-facing message from an RFC 7807 problem response.</summary>
    private static async Task<string> ReadProblemAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject()
                        .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString()))
                        .Where(m => !string.IsNullOrEmpty(m));
                    return string.Join(Environment.NewLine, messages);
                }
                if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d) return d;
            }
        }
        catch (JsonException)
        {
            // Fall through to a status-based message.
        }

        return res.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has ended. Please sign in again.",
            HttpStatusCode.Forbidden => "You don't have permission to do this.",
            HttpStatusCode.NotFound => "The record was not found. It may have been removed.",
            _ => $"The server returned an error ({(int)res.StatusCode}).",
        };
    }
}
