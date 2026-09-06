using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Shared.Models;

namespace MiFuturoTrabajo.Tests.Integration
{
    /// <summary>
    /// Stands in for the model call.
    ///
    /// The suite must not talk to a model: it would cost money per run, need a
    /// network and an API key in CI, and answer differently every time - so any
    /// assertion worth making about the wording would be flaky, and one that
    /// was not worth making would be testing nothing.
    ///
    /// What is worth testing is everything wrapped around that call, and all of
    /// it is here: who may read which CV, the allowance being spent and
    /// enforced, the input limits, and what happens when the provider fails.
    /// This records what it was handed so a test can prove the right CV and the
    /// right posting actually reached it.
    /// </summary>
    public class StubCvTailoringService : ICvTailoringService
    {
        public bool IsEnabled { get; set; } = true;

        public CvTailoringOptions Options { get; set; } = new();

        /// <summary>Set to make the next call fail the way a provider outage would.</summary>
        public string? NextError { get; set; }

        public int CallCount { get; private set; }

        public CvTailoringInput? LastInput { get; private set; }

        public void Reset()
        {
            IsEnabled = true;
            NextError = null;
            CallCount = 0;
            LastInput = null;
        }

        public Task<(CvTailoringResultDto? Result, string? Error)> TailorAsync(
            CvTailoringInput input, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastInput = input;

            if (NextError != null)
            {
                return Task.FromResult<(CvTailoringResultDto?, string?)>((null, NextError));
            }

            var result = input.Request.Mode == CvTailoringMode.CoverLetter
                ? new CvTailoringResultDto
                {
                    Mode = CvTailoringMode.CoverLetter,
                    Summary = "stub summary",
                    CoverLetter = "Dear hiring manager,\n\nStub letter.\n\nRegards."
                }
                : new CvTailoringResultDto
                {
                    Mode = CvTailoringMode.Suggestions,
                    Summary = "stub summary",
                    Strengths = { new CvStrengthDto { Point = "stub point", Evidence = "stub evidence" } },
                    Gaps = { new CvGapDto { Requirement = "Kubernetes", Severity = "must-have", Advice = "stub advice" } },
                    Suggestions =
                    {
                        new CvSuggestionDto
                        {
                            Section = "Summary", Current = "stub current",
                            Proposed = "stub proposed", Why = "stub why"
                        }
                    },
                    MissingKeywords = { "kubernetes" }
                };

            return Task.FromResult<(CvTailoringResultDto?, string?)>((result, null));
        }
    }
}
