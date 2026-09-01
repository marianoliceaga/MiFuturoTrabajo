using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MiFuturoTrabajo.Client.Services
{
    /// <param name="Code">Two-letter culture name, also the .resx suffix.</param>
    /// <param name="NativeName">How the language names itself. A language picker
    /// that says "Spanish" to someone who only reads Spanish is a picker they
    /// cannot use, so both options are always shown in their own language.</param>
    public record CultureOption(string Code, string NativeName);

    public interface ICultureService
    {
        IReadOnlyList<CultureOption> Supported { get; }

        /// <summary>The culture the app is currently rendering in.</summary>
        string CurrentCode { get; }

        /// <summary>
        /// Persists the choice and reloads. Reloading is not laziness: the
        /// resource strings for a rendered tree were resolved when it was
        /// built, and Blazor has no mechanism to re-resolve them in place.
        /// </summary>
        Task SetCultureAsync(string code);
    }

    /// <summary>
    /// Language selection, backed by localStorage through <c>js/preferences.js</c>.
    ///
    /// Neutral cultures ("es", "en") rather than specific ones ("es-AR",
    /// "en-US"): the audience spans several Spanish-speaking countries, so a
    /// single Spanish translation serves all of them and one .resx per language
    /// stays maintainable. It also keeps the app inside the ICU data that .NET
    /// ships by default, which matters for a WebAssembly download.
    /// </summary>
    public class CultureService : ICultureService
    {
        /// <summary>The language the app falls back to, and starts in when the
        /// browser asks for neither Spanish nor English.</summary>
        public const string DefaultCode = "es";

        private readonly IJSRuntime _js;
        private readonly NavigationManager _navigation;
        private readonly IUserPreferenceService _preferences;

        public CultureService(
            IJSRuntime js, NavigationManager navigation, IUserPreferenceService preferences)
        {
            _js = js;
            _navigation = navigation;
            _preferences = preferences;
        }

        public IReadOnlyList<CultureOption> Supported { get; } = new[]
        {
            new CultureOption("es", "Espa\u00f1ol"),
            new CultureOption("en", "English")
        };

        public string CurrentCode => Normalise(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        public async Task SetCultureAsync(string code)
        {
            var normalised = Normalise(code);

            if (normalised == CurrentCode)
            {
                return;
            }

            await _js.InvokeAsync<string>("mftPrefs.setCulture", normalised);

            // Told to the server as well, because the reminder emails are sent
            // by a timer trigger that has no browser to ask. A failure here
            // must not block the language change the user just made.
            await _preferences.TrySaveLanguageAsync(normalised);

            _navigation.NavigateTo(_navigation.Uri, forceLoad: true);
        }

        /// <summary>Anything unrecognised becomes the default rather than throwing.</summary>
        private static string Normalise(string? code) =>
            string.IsNullOrWhiteSpace(code) ? DefaultCode
            : code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en"
            : DefaultCode;
    }
}
