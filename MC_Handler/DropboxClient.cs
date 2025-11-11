using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class DropboxClient
{
    private readonly string _appKey;
    private readonly string _appSecret;
    private readonly string _refreshToken;

    private string _accessToken;
    private readonly HttpClient _http;

    public DropboxClient(string accessToken, string refreshToken = null, string appKey = null, string appSecret = null)
    {
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        _refreshToken = refreshToken;
        _appKey = appKey;
        _appSecret = appSecret;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private async Task<bool> TryRefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_refreshToken) || string.IsNullOrWhiteSpace(_appKey) || string.IsNullOrWhiteSpace(_appSecret))
        {
            Console.WriteLine("[Dropbox] Cannot refresh access token — missing refresh credentials.");
            return false;
        }

        try
        {
            Console.WriteLine("[Dropbox] Refreshing access token...");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", _refreshToken),
                new KeyValuePair<string, string>("client_id", _appKey),
                new KeyValuePair<string, string>("client_secret", _appSecret)
            });

            _http.DefaultRequestHeaders.Authorization = null;

            var resp = await _http.PostAsync("https://api.dropbox.com/oauth2/token", content);
            var json = await resp.Content.ReadAsStringAsync();

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Dropbox] Token refresh failed: {resp.StatusCode} {json}");
                return false;
            }

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElem))
            {
                _accessToken = tokenElem.GetString();
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                Console.WriteLine("[Dropbox] Access token refreshed successfully.");

                SaveNewAccessToken(_accessToken);
                return true;
            }

            Console.WriteLine("[Dropbox] Unexpected response while refreshing token.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dropbox] Refresh error: {ex.Message}");
            return false;
        }
    }

    private void SaveNewAccessToken(string newToken)
    {
        try
        {
            const string configFile = "appsettings.json";
            if (!File.Exists(configFile)) return;

            var text = File.ReadAllText(configFile);
            using var jsonDoc = JsonDocument.Parse(text);
            var root = jsonDoc.RootElement.Clone();

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("Dropbox"))
                    {
                        writer.WritePropertyName("Dropbox");
                        writer.WriteStartObject();
                        foreach (var sub in prop.Value.EnumerateObject())
                        {
                            if (sub.NameEquals("AccessToken"))
                                writer.WriteString("AccessToken", newToken);
                            else
                                sub.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            File.WriteAllBytes(configFile, ms.ToArray());
            Console.WriteLine("[Dropbox] Saved new access token to appsettings.json.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dropbox] Failed to save updated token: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage req)
    {
        var resp = await _http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            if (msg.Contains("expired_access_token") || msg.Contains("invalid_access_token"))
            {
                Console.WriteLine("[Dropbox] Access token expired — refreshing...");
                if (await TryRefreshTokenAsync())
                {
                    req.Headers.Remove("Authorization");
                    _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    resp = await _http.SendAsync(req);
                }
            }
        }

        return resp;
    }

    public async Task<bool> UploadFileAsync(string dropboxPath, string localFilePath)
    {
        try
        {
            if (!File.Exists(localFilePath))
            {
                Console.WriteLine($"Upload skipped: file not found {localFilePath}");
                return false;
            }

            using var fs = File.OpenRead(localFilePath);
            using var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var req = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
            req.Headers.Add("Dropbox-API-Arg", $"{{\"path\":\"{dropboxPath}\",\"mode\":\"overwrite\"}}");
            req.Content = content;

            Console.WriteLine($"Uploading {localFilePath} → {dropboxPath} ...");
            var resp = await SendWithRetryAsync(req);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Upload failed: {resp.StatusCode} {msg}");
                return false;
            }

            Console.WriteLine("Upload finished.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Upload error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(string dropboxPath, string localFilePath)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
            req.Headers.Add("Dropbox-API-Arg", $"{{\"path\":\"{dropboxPath}\"}}");

            Console.WriteLine($"Downloading {dropboxPath} → {localFilePath} ...");
            var resp = await SendWithRetryAsync(req);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Download failed: {resp.StatusCode} {msg}");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localFilePath)!);
            using var fs = File.Create(localFilePath);
            await resp.Content.CopyToAsync(fs);

            Console.WriteLine("Download finished.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Download error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(string dropboxPath)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/get_metadata");
            req.Content = new StringContent($"{{\"path\":\"{dropboxPath}\"}}", Encoding.UTF8, "application/json");

            var resp = await SendWithRetryAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Metadata check error: {ex.Message}");
            return false;
        }
    }
}
