using System.Globalization;
using System.Text;
using System.Net;

namespace ScOtaServer;

class Program
{
    static readonly Team Team1 = new Team("team1", "PQWK7WdSdmrej6TC3xaf");
    static readonly Team Team2 = new Team("team2", "BvZGcmXW39RnL5MtpzvM");
    static readonly Team Team3 = new Team("team3", "fTfknjjeJqRD5myKTnK9");
    static readonly Team Team4 = new Team("team4", "MUnFEnRqzH4fBWRT8YXJ");

    static void ProcessRequest(HttpListenerContext context, TokenBucket tokenBucket)
    {
        new Task(async () =>
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            if (!tokenBucket.TryConsumeToken(1))
            {
                context.Response.StatusCode = 429;
                context.Response.StatusDescription = "Rate limit exceeded";
                await context.Response.OutputStream.WriteAsync("{ \"error\": \"Rate limit exceeded\" }"u8.ToArray()).ConfigureAwait(false);
                await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
                context.Response.Close();

                return;
            }

            // Get the Authorization header
            string? authHeader = request.Headers["Authorization"];
            // If the header is null or empty, send a 401
            if (string.IsNullOrEmpty(authHeader))
            {
                throw new HttpListenerException(401, "Missing or invalid 'Authorization' header");
            }

            try
            {
                var path = request.Url?.AbsolutePath.Trim('/');
                if (!string.IsNullOrWhiteSpace(path) &&
                    string.Compare(path, "team1", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
                {
                    await Team1.HandleRequest(context);
                }
                else if (!string.IsNullOrWhiteSpace(path) &&
                         string.Compare(path, "team2", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
                {
                    await Team2.HandleRequest(context);
                }
                else if (!string.IsNullOrWhiteSpace(path) &&
                         string.Compare(path, "team3", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
                {
                    await Team3.HandleRequest(context);
                }
                else if (!string.IsNullOrWhiteSpace(path) &&
                         string.Compare(path, "team4", CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0)
                {
                    await Team4.HandleRequest(context);
                }
                else
                {
                    response.StatusCode = 400;
                    response.StatusDescription = "Bad Request";
                    response.ContentType = "application/json";
                    response.ContentEncoding = Encoding.UTF8;

                    using var writer = new StreamWriter(response.OutputStream);
                    writer.WriteLine("{ \"error\": \"Invalid team name\" }");
                    writer.Flush();

                    response.Close();
                }
            }
            catch (HttpListenerException ex)
            {
                response.StatusCode = ex.ErrorCode;
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;

                await using var writer = new StreamWriter(response.OutputStream);
                await writer.WriteLineAsync($"{{ \"error\": \"{ex.Message}\" }}").ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);

                response.Close();
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.StatusDescription = "Internal Server Error";
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;

                await using var writer = new StreamWriter(response.OutputStream);
                await writer.WriteLineAsync($"{{ \"error\": \"{ex.Message}\" }}").ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);

                response.Close();
            }
        }).Start();
    }

    static void Main()
    {
        HttpListener listener = new HttpListener();
        TokenBucket tokenBucket = new TokenBucket(100, 1000);

        listener.Prefixes.Add("http://*:8080/");
        listener.AuthenticationSchemes = AuthenticationSchemes.Basic;

        listener.Start();
        while (listener.IsListening)
        {
            HttpListenerContext context = listener.GetContext();
            ProcessRequest(context, tokenBucket);
        }
    }
}