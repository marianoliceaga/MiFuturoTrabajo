using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Configurable under <c>Ai:</c>. Every field here exists to bound spend:
    /// this is the only feature in the app whose per-request cost is real money
    /// rather than a fraction of a request unit.
    /// </summary>
    public class CvTailoringOptions
    {
        public string? ApiKey { get; set; }

        /// <summary>
        /// Model id. Configurable rather than compiled in, because it is the
        /// single biggest lever on what this feature costs and the right answer
        /// depends on how the deployment is being used, not on the code.
        ///
        /// A flash-lite tier is the default because this is a demo running on
        /// free tiers: the allowance is five calls, and a default that quietly
        /// costs several times more per call is the wrong one to ship unasked.
        /// </summary>
        public string Model { get; set; } = "gemini-3.5-flash-lite";

        /// <summary>
        /// The Interactions API is revision-pinned by header. Pinning it means a
        /// server-side revision cannot silently reshape the request under a
        /// deployment nobody is watching; the cost is that this string has to be
        /// moved forward deliberately, which is the point.
        /// </summary>
        public string ApiRevision { get; set; } = "2026-05-20";

        /// <summary>
        /// Overridable so the wire format can be checked against a local
        /// listener, without a key and without spending anything.
        /// </summary>
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

        /// <summary>
        /// Optional: "minimal", "low", "medium", "high". Left unset so the API's
        /// own default applies - naming a level here would break a deployment
        /// that configured a model which does not accept one.
        /// </summary>
        public string? ThinkingLevel { get; set; }

        public int MaxTokens { get; set; } = 8_000;

        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>Calls one account may make per <see cref="PeriodDays"/>.</summary>
        public int CallsPerPeriod { get; set; } = 5;

        public int PeriodDays { get; set; } = 30;

        /// <summary>
        /// Roughly three pages of posting. Long enough for any real advert,
        /// short enough that pasting an entire careers site does not quietly
        /// cost ten times what a normal request does.
        /// </summary>
        public int MaxJobDescriptionChars { get; set; } = 8_000;

        /// <summary>
        /// Matches the upload cap, so any CV that could be stored can be read.
        /// The provider would accept a great deal more inline; this ceiling is
        /// ours, not theirs.
        /// </summary>
        public long MaxCvBytes { get; set; } = 5 * 1024 * 1024;

        public TimeSpan Period => TimeSpan.FromDays(PeriodDays);
    }

    /// <param name="Cv">The CV, already read out of storage.</param>
    /// <param name="Request">What the user asked for.</param>
    public record CvTailoringInput(CvContent Cv, CvTailoringRequestDto Request);

    public interface ICvTailoringService
    {
        /// <summary>False when no API key is configured; the feature then
        /// reports itself unavailable instead of failing at the button.</summary>
        bool IsEnabled { get; }

        CvTailoringOptions Options { get; }

        /// <summary>
        /// On failure returns a message fit to show a user. The exception is
        /// logged rather than returned: an upstream provider's error text is
        /// not something to render into somebody's browser.
        /// </summary>
        Task<(CvTailoringResultDto? Result, string? Error)> TailorAsync(
            CvTailoringInput input, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// CV tailoring against a job posting, backed by the Gemini Interactions API.
    ///
    /// Three design points worth stating, because all three were choices:
    ///
    /// The response is a constrained JSON schema rather than prose. Prose would
    /// be less code, but rendering it means either dumping a wall of markdown
    /// into the page or parsing it back out with string matching - and a model
    /// that one day writes its headings differently silently breaks the second.
    /// A schema makes the shape a contract the API enforces.
    ///
    /// The prompt forbids inventing experience, and that is a correctness
    /// requirement, not politeness. A tool that helpfully adds the skills a
    /// posting asks for produces a CV its owner cannot defend in an interview -
    /// it does real harm to the person it is supposed to help. Everything it
    /// proposes has to be a rewording of something already in the document.
    ///
    /// The provider is reached over plain HTTP rather than through a wrapper.
    /// Google publishes no first-party .NET SDK for this API, and the request is
    /// four fields; taking a third-party package to avoid writing them would
    /// trade code this repository can read and test for a dependency it cannot.
    /// </summary>
    public class CvTailoringService : ICvTailoringService
    {
        /// <summary>The API speaks snake_case; the records below are named in C#.</summary>
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>The model's own answer is camelCase, per the schemas below.</summary>
        private static readonly JsonSerializerOptions ParseOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public const string HttpClientName = "gemini";

        private readonly CvTailoringOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CvTailoringService> _logger;

        public CvTailoringService(
            CvTailoringOptions options,
            IHttpClientFactory httpClientFactory,
            ILogger<CvTailoringService> logger)
        {
            _options = options;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                _logger.LogWarning(
                    "Ai:ApiKey is not configured. CV tailoring will report itself unavailable.");
            }
        }

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

        public CvTailoringOptions Options => _options;

        public async Task<(CvTailoringResultDto? Result, string? Error)> TailorAsync(
            CvTailoringInput input, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                return (null, "AI assistance is not configured on this deployment.");
            }

            var isCoverLetter = input.Request.Mode == CvTailoringMode.CoverLetter;

            var payload = new GeminiRequest(
                Model: _options.Model,
                SystemInstruction: SystemPrompt(input.Request),
                Input: BuildInput(input),
                ResponseFormat: BuildResponseFormat(isCoverLetter),
                GenerationConfig: new GeminiGenerationConfig(
                    MaxOutputTokens: _options.MaxTokens,
                    // Omitted rather than defaulted: an unset level lets the
                    // model decide, and a typo in configuration should not take
                    // the feature down with a 400.
                    ThinkingLevel: string.IsNullOrWhiteSpace(_options.ThinkingLevel)
                        ? null
                        : _options.ThinkingLevel.Trim().ToLowerInvariant()));

            try
            {
                using var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, "interactions")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload, WireOptions),
                        Encoding.UTF8,
                        "application/json")
                };

                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, DescribeFailure(response.StatusCode, body));
                }

                return ReadResult(
                    JsonSerializer.Deserialize<GeminiResponse>(body, WireOptions),
                    input.Request.Mode);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Separated from a caller-cancelled request, which is not an
                // error and should not be logged as one.
                _logger.LogError(ex, "The model provider did not answer within {Seconds}s", _options.TimeoutSeconds);

                return (null, "The assistant took too long to answer. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Could not reach the model provider");

                return (null, "Could not reach the assistant. Please try again shortly.");
            }
            catch (JsonException ex)
            {
                // The schema is enforced upstream, so this should not happen -
                // which is exactly why it is worth logging loudly rather than
                // folding into a catch-all.
                _logger.LogError(ex, "The model returned JSON that did not match the requested schema");

                return (null, "The assistant returned an answer that could not be read. Please try again.");
            }
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // Set here rather than at registration so a deployment can point the
            // service somewhere else through configuration alone.
            client.BaseAddress = new Uri(
                _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

            client.DefaultRequestHeaders.Add("x-goog-api-key", _options.ApiKey);
            client.DefaultRequestHeaders.Add("Api-Revision", _options.ApiRevision);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        /// <summary>
        /// The provider's own error text is logged, never returned: it can carry
        /// fragments of the request, and a user could not act on it anyway.
        /// </summary>
        private string DescribeFailure(HttpStatusCode status, string body)
        {
            // A key this API will not accept comes back as 400 API_KEY_INVALID,
            // not as 401. Going by the status alone files a misconfigured
            // deployment under "malformed request", which sends whoever is
            // debugging it to read the JSON we send instead of the key we send
            // it with. Found by the first real call this feature ever made.
            if (body.Contains("API_KEY_INVALID", StringComparison.Ordinal))
            {
                _logger.LogError("The configured Ai:ApiKey was rejected. Body: {Body}", body);

                return "AI assistance is misconfigured on this deployment.";
            }

            switch (status)
            {
                case HttpStatusCode.TooManyRequests:
                    _logger.LogWarning("Rate limited by the model provider. Body: {Body}", body);

                    return "The assistant is busy right now. Please try again in a minute.";

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    // Configuration, not user error - say so plainly in the log
                    // so whoever reads it does not go hunting in the request.
                    _logger.LogError("The configured Ai:ApiKey was rejected. Body: {Body}", body);

                    return "AI assistance is misconfigured on this deployment.";

                case HttpStatusCode.BadRequest:
                    // Almost always the request shape or the schema, which makes
                    // it a bug here rather than anything the user did.
                    _logger.LogError(
                        "The model provider rejected the request as malformed. Model: {Model}, body: {Body}",
                        _options.Model, body);

                    return "The assistant could not complete that request.";

                default:
                    if ((int)status >= 500)
                    {
                        _logger.LogError(
                            "The model provider returned {Status}. Body: {Body}", (int)status, body);

                        return "The assistant is unavailable right now. Please try again shortly.";
                    }

                    _logger.LogError(
                        "Unexpected {Status} from the model provider. Body: {Body}", (int)status, body);

                    return "The assistant could not complete that request.";
            }
        }

        /// <summary>
        /// The Interactions API reports its outcome as a status plus a timeline
        /// of steps, with no single stop-reason field. A declined answer
        /// therefore looks like a success with nothing in it, which is why the
        /// text is checked rather than assumed.
        /// </summary>
        private (CvTailoringResultDto? Result, string? Error) ReadResult(
            GeminiResponse? interaction, CvTailoringMode mode)
        {
            if (interaction is null)
            {
                _logger.LogError("The model provider returned an empty body");

                return (null, "The assistant returned an empty answer. Please try again.");
            }

            var status = interaction.Status?.ToLowerInvariant();

            if (status is "failed" or "budget_exceeded")
            {
                _logger.LogError("The interaction ended as {Status}", interaction.Status);

                return (null, "The assistant could not complete that request.");
            }

            var text = interaction.Steps?
                .Where(step => string.Equals(step.Type, "model_output", StringComparison.OrdinalIgnoreCase))
                .SelectMany(step => step.Content ?? [])
                .Where(part => string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
                .Select(part => part.Text)
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

            if (string.IsNullOrWhiteSpace(text))
            {
                // A completed interaction carrying no output is the shape a
                // safety block takes here. Reported as a decline rather than as
                // a fault, because that is what it is.
                _logger.LogWarning(
                    "CV tailoring produced no output. Status: {Status}", interaction.Status);

                return (null,
                    "The assistant declined to answer for this posting. Check the job description " +
                    "for anything unusual, or try a different one.");
            }

            if (status == "incomplete")
            {
                // Truncated at the token ceiling: the JSON will not parse, and
                // saying so beats a schema error the user cannot act on.
                _logger.LogWarning("The answer was truncated at {MaxTokens} output tokens", _options.MaxTokens);

                return (null, "The assistant's answer was cut short. Please try a shorter job description.");
            }

            _logger.LogInformation(
                "CV tailoring completed. Mode: {Mode}, in: {In} tokens, out: {Out} tokens",
                mode, interaction.Usage?.TotalInputTokens, interaction.Usage?.TotalOutputTokens);

            return (Parse(text, mode), null);
        }

        private static List<GeminiPart> BuildInput(CvTailoringInput input)
        {
            var instruction = UserPrompt(input.Request);

            // The CV goes first and the instruction last. A document ahead of
            // the question is the documented ordering, and it keeps the part
            // that varies per request at the end.
            if (input.Cv.Kind == CvContentKind.Pdf)
            {
                return
                [
                    new GeminiPart(
                        Type: "document",
                        Data: Convert.ToBase64String(input.Cv.Pdf!),
                        MimeType: "application/pdf"),
                    new GeminiPart(Type: "text", Text: instruction)
                ];
            }

            return
            [
                new GeminiPart(
                    Type: "text",
                    Text: $"<cv>\n{input.Cv.Text}\n</cv>\n\n{instruction}")
            ];
        }

        private static GeminiResponseFormat BuildResponseFormat(bool isCoverLetter) =>
            new(
                Type: "text",
                MimeType: "application/json",
                Schema: JsonSerializer.Deserialize<JsonElement>(
                    isCoverLetter ? CoverLetterSchema : SuggestionsSchema));

        private static CvTailoringResultDto Parse(string json, CvTailoringMode mode)
        {
            var result = JsonSerializer.Deserialize<CvTailoringResultDto>(json, ParseOptions)
                         ?? new CvTailoringResultDto();

            // The schema does not carry the mode - the caller asked for it, so
            // the caller is the authority on what came back.
            result.Mode = mode;

            return result;
        }

        // ----------------------------------------------------------- wire shapes

        private sealed record GeminiRequest(
            string Model,
            string SystemInstruction,
            List<GeminiPart> Input,
            GeminiResponseFormat ResponseFormat,
            GeminiGenerationConfig GenerationConfig);

        /// <summary>
        /// One shape for both part kinds: the API discriminates on <c>type</c>,
        /// and the fields the other kind does not use are omitted rather than
        /// sent as nulls.
        /// </summary>
        private sealed record GeminiPart(
            string Type,
            string? Text = null,
            string? Data = null,
            string? MimeType = null);

        private sealed record GeminiResponseFormat(string Type, string MimeType, JsonElement Schema);

        private sealed record GeminiGenerationConfig(int MaxOutputTokens, string? ThinkingLevel);

        private sealed record GeminiResponse(
            string? Status,
            List<GeminiStep>? Steps,
            GeminiUsage? Usage);

        private sealed record GeminiStep(string? Type, List<GeminiContent>? Content);

        private sealed record GeminiContent(string? Type, string? Text);

        private sealed record GeminiUsage(int TotalInputTokens, int TotalOutputTokens);

        // --------------------------------------------------------------- prompts

        private static string LanguageName(string code) =>
            code?.ToLowerInvariant() switch
            {
                "en" => "English",
                _ => "Spanish"
            };

        private static string SystemPrompt(CvTailoringRequestDto request) =>
            $"""
            You are an experienced technical recruiter reviewing a CV against a specific job posting
            on behalf of the candidate who owns it.

            Hard rules:

            1. Never invent experience, skills, employers, dates, or qualifications. Every strength you
               cite and every rewrite you propose must be traceable to something already written in the
               CV. If the CV does not support a claim the posting asks for, that is a gap to report,
               never a line to write.
            2. A proposed rewrite may reframe, re-order, sharpen or quantify what is already there.
               It may not add a fact that is not there.
            3. Quote the CV accurately in the "current" field. If you are proposing new wording for a
               section that does not exist yet, say so in that field rather than inventing a quotation.
            4. Be specific and concrete. "Add more detail about your leadership" is useless advice;
               naming the line to change and how to change it is not.
            5. Write every piece of prose you produce in {LanguageName(request.Language)}, even when the
               CV and the posting are written in another language. Keep proper nouns and technical terms
               in their original form.
            """;

        private static string UserPrompt(CvTailoringRequestDto request)
        {
            var role = string.IsNullOrWhiteSpace(request.JobTitle) ? "(not stated)" : request.JobTitle.Trim();
            var company = string.IsNullOrWhiteSpace(request.CompanyName) ? "(not stated)" : request.CompanyName.Trim();

            var task = request.Mode == CvTailoringMode.CoverLetter
                ? """
                  Write a cover letter for this application, based strictly on the CV above.

                  Keep it under 300 words and in four short paragraphs: why this role, the single most
                  relevant piece of experience, what the candidate would bring, and a brief close. Write
                  it as the candidate, in the first person. No placeholder brackets and no invented
                  detail - if you do not know something, leave it out rather than marking it for the
                  reader to fill in. Also give a one-sentence summary of the angle you took.
                  """
                : """
                  Analyse this CV against the posting and report:

                  - What genuinely matches, with the CV line that shows it.
                  - What the posting requires that the CV does not support, marked must-have or
                    nice-to-have, with honest advice about what the candidate can do.
                  - Concrete rewrites: the section, the current wording, your proposed wording, and why.
                    Give the rewrites that matter, not one per section for its own sake.
                  - Terms the posting uses that the CV never uses, and which the candidate's real
                    experience would justify adding.
                  """;

            return $"""
                <job_posting>
                Role: {role}
                Company: {company}

                {request.JobDescription.Trim()}
                </job_posting>

                {task}
                """;
        }

        // --------------------------------------------------------------- schemas

        /// <summary>
        /// Written as JSON rather than assembled from objects: this is a schema
        /// document, and it is far easier to check one against the shape it
        /// describes when it is laid out like one.
        /// </summary>
        private const string SuggestionsSchema = """
            {
              "type": "object",
              "properties": {
                "summary": {
                  "type": "string",
                  "description": "Two or three sentences on how well this CV fits this posting."
                },
                "strengths": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "point": { "type": "string" },
                      "evidence": { "type": "string", "description": "The supporting line from the CV." }
                    },
                    "required": ["point", "evidence"],
                    "additionalProperties": false
                  }
                },
                "gaps": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "requirement": { "type": "string" },
                      "severity": { "type": "string", "enum": ["must-have", "nice-to-have"] },
                      "advice": { "type": "string" }
                    },
                    "required": ["requirement", "severity", "advice"],
                    "additionalProperties": false
                  }
                },
                "suggestions": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "section": { "type": "string" },
                      "current": { "type": "string" },
                      "proposed": { "type": "string" },
                      "why": { "type": "string" }
                    },
                    "required": ["section", "current", "proposed", "why"],
                    "additionalProperties": false
                  }
                },
                "missingKeywords": {
                  "type": "array",
                  "items": { "type": "string" }
                }
              },
              "required": ["summary", "strengths", "gaps", "suggestions", "missingKeywords"],
              "additionalProperties": false
            }
            """;

        private const string CoverLetterSchema = """
            {
              "type": "object",
              "properties": {
                "summary": {
                  "type": "string",
                  "description": "One sentence on the angle the letter takes."
                },
                "coverLetter": {
                  "type": "string",
                  "description": "The letter itself, paragraphs separated by blank lines."
                }
              },
              "required": ["summary", "coverLetter"],
              "additionalProperties": false
            }
            """;
    }
}
