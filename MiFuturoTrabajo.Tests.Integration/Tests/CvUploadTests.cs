using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// End-to-end cover for CV documents: what gets stored, what gets refused,
    /// what a download hands back, and what happens to the bytes on delete.
    ///
    /// Storage is faked (see <see cref="FakeBlobStorageService"/>) so the suite
    /// needs no emulator, but the fake is inspected directly - these assert on
    /// storage contents, not just on status codes.
    /// </summary>
    public class CvUploadTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public CvUploadTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
        }

        // --- helpers ----------------------------------------------------

        private static byte[] PdfBytes(int size = 2048)
        {
            var bytes = new byte[size];
            Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
            return bytes;
        }

        private static byte[] ExecutableBytes(int size = 2048)
        {
            var bytes = new byte[size];
            new byte[] { 0x4D, 0x5A, 0x90, 0x00 }.CopyTo(bytes, 0);
            return bytes;
        }

        private async Task<HttpClient> RegisteredClientAsync()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = $"cv{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "CV",
                LastName = "Tester"
            });

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

            return client;
        }

        private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string? name = null)
        {
            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(file, "file", fileName);

            if (name != null)
            {
                content.Add(new StringContent(name), "name");
            }

            return content;
        }

        // --- the happy path ---------------------------------------------

        [Fact]
        public async Task Upload_ThenDownload_ThenDelete_RemovesTheBlob()
        {
            var client = await RegisteredClientAsync();
            var before = _factory.Blobs.Count;

            // Upload
            var uploadResponse = await client.PostAsync(
                "/api/applicant/UploadCV", Upload(PdfBytes(3000), "my-resume.pdf", "Backend CV"));

            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

            var cv = await uploadResponse.Content.ReadFromJsonAsync<CVVersionDto>();
            Assert.NotNull(cv);
            Assert.Equal("Backend CV", cv!.Name);
            Assert.Equal("my-resume.pdf", cv.FileName);
            Assert.Equal(3000, cv.SizeBytes);
            Assert.True(cv.HasFile);
            Assert.True(cv.IsDefault, "the first CV uploaded should become the default");

            Assert.Equal(before + 1, _factory.Blobs.Count);

            // Download link
            var linkResponse = await client.GetAsync($"/api/applicant/CV/{cv.Id}/download");
            Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

            var link = await linkResponse.Content.ReadFromJsonAsync<CVDownloadLinkDto>();
            Assert.NotNull(link);
            Assert.False(string.IsNullOrWhiteSpace(link!.Url));
            Assert.Equal("my-resume.pdf", link.FileName);
            Assert.True(link.ExpiresAtUtc > DateTime.UtcNow, "the link should not arrive already expired");
            Assert.True(link.ExpiresAtUtc < DateTime.UtcNow.AddHours(1), "the link should be short-lived");

            // Delete
            var deleteResponse = await client.DeleteAsync($"/api/applicant/CV/{cv.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            Assert.Equal(before, _factory.Blobs.Count);
        }

        /// <summary>
        /// The blob path is derived server-side and namespaced per user, so the
        /// documents of one account cannot collide with or reach into another.
        /// </summary>
        [Fact]
        public async Task Upload_StoresTheBlobUnderTheOwnerPrefix()
        {
            var client = await RegisteredClientAsync();

            await client.PostAsync("/api/applicant/UploadCV", Upload(PdfBytes(), "cv.pdf"));

            var stored = _factory.Blobs.StoredPaths.Where(p => p.EndsWith(".pdf")).ToList();

            Assert.NotEmpty(stored);
            Assert.All(stored, p => Assert.Contains("/", p));
            Assert.All(stored, p => Assert.DoesNotContain("..", p));
        }

        // --- what gets refused -------------------------------------------

        [Fact]
        public async Task Upload_ExecutableRenamedToPdf_IsRejected()
        {
            var client = await RegisteredClientAsync();
            var before = _factory.Blobs.Count;

            var response = await client.PostAsync(
                "/api/applicant/UploadCV", Upload(ExecutableBytes(), "totally-a-cv.pdf"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("does not look like", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, _factory.Blobs.Count);
        }

        [Fact]
        public async Task Upload_DisallowedExtension_IsRejected()
        {
            var client = await RegisteredClientAsync();

            var response = await client.PostAsync(
                "/api/applicant/UploadCV", Upload(ExecutableBytes(), "payload.exe"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("not accepted", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Upload_OverTheSizeLimit_IsRejected()
        {
            var client = await RegisteredClientAsync();
            var before = _factory.Blobs.Count;

            var tooBig = PdfBytes((5 * 1024 * 1024) + 1024);

            var response = await client.PostAsync("/api/applicant/UploadCV", Upload(tooBig, "huge.pdf"));

            Assert.True(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
                $"expected the upload to be refused, got {(int)response.StatusCode}");

            Assert.Equal(before, _factory.Blobs.Count);
        }

        [Fact]
        public async Task Upload_WithoutAToken_IsUnauthorized()
        {
            var anonymous = _factory.CreateClient();

            var response = await anonymous.PostAsync("/api/applicant/UploadCV", Upload(PdfBytes(), "cv.pdf"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // --- isolation ----------------------------------------------------

        /// <summary>
        /// The download endpoint looks the CV up inside the applicant belonging
        /// to the caller, so knowing the CV id of another user is not enough to
        /// obtain a link to their document.
        /// </summary>
        [Fact]
        public async Task Download_AnotherUsersCv_IsNotFound()
        {
            var owner = await RegisteredClientAsync();
            var intruder = await RegisteredClientAsync();

            var uploadResponse = await owner.PostAsync(
                "/api/applicant/UploadCV", Upload(PdfBytes(), "private.pdf"));
            var cv = await uploadResponse.Content.ReadFromJsonAsync<CVVersionDto>();

            var response = await intruder.GetAsync($"/api/applicant/CV/{cv!.Id}/download");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Delete_AnotherUsersCv_LeavesTheBlobIntact()
        {
            var owner = await RegisteredClientAsync();
            var intruder = await RegisteredClientAsync();

            var uploadResponse = await owner.PostAsync(
                "/api/applicant/UploadCV", Upload(PdfBytes(), "private.pdf"));
            var cv = await uploadResponse.Content.ReadFromJsonAsync<CVVersionDto>();

            var stored = _factory.Blobs.Count;

            var response = await intruder.DeleteAsync($"/api/applicant/CV/{cv!.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(stored, _factory.Blobs.Count);
        }

        // --- the wire contract --------------------------------------------

        /// <summary>
        /// The storage key must never reach the client, or the expiring-link
        /// design buys nothing.
        /// </summary>
        [Fact]
        public async Task UploadResponse_DoesNotContainTheStorageKey()
        {
            var client = await RegisteredClientAsync();

            var response = await client.PostAsync("/api/applicant/UploadCV", Upload(PdfBytes(), "cv.pdf"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("blobPath", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cv-uploads", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A PATCH may rename a CV but must not be able to repoint it. Before
        /// this phase the update path assigned a client-supplied file reference
        /// straight onto the entity.
        /// </summary>
        [Fact]
        public async Task Patch_CannotRepointACvAtAnotherDocument()
        {
            var client = await RegisteredClientAsync();

            var uploadResponse = await client.PostAsync("/api/applicant/UploadCV", Upload(PdfBytes(), "mine.pdf"));
            var cv = await uploadResponse.Content.ReadFromJsonAsync<CVVersionDto>();

            var patch = await client.PatchAsJsonAsync($"/api/cvversion/{cv!.Id}", new CVVersionDto
            {
                Name = "Renamed",
                FileName = "someone-elses.pdf",
                SizeBytes = 999_999,
                HasFile = true
            });

            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            var updated = await patch.Content.ReadFromJsonAsync<CVVersionDto>();
            Assert.Equal("Renamed", updated!.Name);
            Assert.Equal("mine.pdf", updated.FileName);
            Assert.Equal(cv.SizeBytes, updated.SizeBytes);
        }

        /// <summary>A metadata-only CV version has nothing to download.</summary>
        [Fact]
        public async Task Download_CvWithNoFile_IsNotFound()
        {
            var client = await RegisteredClientAsync();

            var createResponse = await client.PostAsJsonAsync("/api/cvversion", new CVVersionDto
            {
                Name = "Placeholder",
                UploadedDate = DateTime.UtcNow
            });

            var created = await createResponse.Content.ReadFromJsonAsync<CVVersionDto>();
            Assert.False(created!.HasFile);

            var response = await client.GetAsync($"/api/cvversion/{created.Id}/download");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
