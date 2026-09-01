namespace MiFuturoTrabajo.Client.Resources
{
    /// <summary>
    /// Marker type for <c>IStringLocalizer&lt;Strings&gt;</c>.
    ///
    /// One shared resource file for the whole client rather than one per
    /// component: fourteen pages share most of their vocabulary ("Save",
    /// "Cancel", "Company"), and splitting it up would mean translating the
    /// same word a dozen times and letting the translations drift.
    ///
    /// The localizer resolves resources by this type's full name, so
    /// <c>Resources/Strings.resx</c> and <c>Resources/Strings.es.resx</c> sit
    /// beside this file and need no <c>ResourcesPath</c> configuration.
    ///
    /// The neutral file holds English. English is the fallback rather than the
    /// default the user sees: Spanish is what the app starts in, and any key
    /// missing from the Spanish file degrades to readable English instead of
    /// rendering the key name.
    /// </summary>
    public sealed class Strings
    {
    }
}
