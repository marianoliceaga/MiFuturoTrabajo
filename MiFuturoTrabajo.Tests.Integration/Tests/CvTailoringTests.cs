using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// The endpoint around the model call: who may analyse which CV, what the
    /// allowance does, which inputs are refused, and how a provider failure
    /// surfaces.
    ///
    /// The model itself is stubbed - see <see cref="StubCvTailoringService"/>
    /// for why - so nothing here asserts on the quality of any advice. It
    /// asserts on the things that would cost money or leak a document if they
    /// were wrong.
    /// </summary>
    public class CvTailoringTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public CvTailoringTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _factory.Tailoring.Reset();
        }

        // --- helpers ----------------------------------------------------

        private static byte[] PdfBytes(int size = 2048)
        {
            var bytes = new byte[size];
            Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
            return bytes;
        }

        private async Task<HttpClient> RegisteredClientAsync()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = $"tailor{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Tailor",
                LastName = "Tester"
            });

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

            return client;
        }

        /// <summary>Registers a user and gives them one uploaded CV.</summary>
        private async Task<(HttpClient Client, string CvId)> ClientWithCvAsync()
        {
            var client = await RegisteredClientAsync();

            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(PdfBytes());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(file, "file", "cv.pdf");
            content.Add(new StringContent("My CV"), "name");

            var upload = await client.PostAsync("/api/cvversion/upload", content);
            upload.EnsureSuccessStatusCode();

            var cv = await upload.Content.ReadFromJsonAsync<CVVersionDto>();

            return (client, cv!.Id);
        }

        private static CvTailoringRequestDto Request(string cvId, string? description = null) => new()
        {
            CvVersionId = cvId,
            JobDescription = description ?? "We need a backend developer with Kubernetes experience.",
            JobTitle = "Backend Developer",
            CompanyName = "Acme",
            Mode = CvTailoringMode.Suggestions,
            Language = "en"
        };

        // --- the happy path ---------------------------------------------

        [Fact]
        public async Task Tailor_ReturnsAnalysis_AndSpendsOneCredit()
        {
            var (client, cvId) = await ClientWithCvAsync();

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<CvTailoringResultDto>();

            Assert.NotNull(result);
            Assert.Equal(CvTailoringMode.Suggestions, result!.Mode);
            Assert.NotEmpty(result.Summary);
            Assert.NotEmpty(result.Suggestions);

            // The allowance comes back with the answer, so the page never has
            // to make a second call to know what is left.
            Assert.Equal(1, result.Usage.Used);
            Assert.Equal(result.Usage.Limit - 1, result.Usage.Remaining);
        }

        [Fact]
        public async Task Tailor_PassesTheStoredDocumentAndThePosting()
        {
            var (client, cvId) = await ClientWithCvAsync();

            await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId, "Kubernetes and Go, five years."));

            var input = _factory.Tailoring.LastInput;

            Assert.NotNull(input);

            // A PDF reaches the model as a PDF. If this ever flips to text, the
            // layout has been thrown away somewhere upstream.
            Assert.Equal(MiFuturoTrabajo.Api.Services.CvContentKind.Pdf, input!.Cv.Kind);
            Assert.NotNull(input.Cv.Pdf);
            Assert.Contains("Kubernetes and Go", input.Request.JobDescription);
        }

        [Fact]
        public async Task CoverLetterMode_ReturnsALetter()
        {
            var (client, cvId) = await ClientWithCvAsync();

            var request = Request(cvId);
            request.Mode = CvTailoringMode.CoverLetter;

            var response = await client.PostAsJsonAsync("/api/cvtailoring", request);
            var result = await response.Content.ReadFromJsonAsync<CvTailoringResultDto>();

            Assert.Equal(CvTailoringMode.CoverLetter, result!.Mode);
            Assert.NotEmpty(result.CoverLetter);
        }

        // --- who may read what -------------------------------------------

        [Fact]
        public async Task Anonymous_IsRejected()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request("anything"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AnotherAccountsCv_IsNotFound_AndNeverReachesTheModel()
        {
            var (_, victimCvId) = await ClientWithCvAsync();
            var (attacker, _) = await ClientWithCvAsync();

            _factory.Tailoring.Reset();

            var response = await attacker.PostAsJsonAsync("/api/cvtailoring", Request(victimCvId));

            // The lookup happens inside the caller's own applicant document, so
            // the id is simply not there - not "found but forbidden".
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // The important half: the other account's document was never read,
            // let alone sent anywhere.
            Assert.Equal(0, _factory.Tailoring.CallCount);
        }

        // --- refusals that cost nothing ----------------------------------

        [Fact]
        public async Task EmptyJobDescription_IsRefused_WithoutSpendingACredit()
        {
            var (client, cvId) = await ClientWithCvAsync();

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId, "   "));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, _factory.Tailoring.CallCount);

            var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");
            Assert.Equal(0, status!.Usage.Used);
        }

        [Fact]
        public async Task OverlongJobDescription_IsRefused_AndSaysByHowMuch()
        {
            var (client, cvId) = await ClientWithCvAsync();

            var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");
            var tooLong = new string('x', status!.MaxJobDescriptionChars + 1);

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId, tooLong));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // The message has to name the limit; "bad request" leaves the user
            // trimming text blindly.
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(status.MaxJobDescriptionChars.ToString(), body);

            Assert.Equal(0, _factory.Tailoring.CallCount);
        }

        [Fact]
        public async Task CvWithoutAFile_IsRefused()
        {
            var client = await RegisteredClientAsync();

            // A CV version recorded with no document attached - the API allows
            // this, and there is nothing for the model to read.
            var created = await client.PostAsJsonAsync("/api/cvversion", new CVVersionDto
            {
                Name = "Notes only"
            });

            var cv = await created.Content.ReadFromJsonAsync<CVVersionDto>();

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cv!.Id));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, _factory.Tailoring.CallCount);
        }

        [Fact]
        public async Task WhenNotConfigured_ReportsUnavailable_AndSpendsNothing()
        {
            var (client, cvId) = await ClientWithCvAsync();

            _factory.Tailoring.IsEnabled = false;

            try
            {
                var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");
                Assert.False(status!.Enabled);

                var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

                // A deployment with no key must not burn anybody's allowance on
                // requests that could never have worked.
                var after = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");
                Assert.Equal(0, after!.Usage.Used);
            }
            finally
            {
                _factory.Tailoring.IsEnabled = true;
            }
        }

        // --- the allowance -----------------------------------------------

        [Fact]
        public async Task TheAllowanceRunsOut_AndSaysWhenItRenews()
        {
            var (client, cvId) = await ClientWithCvAsync();

            var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");
            var limit = status!.Usage.Limit;

            Assert.True(limit is > 0 and < 20, $"expected a small configured limit, got {limit}");

            for (var i = 0; i < limit; i++)
            {
                var ok = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            var refused = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

            // A dead end with no date is a support ticket. The refusal says
            // when the allowance comes back.
            var body = await refused.Content.ReadAsStringAsync();
            Assert.Contains(DateTime.UtcNow.Year.ToString(), body);

            Assert.Equal(limit, _factory.Tailoring.CallCount);
        }

        [Fact]
        public async Task TheAllowanceIsPerAccount()
        {
            var (first, firstCv) = await ClientWithCvAsync();

            await first.PostAsJsonAsync("/api/cvtailoring", Request(firstCv));

            var (second, _) = await ClientWithCvAsync();
            var status = await second.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");

            Assert.Equal(0, status!.Usage.Used);
        }

        [Fact]
        public async Task Status_DoesNotSpendAnything()
        {
            var (client, _) = await ClientWithCvAsync();

            for (var i = 0; i < 5; i++)
            {
                await client.GetAsync("/api/cvtailoring/status");
            }

            var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");

            Assert.Equal(0, status!.Usage.Used);
            Assert.Equal(status.Usage.Limit, status.Usage.Remaining);
        }

        // --- provider failure ---------------------------------------------

        [Fact]
        public async Task WhenTheProviderFails_TheErrorIsPassedOn()
        {
            var (client, cvId) = await ClientWithCvAsync();

            _factory.Tailoring.NextError = "The assistant is unavailable right now.";

            var response = await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("unavailable", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task AProviderFailureStillSpendsTheCredit()
        {
            // Documenting the deliberate tradeoff rather than the ideal.
            //
            // The credit is taken before the call, because counting afterwards
            // would let several requests arriving together each read the same
            // count and each find room - an allowance that bounds nothing. The
            // cost of that choice is this: a provider outage can take a credit
            // for an answer nobody received.
            //
            // If this ever becomes a real complaint the fix is a refund path on
            // provider-side failures only, and this test is where that change
            // announces itself.
            var (client, cvId) = await ClientWithCvAsync();

            _factory.Tailoring.NextError = "boom";

            await client.PostAsJsonAsync("/api/cvtailoring", Request(cvId));

            var status = await client.GetFromJsonAsync<CvTailoringStatusDto>("/api/cvtailoring/status");

            Assert.Equal(1, status!.Usage.Used);
        }
    }
}
