using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
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
        /// Sonnet is the default because this is a demo running on free tiers:
        /// the allowance is five calls, and a default that quietly costs several
        /// times more per call is the wrong one to ship unasked. A deployment
        /// that wants a larger model sets it here.
        /// </summary>
        public string Model { get; set; } = "claude-sonnet-5";

        /// <summary>
        /// Optional: "low", "medium", "high", "xhigh", "max". Left unset so the
        /// API's own default applies - naming a level here would break a
        /// deployment that configured a model which does not accept one.
        /// </summary>
        public string? Effort { get; set; }

        public int MaxTokens { get; set; } = 8_000;

        /// <summary>Calls one account may make per <see cref="PeriodDays"/>.</summary>
        public int CallsPerPeriod { get; set; } = 5;

        public int PeriodDays { get; set; } = 30;

        /// <summary>
        /// Roughly three pages of posting. Long enough for any real advert,
        /// short enough that pasting an entire careers site does not quietly
        /// cost ten times what a normal request does.
        /// </summary>
        public int MaxJobDescriptionChars { get; set; } = 8_000;

        /// <summary>Matches the upload cap, so any CV that could be stored can be read.</summary>
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
    /// CV tailoring against a job posting, backed by Claude.
    ///
    /// Two design points worth stating, because both were choices:
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
    /// </summary>
    public class CvTailoringService : ICvTailoringService
    {
        private static readonly JsonSerializerOptions ParseOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly CvTailoringOptions _options;
        private readonly AnthropicClient? _client;
        private readonly ILogger<CvTailoringService> _logger;

        public CvTailoringService(CvTailoringOptions options, ILogger<CvTailoringService> logger)
        {
            _options = options;
            _logger = logger;

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                _client = new AnthropicClient { ApiKey = options.ApiKey };
            }
            else
            {
                _logger.LogWarning(
                    "Ai:ApiKey is not configured. CV tailoring will report itself unavailable.");
            }
        }

        public bool IsEnabled => _client is not null;

        public CvTailoringOptions Options => _options;

        public async Task<(CvTailoringResultDto? Result, string? Error)> TailorAsync(
            CvTailoringInput input, CancellationToken cancellationToken = default)
        {
            if (_client is null)
            {
                return (null, "AI assistance is not configured on this deployment.");
            }

            var isCoverLetter = input.Request.Mode == CvTailoringMode.CoverLetter;

            var parameters = new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = SystemPrompt(input.Request),
                Messages = [BuildMessage(input)],
                OutputConfig = BuildOutputConfig(isCoverLetter)
            };

            try
            {
                var response = await _client.Messages.Create(parameters, cancellationToken);

                // Checked before the content is touched: a refusal is a 200
                // with no usable body, so reading blindly would surface as a
                // confusing empty result rather than as what it is.
                if (response.StopReason == "refusal")
                {
                    _logger.LogWarning(
                        "CV tailoring was declined. Category: {Category}",
                        response.StopDetails?.Category);

                    return (null,
                        "The assistant declined to answer for this posting. Check the job description " +
                        "for anything unusual, or try a different one.");
                }

                var json = response.Content
                    .Select(block => block.Value)
                    .OfType<TextBlock>()
                    .Select(text => text.Text)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogError("CV tailoring returned no text. Stop reason: {Stop}", response.StopReason);

                    return (null, "The assistant returned an empty answer. Please try again.");
                }

                _logger.LogInformation(
                    "CV tailoring completed. Mode: {Mode}, in: {In} tokens, out: {Out} tokens",
                    input.Request.Mode, response.Usage.InputTokens, response.Usage.OutputTokens);

                return (Parse(json, input.Request.Mode), null);
            }
            catch (AnthropicRateLimitException ex)
            {
                _logger.LogWarning(ex, "Rate limited by the model provider");

                return (null, "The assistant is busy right now. Please try again in a minute.");
            }
            catch (AnthropicUnauthorizedException ex)
            {
                // Configuration, not user error - say so plainly in the log so
                // whoever reads it does not go hunting in the request.
                _logger.LogError(ex, "The configured Ai:ApiKey was rejected");

                return (null, "AI assistance is misconfigured on this deployment.");
            }
            catch (Anthropic5xxException ex)
            {
                _logger.LogError(ex, "The model provider returned a server error");

                return (null, "The assistant is unavailable right now. Please try again shortly.");
            }
            catch (AnthropicIOException ex)
            {
                _logger.LogError(ex, "Could not reach the model provider");

                return (null, "Could not reach the assistant. Please try again shortly.");
            }
            catch (AnthropicApiException ex)
            {
                _logger.LogError(ex, "Unexpected error from the model provider");

                return (null, "The assistant could not complete that request.");
            }
            catch (JsonException ex)
            {
                // The schema is enforced upstream, so this should not happen -
                // which is exactly why it is worth logging loudly rather than
                // folding into the catch-all above.
                _logger.LogError(ex, "The model returned JSON that did not match the requested schema");

                return (null, "The assistant returned an answer that could not be read. Please try again.");
            }
        }

        private static MessageParam BuildMessage(CvTailoringInput input)
        {
            var instruction = UserPrompt(input.Request);

            // The CV goes first and the instruction last. A document ahead of
            // the question is the documented ordering, and it keeps the part
            // that varies per request at the end.
            if (input.Cv.Kind == CvContentKind.Pdf)
            {
                return new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new DocumentBlockParam
                        {
                            Source = new Base64PdfSource { Data = Convert.ToBase64String(input.Cv.Pdf!) }
                        },
                        new TextBlockParam { Text = instruction }
                    }
                };
            }

            return new MessageParam
            {
                Role = Role.User,
                Content = new List<ContentBlockParam>
                {
                    new TextBlockParam
                    {
                        Text = $"<cv>\n{input.Cv.Text}\n</cv>\n\n{instruction}"
                    }
                }
            };
        }

        private OutputConfig BuildOutputConfig(bool isCoverLetter)
        {
            var format = new JsonOutputFormat
            {
                Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    isCoverLetter ? CoverLetterSchema : SuggestionsSchema)!
            };

            // Effort is init-only, so the two shapes are built rather than one
            // being patched. An unparseable value falls through to the API's
            // own default instead of throwing: a typo in configuration should
            // not take the feature down.
            return !string.IsNullOrWhiteSpace(_options.Effort)
                   && Enum.TryParse<Effort>(_options.Effort, ignoreCase: true, out var level)
                ? new OutputConfig { Format = format, Effort = level }
                : new OutputConfig { Format = format };
        }

        private static CvTailoringResultDto Parse(string json, CvTailoringMode mode)
        {
            var result = JsonSerializer.Deserialize<CvTailoringResultDto>(json, ParseOptions)
                         ?? new CvTailoringResultDto();

            // The schema does not carry the mode - the caller asked for it, so
            // the caller is the authority on what came back.
            result.Mode = mode;

            return result;
        }

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
