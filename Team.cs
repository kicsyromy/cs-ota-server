using System.Net;

namespace ScOtaServer;

internal class Team
{
    enum Query
    {
        GetPutModel,
        ListModels,
        ResetModels,
        UploadResults,
        ViewResults,
        ResetResults,
        Invalid
    }

    const string MODEL_PATH = "/var/sc-ota/models";
    const string RESULTS_PATH = "/var/sc-ota/results";

    readonly string _username;
    readonly string _password;

    public Team(string username, string password)
    {
        _username = username;
        _password = password;
    }

    internal async Task HandleRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // Check if the user is authorized
        if (!CheckAuthorization(context))
        {
            throw new HttpListenerException(401, "Missing or invalid 'Authorization' header");
        }

        var (queryType, parameter) = ParseQuery(request.Url?.Query);
        switch (queryType)
        {
            case Query.GetPutModel:
                await HandleGetPutModelQuery(context, parameter);
                break;

            case Query.ListModels:
                await HandleListModelsQuery(context);
                break;

            case Query.ResetModels:
                await HandleResetModelsQuery(context);
                break;

            case Query.UploadResults:
                await HandleUploadResultsQuery(context);
                break;

            case Query.ViewResults:
                await HandleViewResultsQuery(context);
                break;

            case Query.ResetResults:
                await HandleResetResultsQuery(context);
                break;

            case Query.Invalid:
                throw new HttpListenerException(400, $"Invalid query: '{request.Url?.Query}'." +
                                                     "The following queries are allowed: 'modelName', 'listModels', 'resetModels', " +
                                                     "'uploadResults', 'viewResults', 'resetResults'.");
        }

        response.Close();
    }

    bool CheckAuthorization(HttpListenerContext context)
    {
        var identity = (HttpListenerBasicIdentity?)context.User!.Identity;

        // Check if the username and password are correct
        if (identity != null && identity.Name == _username && identity.Password == _password)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    (Query, string?) ParseQuery(string? queryString)
    {
        var query = queryString?.Trim('?').Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (query == null || query.Length > 1)
        {
            throw new HttpListenerException(400, "Only one query string parameter is allowed");
        }

        var modelQuery = query.FirstOrDefault(q => q.StartsWith("modelName", StringComparison.InvariantCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(modelQuery))
        {
            return (Query.GetPutModel, modelQuery.Substring(9).TrimStart('='));
        }

        var listModelsQuery = query.FirstOrDefault(q => string.Compare(q, "listModels", StringComparison.InvariantCultureIgnoreCase) == 0);
        if (!string.IsNullOrWhiteSpace(listModelsQuery))
        {
            return (Query.ListModels, null);
        }

        var resetModelsQuery = query.FirstOrDefault(q => string.Compare(q, "resetModels", StringComparison.InvariantCultureIgnoreCase) == 0);
        if (!string.IsNullOrWhiteSpace(resetModelsQuery))
        {
            return (Query.ResetModels, null);
        }

        var uploadResultsQuery = query.FirstOrDefault(q => string.Compare(q, "uploadResults", StringComparison.InvariantCultureIgnoreCase) == 0);
        if (!string.IsNullOrWhiteSpace(uploadResultsQuery))
        {
            return (Query.UploadResults, null);
        }

        var viewResultsQuery = query.FirstOrDefault(q => string.Compare(q, "viewResults", StringComparison.InvariantCultureIgnoreCase) == 0);
        if (!string.IsNullOrWhiteSpace(viewResultsQuery))
        {
            return (Query.ViewResults, null);
        }

        var resetResultsQuery = query.FirstOrDefault(q => string.Compare(q, "resetResults", StringComparison.InvariantCultureIgnoreCase) == 0);
        if (!string.IsNullOrWhiteSpace(resetResultsQuery))
        {
            return (Query.ResetResults, null);
        }

        return (Query.Invalid, null);
    }

    async Task HandleGetPutModelQuery(HttpListenerContext context, string? parameter)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (string.IsNullOrWhiteSpace(parameter))
        {
            throw new HttpListenerException(400, "Missing 'modelName' query parameter");
        }

        if (request.HttpMethod == "PUT")
        {
            await UploadModel(request.InputStream, parameter).ConfigureAwait(false);

            response.ContentType = "application/json";
            response.OutputStream.Write("{ \"success\": true }"u8);
        }
        else if (request.HttpMethod == "GET")
        {
            response.ContentType = "application/octet-stream";

            await DownloadModel(response.OutputStream, parameter).ConfigureAwait(false);
            await response.OutputStream.FlushAsync().ConfigureAwait(false);
        }
        else
        {
            throw new HttpListenerException(405, "Only 'PUT' and 'GET' requests are allowed");
        }
    }

    async Task HandleListModelsQuery(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.HttpMethod != "GET")
        {
            throw new HttpListenerException(400, "Only GET requests are allowed when 'list'-ing models.");
        }

        response.ContentType = "application/json";

        var models = Directory.GetFiles(Path.Combine(MODEL_PATH, _username)).Select(Path.GetFileName).ToArray();
        await response.OutputStream.WriteAsync(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(models))
            .ConfigureAwait(false);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    async Task HandleResetModelsQuery(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.HttpMethod != "DELETE")
        {
            throw new HttpListenerException(400, "Only DELETE requests are allowed for the 'resetModels' query.");
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(MODEL_PATH, _username)))
        {
            File.Delete(file);
        }

        response.ContentType = "application/json";
        response.OutputStream.Write("{ \"success\": true }"u8);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    async Task HandleUploadResultsQuery(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.HttpMethod != "POST")
        {
            throw new HttpListenerException(400, "Only POST requests are allowed for the 'uploadResults' query.");
        }

        if (string.IsNullOrWhiteSpace(request.ContentType) || request.ContentType != "application/json")
        {
            throw new HttpListenerException(400, "Only 'application/json' content type is allowed for the 'uploadResults' query.");
        }

        // Create the directory if it doesn't already exist
        Directory.CreateDirectory(Path.Combine(RESULTS_PATH, _username));

        await using var fileStream = File.Create(Path.Combine(RESULTS_PATH, _username, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json"));

        await CopyStreamLimited(request.InputStream, fileStream, 1024 * 1024 * 100).ConfigureAwait(false);
        await fileStream.FlushAsync().ConfigureAwait(false);

        response.ContentType = "application/json";
        response.OutputStream.Write("{ \"success\": true }"u8);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    async Task HandleViewResultsQuery(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.HttpMethod != "GET")
        {
            throw new HttpListenerException(400, "Only GET requests are allowed for the 'viewResults' query.");
        }

        response.ContentType = "application/json";
        response.OutputStream.Write("[\n"u8);

        var first = true;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RESULTS_PATH, _username)))
        {
            if (!first)
            {
                response.OutputStream.Write(",\n"u8);
            }
            first = false;

            await using var fileStream = File.OpenRead(file);
            await fileStream.CopyToAsync(response.OutputStream).ConfigureAwait(false);
        }

        response.OutputStream.Write("\n]"u8);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    async Task HandleResetResultsQuery(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.HttpMethod != "DELETE")
        {
            throw new HttpListenerException(400, "Only DELETE requests are allowed for the 'resetResults' query.");
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RESULTS_PATH, _username)))
        {
            File.Delete(file);
        }

        response.ContentType = "application/json";
        response.OutputStream.Write("{ \"success\": true }"u8);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    static async Task CopyStreamLimited(Stream source, Stream destination, long maxBytes)
    {
        byte[] buffer = new byte[4096];
        long totalBytesRead = 0;

        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0 && totalBytesRead < maxBytes)
        {
            var bytesLeftToWrite = maxBytes - totalBytesRead;
            int bytesToWrite = (int)Math.Min(bytesRead, bytesLeftToWrite < 0 ? 0 : bytesLeftToWrite);

            await destination.WriteAsync(buffer, 0, bytesToWrite);

            totalBytesRead += bytesToWrite;
        }

        if (await source.ReadAsync(buffer, 0, 1) > 0 && totalBytesRead >= maxBytes)
        {
            throw new HttpListenerException(413, "File exceeds maximum size of 1 GiB");
        }
    }

    async Task UploadModel(Stream requestStream, string fileName)
    {
        // Create the directory if it doesn't already exist
        Directory.CreateDirectory(Path.Combine(MODEL_PATH, _username));

        await using var fileStream = File.Create(Path.Combine(MODEL_PATH, _username, fileName));

        await CopyStreamLimited(requestStream, fileStream, 1024 * 1024 * 1024).ConfigureAwait(false);
        await fileStream.FlushAsync().ConfigureAwait(false);
    }

    async Task DownloadModel(Stream responseStream, string fileName)
    {
        try
        {
            await using var fileStream = File.OpenRead(Path.Combine(MODEL_PATH, _username, fileName));
            await fileStream.CopyToAsync(responseStream).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new HttpListenerException(404, $"Model '{fileName}' could not be found");
        }
    }
}