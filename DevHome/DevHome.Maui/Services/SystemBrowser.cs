using System.Diagnostics;
using System.Net;
using System.Text;
using OidcBrowser = IdentityModel.OidcClient.Browser;

namespace DevHome.Maui.Services;

/// <summary>
/// Открывает системный браузер и принимает код авторизации на loopback-адресе.
/// </summary>
public sealed class SystemBrowser : OidcBrowser.IBrowser
{
    public async Task<OidcBrowser.BrowserResult> InvokeAsync(
        OidcBrowser.BrowserOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endUrl = options.EndUrl.TrimEnd('/') + "/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(endUrl);
        listener.Start();

        try
        {
            if (!string.IsNullOrEmpty(options.StartUrl))
            {
                Process.Start(new ProcessStartInfo(options.StartUrl) { UseShellExecute = true });
            }

            using var registration = cancellationToken.Register(listener.Stop);
            var context = await listener.GetContextAsync();
            var responseText = Encoding.UTF8.GetBytes(
                "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>DevHome</title></head>" +
                "<body style=\"font-family: system-ui; text-align: center; margin-top: 4rem;\">" +
                "<h1>Вход выполнен</h1><p>Можно закрыть это окно и вернуться в приложение.</p></body></html>");

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseText.Length;
            await context.Response.OutputStream.WriteAsync(responseText, cancellationToken);
            context.Response.Close();

            return new OidcBrowser.BrowserResult
            {
                ResultType = OidcBrowser.BrowserResultType.Success,
                Response = context.Request.Url?.ToString() ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            return new OidcBrowser.BrowserResult { ResultType = OidcBrowser.BrowserResultType.UserCancel };
        }
        catch (HttpListenerException)
        {
            return new OidcBrowser.BrowserResult
            {
                ResultType = OidcBrowser.BrowserResultType.UnknownError,
                Error = $"Не удалось открыть адрес возврата {endUrl}."
            };
        }
    }
}
