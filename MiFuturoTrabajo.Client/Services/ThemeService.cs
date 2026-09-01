using Microsoft.JSInterop;

namespace MiFuturoTrabajo.Client.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public interface IThemeService
    {
        AppTheme Current { get; }

        /// <summary>Raised after <see cref="Current"/> changes, so the toggle can redraw.</summary>
        event Action? Changed;

        /// <summary>Reads the stored or detected theme. Call once, on first render.</summary>
        Task InitialiseAsync();

        Task SetThemeAsync(AppTheme theme);

        Task ToggleAsync();
    }

    /// <summary>
    /// Theme selection, backed by localStorage through <c>js/preferences.js</c>.
    ///
    /// Unlike the language, changing the theme needs no reload: the palette
    /// lives in CSS custom properties keyed off <c>data-theme</c> on the root
    /// element, so flipping that attribute restyles the page in place.
    ///
    /// This service does not decide the initial theme - preferences.js already
    /// did, before the first paint. It reads back what was decided so the
    /// toggle shows the right state.
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly IJSRuntime _js;

        public ThemeService(IJSRuntime js)
        {
            _js = js;
        }

        public AppTheme Current { get; private set; } = AppTheme.Light;

        public event Action? Changed;

        public async Task InitialiseAsync()
        {
            try
            {
                var stored = await _js.InvokeAsync<string>("mftPrefs.getTheme");
                Current = Parse(stored);
            }
            catch (JSException)
            {
                Current = AppTheme.Light;
            }

            Changed?.Invoke();
        }

        public async Task SetThemeAsync(AppTheme theme)
        {
            if (theme == Current)
            {
                return;
            }

            await _js.InvokeAsync<string>("mftPrefs.setTheme", Name(theme));

            Current = theme;
            Changed?.Invoke();
        }

        public Task ToggleAsync() =>
            SetThemeAsync(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

        private static string Name(AppTheme theme) => theme == AppTheme.Dark ? "dark" : "light";

        private static AppTheme Parse(string? value) =>
            string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
    }
}
