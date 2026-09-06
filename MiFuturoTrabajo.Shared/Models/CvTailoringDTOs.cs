namespace MiFuturoTrabajo.Shared.Models
{
    public enum CvTailoringMode
    {
        /// <summary>Concrete edits to make to the CV for this posting.</summary>
        Suggestions = 1,

        /// <summary>A cover letter drafted from the CV and the posting.</summary>
        CoverLetter = 2
    }

    public class CvTailoringRequestDto
    {
        public string CvVersionId { get; set; } = string.Empty;

        public string JobDescription { get; set; } = string.Empty;

        /// <summary>Optional - the posting usually states it, but a pasted excerpt may not.</summary>
        public string JobTitle { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public CvTailoringMode Mode { get; set; } = CvTailoringMode.Suggestions;

        /// <summary>
        /// Two-letter code the answer should be written in. The CV and the
        /// posting are frequently in different languages, so neither one can be
        /// used to infer it - the user's own UI language is the only reliable
        /// signal for which language they want to read.
        /// </summary>
        public string Language { get; set; } = "es";
    }

    public class CvStrengthDto
    {
        /// <summary>What the CV already does well against this posting.</summary>
        public string Point { get; set; } = string.Empty;

        /// <summary>The line in the CV that backs it up.</summary>
        public string Evidence { get; set; } = string.Empty;
    }

    public class CvGapDto
    {
        public string Requirement { get; set; } = string.Empty;

        /// <summary>"must-have" or "nice-to-have", constrained by the response schema.</summary>
        public string Severity { get; set; } = string.Empty;

        public string Advice { get; set; } = string.Empty;
    }

    public class CvSuggestionDto
    {
        /// <summary>Which part of the CV to change - "Summary", "Experience", ...</summary>
        public string Section { get; set; } = string.Empty;

        /// <summary>The wording as it stands today.</summary>
        public string Current { get; set; } = string.Empty;

        /// <summary>The wording to replace it with.</summary>
        public string Proposed { get; set; } = string.Empty;

        public string Why { get; set; } = string.Empty;
    }

    /// <summary>How much of the allowance is left, returned with every answer
    /// so the UI never has to guess or ask again.</summary>
    public class AiUsageDto
    {
        public int Used { get; set; }
        public int Limit { get; set; }
        public int Remaining { get; set; }
        public DateTime RenewsAtUtc { get; set; }
    }

    public class CvTailoringResultDto
    {
        public CvTailoringMode Mode { get; set; }

        public string Summary { get; set; } = string.Empty;

        public List<CvStrengthDto> Strengths { get; set; } = new();
        public List<CvGapDto> Gaps { get; set; } = new();
        public List<CvSuggestionDto> Suggestions { get; set; } = new();

        /// <summary>Terms the posting asks for that the CV never uses.</summary>
        public List<string> MissingKeywords { get; set; } = new();

        /// <summary>Populated for <see cref="CvTailoringMode.CoverLetter"/> only.</summary>
        public string CoverLetter { get; set; } = string.Empty;

        public AiUsageDto Usage { get; set; } = new();
    }

    /// <summary>
    /// Whether the feature can be used at all, and how much of the allowance
    /// remains. The page asks for this on load so it can disable itself with a
    /// reason instead of failing at the moment the user presses the button.
    /// </summary>
    public class CvTailoringStatusDto
    {
        /// <summary>False when the deployment has no API key configured.</summary>
        public bool Enabled { get; set; }

        public AiUsageDto Usage { get; set; } = new();

        public int MaxJobDescriptionChars { get; set; }
    }
}
