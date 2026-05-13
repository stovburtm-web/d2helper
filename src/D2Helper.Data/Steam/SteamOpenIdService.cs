using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace D2Helper.Data.Steam;

/// <summary>
/// Реалізує "Sign in through Steam" (OpenID 2.0).
/// Запускає локальний HTTP-listener на random-порту → відкриває браузер → ловить redirect → верифікує підпис.
/// </summary>
public sealed class SteamOpenIdService
{
    private const string SteamOpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private static readonly Regex SteamIdRegex =
        new(@"https://steamcommunity\.com/openid/id/(?<id>\d{17})", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public SteamOpenIdService(HttpClient http) => _http = http;

    /// <summary>
    /// Запускає інтерактивну авторизацію. Блокується доки користувач не залогіниться або не закриє браузер
    /// (max <paramref name="timeout"/>).
    /// </summary>
    public async Task<long?> SignInAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        // 1. Підняти listener на ефемерному порту.
        var (listener, callbackUrl) = StartListener();
        try
        {
            // 2. Сформувати URL і відкрити браузер.
            var authUrl = BuildAuthUrl(callbackUrl);
            OpenBrowser(authUrl);

            // 3. Дочекатись callback'у.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));

            HttpListenerContext context;
            using (timeoutCts.Token.Register(listener.Stop))
            {
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { return null; } // listener closed by timeout
                catch (ObjectDisposedException) { return null; }
            }

            // 4. Розпарсити query.
            var query = context.Request.Url?.Query ?? "";
            await WriteBrowserResponseAsync(context, query).ConfigureAwait(false);
            if (string.IsNullOrEmpty(query)) return null;

            var parsed = HttpUtility.ParseQueryString(query);
            var claimedId = parsed["openid.claimed_id"];
            if (string.IsNullOrEmpty(claimedId)) return null;

            // 5. Витягти SteamID64.
            var m = SteamIdRegex.Match(claimedId);
            if (!m.Success) return null;
            if (!long.TryParse(m.Groups["id"].Value, out var steamId64)) return null;

            // 6. Верифікація підпису у Steam (`check_authentication`).
            if (!await VerifyAsync(parsed, ct).ConfigureAwait(false)) return null;

            return steamId64;
        }
        finally
        {
            try { listener.Close(); } catch { /* ignore */ }
        }
    }

    private static (HttpListener listener, string callbackUrl) StartListener()
    {
        // 0 = OS даcть вільний порт. Але HttpListener не вміє так — фіксований префікс.
        // Тому пробуємо діапазон.
        for (var port = 54321; port < 54400; port++)
        {
            var prefix = $"http://127.0.0.1:{port}/openid/";
            var l = new HttpListener();
            l.Prefixes.Add(prefix);
            try
            {
                l.Start();
                return (l, prefix);
            }
            catch (HttpListenerException)
            {
                l.Close();
            }
        }
        throw new InvalidOperationException("Не вдалось зайняти жодний з портів 54321..54399");
    }

    private static string BuildAuthUrl(string returnTo)
    {
        var realm = new Uri(returnTo).GetLeftPart(UriPartial.Authority) + "/";
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["openid.ns"] = "http://specs.openid.net/auth/2.0";
        qs["openid.mode"] = "checkid_setup";
        qs["openid.return_to"] = returnTo;
        qs["openid.realm"] = realm;
        qs["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select";
        qs["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select";
        return $"{SteamOpenIdEndpoint}?{qs}";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // На Windows зазвичай через cmd /c start
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerContext ctx, string query)
    {
        var ok = !string.IsNullOrEmpty(query) && query.Contains("openid.claimed_id", StringComparison.Ordinal);
        var html = ok
            ? "<html><body style='font-family:sans-serif;background:#1a1a1f;color:#eee;text-align:center;padding-top:80px'>"
              + "<h2>D2Helper</h2><p>Sign-in successful. Ви можете закрити цю вкладку.</p></body></html>"
            : "<html><body style='font-family:sans-serif;background:#1a1a1f;color:#eee;text-align:center;padding-top:80px'>"
              + "<h2>D2Helper</h2><p style='color:#f88'>Login cancelled or invalid response.</p></body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.StatusCode = 200;
        try
        {
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    private async Task<bool> VerifyAsync(System.Collections.Specialized.NameValueCollection parsed, CancellationToken ct)
    {
        var verify = HttpUtility.ParseQueryString(string.Empty);
        foreach (string? key in parsed.AllKeys)
        {
            if (key is null) continue;
            verify[key] = parsed[key];
        }
        verify["openid.mode"] = "check_authentication";

        using var content = new StringContent(verify.ToString() ?? string.Empty,
            Encoding.UTF8, "application/x-www-form-urlencoded");
        using var resp = await _http.PostAsync(SteamOpenIdEndpoint, content, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return false;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return body.Contains("is_valid:true", StringComparison.OrdinalIgnoreCase);
    }
}
