using System.Net;
using System.Text.Json;

namespace Characterization.Harness;

public sealed record Reply(HttpStatusCode Status, string Body, string? Location, string? ContentType)
{
    public int Code => (int)Status;
    public JsonDocument Json() => JsonDocument.Parse(Body);
    public JsonElement Root => Json().RootElement;
    public IEnumerable<string> Errors => Root.GetProperty("Errors").EnumerateArray().Select(e => e.GetString()!);
}

/// <summary>
/// One browser: its own cookie jar (so its own ASP.NET session), never following redirects, so a test can
/// assert on the redirect itself. Pass json:true to get the view model as JSON instead of HTML.
/// </summary>
public sealed class ConfRegClient : IDisposable
{
    private readonly HttpClient _http;

    public ConfRegClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        _http = new HttpClient(handler) { BaseAddress = new Uri(TestEnvironment.BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public Task<Reply> Get(string path, bool json = true) => Send(HttpMethod.Get, path, null, json);

    public Task<Reply> Post(string path, params (string Key, string Value)[] form) =>
        Send(HttpMethod.Post, path, new FormUrlEncodedContent(form.Select(f => KeyValuePair.Create(f.Key, f.Value))), true);

    public Task<Reply> PostForm(string path, IEnumerable<KeyValuePair<string, string>> form) =>
        Send(HttpMethod.Post, path, new FormUrlEncodedContent(form), true);

    private async Task<Reply> Send(HttpMethod method, string path, HttpContent? content, bool json)
    {
        if (json) path += (path.Contains('?') ? "&" : "?") + "format=json";
        using var req = new HttpRequestMessage(method, path.TrimStart('/')) { Content = content };
        using var res = await _http.SendAsync(req);
        return new Reply(res.StatusCode, await res.Content.ReadAsStringAsync(), res.Headers.Location?.ToString(), res.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose() => _http.Dispose();
}
