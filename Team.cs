using System.Net;

namespace ScOtaServer;

internal class Team
{
    const string MODEL_PATH = "/var/sc-ota/models";

    string _username;
    string _password;

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

        // Get the file name from the query string
        var query = request.Url?.Query.Trim('?').Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var fileName = query?.FirstOrDefault(q => q.StartsWith("fileName=", StringComparison.InvariantCultureIgnoreCase))?.Substring(9);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new HttpListenerException(400, "Missing or empty 'fileName' parameter");
        }

        if (request.HttpMethod == "PUT")
        {
            await UploadModel(request.InputStream, fileName).ConfigureAwait(false);

            response.ContentType = "application/json";
            response.OutputStream.Write("{ \"success\": true }"u8);
        }
        else if (request.HttpMethod == "GET")
        {
            response.ContentType = "application/octet-stream";

            await DownloadModel(response.OutputStream, fileName).ConfigureAwait(false);
            await response.OutputStream.FlushAsync().ConfigureAwait(false);
        }
        else
        {
            throw new HttpListenerException(405, "Only 'PUT' and 'GET' requests are allowed");
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
        Directory.CreateDirectory(MODEL_PATH);

        await using var fileStream = File.Create(Path.Combine(MODEL_PATH, fileName));

        await CopyStreamLimited(requestStream, fileStream, 1024 * 1024 * 1024).ConfigureAwait(false);
        await fileStream.FlushAsync().ConfigureAwait(false);
    }

    async Task DownloadModel(Stream responseStream, string fileName)
    {
        try
        {
            await using var fileStream = File.OpenRead(Path.Combine(MODEL_PATH, fileName));
            await fileStream.CopyToAsync(responseStream).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new HttpListenerException(404, $"Model '{fileName}' could not be found");
        }
    }
}