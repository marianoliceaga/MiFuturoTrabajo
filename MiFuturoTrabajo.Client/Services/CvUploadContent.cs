using System.Net.Http.Headers;

namespace MiFuturoTrabajo.Client.Services
{
    /// <summary>
    /// Builds the multipart body for a CV upload, and turns a rejection into an
    /// exception that carries the server's reason.
    ///
    /// The reason matters: the server validates the file by its bytes, so a
    /// rejection says something specific and actionable ("that is not really a
    /// PDF", "the file is 8.2 MB, the limit is 5 MB"). EnsureSuccessStatusCode
    /// would throw all of that away and leave the user with "400 Bad Request".
    /// </summary>
    internal static class CvUploadContent
    {
        public static MultipartFormDataContent Build(
            string name, string fileName, string contentType, Stream fileStream)
        {
            var content = new MultipartFormDataContent();

            var streamContent = new StreamContent(fileStream);

            // The server re-derives the content type from the file's leading
            // bytes and does not trust this. It is sent so the request is
            // well-formed, not as a claim the server acts on.
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            content.Add(streamContent, "file", fileName);

            if (!string.IsNullOrWhiteSpace(name))
            {
                content.Add(new StringContent(name), "name");
            }

            return content;
        }

        public static async Task ThrowIfRejectedAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            var reason = await response.Content.ReadAsStringAsync();

            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(reason) ? $"Upload failed ({(int)response.StatusCode})." : reason);
        }
    }
}
