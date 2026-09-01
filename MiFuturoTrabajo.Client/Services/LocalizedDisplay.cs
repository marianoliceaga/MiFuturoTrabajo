using System.Globalization;
using Microsoft.Extensions.Localization;
using MiFuturoTrabajo.Client.Resources;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Client.Services
{
    /// <summary>
    /// Turns enum values into text a person reads.
    ///
    /// The enums themselves stay in English and keep their numeric values -
    /// they are persisted in Cosmos and travel over the wire, so renaming a
    /// member would be a data migration, not a translation. Only the label
    /// changes per language.
    /// </summary>
    public static class EnumDisplay
    {
        /// <summary>
        /// Looks the value up as <c>{EnumTypeName}_{MemberName}</c>, so adding a
        /// member to an enum and forgetting its resource entry shows up as the
        /// key rendered on screen rather than as a silent English label.
        /// </summary>
        public static string Enum<TEnum>(this IStringLocalizer<Strings> localizer, TEnum value)
            where TEnum : struct, System.Enum =>
            localizer[$"{typeof(TEnum).Name}_{value}"];

        public static string Enum<TEnum>(this IStringLocalizer<Strings> localizer, TEnum? value)
            where TEnum : struct, System.Enum =>
            value.HasValue ? localizer.Enum(value.Value) : string.Empty;

        /// <summary>
        /// Picks between a singular and a plural resource by count, looking up
        /// <c>{key}_One</c> or <c>{key}_Many</c> and passing the count in.
        ///
        /// Without this the app rendered "Vence en 1 días" and "1 steps".
        /// Spanish and English both need exactly two forms and both use the
        /// plural for zero, so a full CLDR plural-rules dependency would be
        /// weight for no benefit - but neither language tolerates the mismatch.
        /// A language with different rules would need more than this, which is
        /// the point at which it stops being a two-line helper.
        /// </summary>
        public static string Plural(this IStringLocalizer<Strings> localizer, string key, int count) =>
            localizer[count == 1 ? $"{key}_One" : $"{key}_Many", count];

        /// <summary>
        /// The Kanban columns, which deliberately do not read like the enum
        /// members behind them: the board calls <c>NotApplied</c> "Applying"
        /// and <c>Completed</c> "Not selected". That wording predates this
        /// change and is preserved rather than corrected - renaming columns is
        /// a product decision, not a translation one.
        /// </summary>
        public static string BoardColumn(this IStringLocalizer<Strings> localizer, JobApplicationStatus status) =>
            localizer[$"BoardColumn_{status}"];
    }

    /// <summary>
    /// Date and time formatting.
    ///
    /// Every method here uses a *standard* format specifier rather than a
    /// custom pattern, which is the whole point: "d" asks the current culture
    /// for its short date, where "MMM dd, yyyy" hard-codes American ordering no
    /// matter what culture is active. The same applies to time - the app used
    /// "hh:mm tt", so Spanish users were shown 12-hour clock times with an
    /// English AM/PM suffix. "t" gives 24-hour time in Spanish and 12-hour in
    /// English, because that is what each culture actually declares.
    /// </summary>
    public static class DateDisplay
    {
        /// <summary>Short date: 01/09/2026 (es), 9/1/2026 (en).</summary>
        public static string ShortDate(this DateTime value) =>
            value.ToString("d", CultureInfo.CurrentCulture);

        /// <summary>Long date: "martes, 1 de septiembre de 2026".</summary>
        public static string LongDate(this DateTime value) =>
            value.ToString("D", CultureInfo.CurrentCulture);

        /// <summary>Short time: "14:30" (es), "2:30 PM" (en).</summary>
        public static string ShortTime(this DateTime value) =>
            value.ToString("t", CultureInfo.CurrentCulture);

        /// <summary>Short date and time together.</summary>
        public static string DateAndTime(this DateTime value) =>
            value.ToString("g", CultureInfo.CurrentCulture);

        /// <summary>Month and year, without a day: "septiembre de 2026".</summary>
        public static string MonthAndYear(this DateTime value) =>
            value.ToString("y", CultureInfo.CurrentCulture);

        /// <summary>Abbreviated month on its own, for date chips: "sept", "Sep".</summary>
        public static string AbbreviatedMonth(this DateTime value) =>
            value.ToString("MMM", CultureInfo.CurrentCulture);
    }
}
