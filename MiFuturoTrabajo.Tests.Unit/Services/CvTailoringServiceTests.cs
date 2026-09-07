using System.Net;
using System.Text;
using System.Text.Json;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    /// <summary>
    /// These tests exist because the request is hand-built rather than produced
    /// by a vendor SDK, which makes its shape this repository's problem. No real
    /// call is made: a stub handler captures what would have gone out and hands
    /// back what the provider would have sent, so both directions of the wire
    /// contract are checked without a key and without spending anything.
    ///
    /// What they cannot check is that the shape asserted here is the shape the
    /// provider actually wants. That comes from its documentation, and the day
    /// it changes these tests will keep passing while the feature breaks - which
    /// is why the request is pinned to an Api-Revision.
    /// </summary>
    public class CvTailoringServiceTests
    {
        private const string SuccessBody = """
            {
              "id": "v1_abc",
              "object": "interaction",
              "model": "gemini-3.5-flash-lite",
              "status": "completed",
              "usage": { "total_input_tokens": 1200, "total_output_tokens": 340 },
              "steps": [
                { "type": "thought", "signature": "Ev..." },
                {
                  "type": "model_output",
                  "content": [
                    {
                      "type": "text",
                      "text": "{\"summary\":\"Solid fit.\",\"strengths\":[{\"point\":\"Five years of .NET\",\"evidence\":\"Backend developer, 2019-2024\"}],\"gaps\":[{\"requirement\":\"Kubernetes\",\"severity\":\"must-have\",\"advice\":\"Say what you ran in Docker.\"}],\"suggestions\":[{\"section\":\"Summary\",\"current\":\"Developer.\",\"proposed\":\"Backend developer, .NET and Azure.\",\"why\":\"Names the stack the posting asks for.\"}],\"missingKeywords\":[\"Kubernetes\"]}"
                    }
                  ]
                }
              ]
            }
            """;

        // ------------------------------------------------------------ the request

        [Fact]
        public async Task ThePdfIsSentAsADocumentPartAheadOfTheInstruction()
        {
            var (service, handler) = Build();

            await service.TailorAsync(PdfInput());

            var input = handler.Body().GetProperty("input");

            Assert.Equal(2, input.GetArrayLength());

            var document = input[0];
            Assert.Equal("document", document.GetProperty("type").GetString());
            Assert.Equal("application/pdf", document.GetProperty("mime_type").GetString());
            Assert.Equal(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.7 pretend")),
                document.GetProperty("data").GetString());

            // The instruction comes second, so the part that varies per request
            // sits at the end.
            Assert.Equal("text", input[1].GetProperty("type").GetString());
            Assert.Contains("Senior Backend Developer", input[1].GetProperty("text").GetString());
        }

        [Fact]
        public async Task ADocumentPartCarriesNoEmptyTextField()
        {
            // Sending "text": null alongside a document is the kind of thing an
            // API rejects, and it would only show up as a 400 in production.
            var (service, handler) = Build();

            await service.TailorAsync(PdfInput());

            var document = handler.Body().GetProperty("input")[0];

            Assert.False(document.TryGetProperty("text", out _));
        }

        [Fact]
        public async Task ATextCvBecomesASingleTextPartWithTheCvInlined()
        {
            var (service, handler) = Build();

            await service.TailorAsync(TextInput("Jane Doe\nBackend developer"));

            var input = handler.Body().GetProperty("input");

            Assert.Equal(1, input.GetArrayLength());

            var text = input[0].GetProperty("text").GetString()!;
            Assert.Contains("<cv>", text);
            Assert.Contains("Jane Doe", text);
            Assert.False(input[0].TryGetProperty("data", out _));
        }

        [Fact]
        public async Task TheSystemInstructionIsSentAsAPlainString()
        {
            var (service, handler) = Build();

            await service.TailorAsync(PdfInput());

            var system = handler.Body().GetProperty("system_instruction");

            Assert.Equal(JsonValueKind.String, system.ValueKind);
            Assert.Contains("Never invent experience", system.GetString());
        }

        [Fact]
        public async Task TheResponseFormatNestsTheSchemaUnderMimeType()
        {
            var (service, handler) = Build();

            await service.TailorAsync(PdfInput());

            var format = handler.Body().GetProperty("response_format");

            Assert.Equal("text", format.GetProperty("type").GetString());
            Assert.Equal("application/json", format.GetProperty("mime_type").GetString());

            var schema = format.GetProperty("schema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.True(schema.TryGetProperty("properties", out var properties));
            Assert.True(properties.TryGetProperty("missingKeywords", out _));
        }

        [Fact]
        public async Task CoverLetterModeSendsTheOtherSchema()
        {
            var (service, handler) = Build();

            await service.TailorAsync(PdfInput(CvTailoringMode.CoverLetter), CancellationToken.None);

            var properties = handler.Body()
                .GetProperty("response_format")
                .GetProperty("schema")
                .GetProperty("properties");

            Assert.True(properties.TryGetProperty("coverLetter", out _));
            Assert.False(properties.TryGetProperty("suggestions", out _));
        }

        [Fact]
        public async Task TheKeyAndTheApiRevisionAreSentAsHeaders()
        {
            var (service, handler) = Build(options => options.ApiRevision = "2026-05-20");

            await service.TailorAsync(PdfInput());

            Assert.Equal("test-key", handler.Header("x-goog-api-key"));
            Assert.Equal("2026-05-20", handler.Header("Api-Revision"));
            Assert.EndsWith("/v1beta/interactions", handler.Uri!.AbsoluteUri);
        }

        [Fact]
        public async Task TheTokenCeilingIsSentAndThinkingLevelIsOmittedWhenUnset()
        {
            var (service, handler) = Build(options => options.MaxTokens = 4321);

            await service.TailorAsync(PdfInput());

            var config = handler.Body().GetProperty("generation_config");

            Assert.Equal(4321, config.GetProperty("max_output_tokens").GetInt32());

            // Unset means "let the model decide", which is not the same as
            // sending null and not the same as picking a level here.
            Assert.False(config.TryGetProperty("thinking_level", out _));
        }

        [Fact]
        public async Task AConfiguredThinkingLevelIsSentLowercased()
        {
            var (service, handler) = Build(options => options.ThinkingLevel = "  Low ");

            await service.TailorAsync(PdfInput());

            Assert.Equal(
                "low",
                handler.Body().GetProperty("generation_config").GetProperty("thinking_level").GetString());
        }

        // ----------------------------------------------------------- the response

        [Fact]
        public async Task ASuccessfulAnswerIsParsedOutOfTheModelOutputStep()
        {
            var (service, _) = Build();

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal("Solid fit.", result!.Summary);
            Assert.Equal("Kubernetes", Assert.Single(result.Gaps).Requirement);
            Assert.Equal("Summary", Assert.Single(result.Suggestions).Section);

            // The schema does not carry the mode, so the caller's request is the
            // authority on what came back.
            Assert.Equal(CvTailoringMode.Suggestions, result.Mode);
        }

        [Fact]
        public async Task TheThoughtStepIsNotMistakenForTheAnswer()
        {
            // The reply opens with a "thought" step. Taking the first step's
            // content rather than the first model_output would parse that.
            var (service, _) = Build();

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(error);
            Assert.Equal("Solid fit.", result!.Summary);
        }

        [Fact]
        public async Task ACompletedInteractionWithNoOutputReadsAsADecline()
        {
            // This is the shape a safety block takes: 200, completed, nothing in
            // it. Read blindly it would surface as an empty result.
            var (service, _) = Build(respondWith: """
                { "status": "completed", "steps": [ { "type": "thought" } ] }
                """);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("declined", error);
        }

        [Theory]
        [InlineData("failed")]
        [InlineData("budget_exceeded")]
        public async Task AFailedInteractionIsAnErrorRatherThanAnEmptyAnswer(string status)
        {
            var (service, _) = Build(respondWith: $$"""
                { "status": "{{status}}", "steps": [] }
                """);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("could not complete", error);
        }

        [Fact]
        public async Task ATruncatedAnswerSaysSoRatherThanFailingToParse()
        {
            var (service, _) = Build(respondWith: """
                {
                  "status": "incomplete",
                  "steps": [ { "type": "model_output", "content": [ { "type": "text", "text": "{\"summ" } ] } ]
                }
                """);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("cut short", error);
        }

        [Fact]
        public async Task MalformedJsonInTheAnswerIsReportedRatherThanThrown()
        {
            var (service, _) = Build(respondWith: """
                {
                  "status": "completed",
                  "steps": [ { "type": "model_output", "content": [ { "type": "text", "text": "not json" } ] } ]
                }
                """);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("could not be read", error);
        }

        // -------------------------------------------------------------- failures

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, "busy")]
        [InlineData(HttpStatusCode.Unauthorized, "misconfigured")]
        [InlineData(HttpStatusCode.Forbidden, "misconfigured")]
        [InlineData(HttpStatusCode.BadRequest, "could not complete")]
        [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
        [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable")]
        public async Task EachFailureGetsAMessageAUserCanActfOn(HttpStatusCode status, string expected)
        {
            var (service, _) = Build(statusCode: status, respondWith: """{"error":{"message":"secret detail"}}""");

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains(expected, error);

            // The provider's own text can carry fragments of the request, so it
            // is logged rather than handed to the browser.
            Assert.DoesNotContain("secret detail", error);
        }

        [Fact]
        public async Task ARejectedKeyReadsAsMisconfigurationEvenThoughItArrivesAsA400()
        {
            // This is the real body the provider returned for a bad key, and it
            // is a 400 rather than a 401. Reported as "malformed request" it
            // sends whoever is debugging to read the JSON we send rather than
            // the key we send it with.
            var (service, _) = Build(
                statusCode: HttpStatusCode.BadRequest,
                respondWith: """
                    {
                      "error": {
                        "code": 400,
                        "message": "API key not valid. Please pass a valid API key.",
                        "status": "INVALID_ARGUMENT",
                        "details": [
                          { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "API_KEY_INVALID" }
                        ]
                      }
                    }
                    """);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("misconfigured", error);
        }

        [Fact]
        public async Task AnUnreachableProviderIsReportedRatherThanThrown()
        {
            var (service, _) = Build(throws: new HttpRequestException("no route to host"));

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("Could not reach", error);
        }

        [Fact]
        public async Task ATimeoutIsReportedRatherThanThrown()
        {
            var (service, _) = Build(throws: new TaskCanceledException("timed out"));

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("too long", error);
        }

        // ------------------------------------------------------------ the key

        [Fact]
        public async Task WithNoKeyTheServiceReportsItselfUnavailableAndCallsNothing()
        {
            var (service, handler) = Build(options => options.ApiKey = "   ");

            Assert.False(service.IsEnabled);

            var (result, error) = await service.TailorAsync(PdfInput());

            Assert.Null(result);
            Assert.Contains("not configured", error);
            Assert.Null(handler.Uri);
        }

        // ------------------------------------------------------------- plumbing

        private static (CvTailoringService Service, RecordingHandler Handler) Build(
            Action<CvTailoringOptions>? configure = null,
            string respondWith = SuccessBody,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            Exception? throws = null)
        {
            var options = new CvTailoringOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://example.invalid/v1beta"
            };

            configure?.Invoke(options);

            var handler = new RecordingHandler(statusCode, respondWith, throws);

            return (
                new CvTailoringService(options, new SingleHandlerFactory(handler), NullLogger<CvTailoringService>.Instance),
                handler);
        }

        private static CvTailoringInput PdfInput(CvTailoringMode mode = CvTailoringMode.Suggestions) =>
            new(
                new CvContent(CvContentKind.Pdf, Encoding.UTF8.GetBytes("%PDF-1.7 pretend"), null),
                new CvTailoringRequestDto
                {
                    JobDescription = "We need someone who knows Kubernetes.",
                    JobTitle = "Senior Backend Developer",
                    CompanyName = "Acme",
                    Mode = mode
                });

        private static CvTailoringInput TextInput(string cv) =>
            new(
                new CvContent(CvContentKind.Text, null, cv),
                new CvTailoringRequestDto
                {
                    JobDescription = "We need someone who knows Kubernetes.",
                    JobTitle = "Senior Backend Developer"
                });

        /// <summary>Captures the outgoing request and answers with a canned reply.</summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;
            private readonly Exception? _throws;

            private string? _capturedBody;
            private HttpRequestMessage? _capturedRequest;

            public RecordingHandler(HttpStatusCode statusCode, string body, Exception? throws)
            {
                _statusCode = statusCode;
                _body = body;
                _throws = throws;
            }

            public Uri? Uri => _capturedRequest?.RequestUri;

            public string? Header(string name) =>
                _capturedRequest is not null
                && _capturedRequest.Headers.TryGetValues(name, out var values)
                    ? string.Join(",", values)
                    : null;

            public JsonElement Body() =>
                JsonDocument.Parse(_capturedBody ?? throw new InvalidOperationException("No request was sent."))
                    .RootElement;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _capturedRequest = request;
                _capturedBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_throws is not null)
                {
                    throw _throws;
                }

                return new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed class SingleHandlerFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;

            public SingleHandlerFactory(HttpMessageHandler handler) => _handler = handler;

            // disposeHandler: false - the service disposes each client it makes,
            // and the handler has to outlive that to be read afterwards.
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }
    }
}
