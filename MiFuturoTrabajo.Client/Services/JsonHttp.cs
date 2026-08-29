using System.Net.Http.Json;

namespace MiFuturoTrabajo.Client.Services
{
    /// <summary>
    /// Deserialisation helpers that keep a broken response from becoming a
    /// mystery later.
    ///
    /// <see cref="HttpClientJsonExtensions.GetFromJsonAsync{T}(HttpClient, string, CancellationToken)"/>
    /// returns <c>T?</c>, but every service method here promises <c>T</c>. The
    /// previous code simply returned the nullable value, so a response that
    /// deserialised to null travelled into a Razor page and surfaced as a
    /// <see cref="NullReferenceException"/> somewhere unrelated - with nothing
    /// in the message naming the endpoint that actually misbehaved.
    ///
    /// These endpoints always return the object they claim to, so an empty body
    /// is a broken contract rather than an expected outcome. Failing at the
    /// boundary, naming the request, is the useful behaviour.
    /// </summary>
    internal static class JsonHttp
    {
        public static async Task<T> GetRequiredAsync<T>(this HttpClient http, string requestUri) =>
            await http.GetFromJsonAsync<T>(requestUri) ?? throw EmptyBody(requestUri);

        /// <summary>
        /// A collection endpoint returning nothing is different: "no rows" is a
        /// real answer, so it becomes an empty list rather than an exception.
        /// </summary>
        public static async Task<List<T>> GetListAsync<T>(this HttpClient http, string requestUri) =>
            await http.GetFromJsonAsync<List<T>>(requestUri) ?? new List<T>();

        public static async Task<T> ReadRequiredAsync<T>(this HttpContent content, string requestUri) =>
            await content.ReadFromJsonAsync<T>() ?? throw EmptyBody(requestUri);

        private static InvalidOperationException EmptyBody(string requestUri) =>
            new($"The server returned an empty body for '{requestUri}', which this call requires.");
    }
}
