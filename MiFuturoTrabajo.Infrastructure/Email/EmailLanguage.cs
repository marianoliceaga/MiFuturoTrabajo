namespace MiFuturoTrabajo.Infrastructure.Email
{
    /// <summary>
    /// The languages transactional email is written in.
    ///
    /// Kept as strings rather than an enum because the value round-trips
    /// through a Cosmos document, an HTTP header and a request body, and a
    /// two-letter code survives all three without a converter. Everything that
    /// is not recognised becomes <see cref="Default"/>, so a stale or hand-
    /// edited document can never make an email fail to render.
    /// </summary>
    public static class EmailLanguage
    {
        public const string Spanish = "es";
        public const string English = "en";

        /// <summary>Matches the language the application itself starts in.</summary>
        public const string Default = Spanish;

        /// <summary>
        /// Accepts a bare code ("es"), a specific culture ("es-AR"), or the
        /// contents of an Accept-Language header ("en-GB,en;q=0.9,es;q=0.8"),
        /// and returns one of the two supported codes.
        /// </summary>
        public static string Resolve(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return Default;
            }

            foreach (var entry in requested.Split(','))
            {
                // Drop any quality weight, then keep the primary subtag.
                var tag = entry.Split(';')[0].Trim();
                var primary = tag.Split('-')[0];

                if (primary.Equals(English, StringComparison.OrdinalIgnoreCase))
                {
                    return English;
                }

                if (primary.Equals(Spanish, StringComparison.OrdinalIgnoreCase))
                {
                    return Spanish;
                }
            }

            return Default;
        }
    }
}
