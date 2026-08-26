using MiFuturoTrabajo.Api.Services;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    /// <summary>
    /// The rules that decide what lands in blob storage. These are cheap to
    /// cover exhaustively and expensive to get wrong, since anything accepted
    /// here is handed back to a browser later.
    /// </summary>
    public class CvFileValidatorTests
    {
        private static byte[] Pdf => new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        private static byte[] Zip => new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 };
        private static byte[] Ole2 => new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        private static byte[] WindowsExecutable => new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

        [Fact]
        public void Pdf_WithPdfBytes_IsAccepted()
        {
            var result = CvFileValidator.Validate("cv.pdf", 120_000, Pdf);

            Assert.True(result.IsValid);
            Assert.Equal(".pdf", result.Extension);
            Assert.Equal("application/pdf", result.ContentType);
        }

        [Fact]
        public void Docx_WithZipBytes_IsAccepted()
        {
            var result = CvFileValidator.Validate("cv.docx", 40_000, Zip);

            Assert.True(result.IsValid);
            Assert.Contains("wordprocessingml", result.ContentType);
        }

        [Fact]
        public void Doc_WithOle2Bytes_IsAccepted()
        {
            Assert.True(CvFileValidator.Validate("cv.doc", 40_000, Ole2).IsValid);
        }

        /// <summary>Word 2007+ saved as .doc is OOXML, not OLE2. Both are real Word documents.</summary>
        [Fact]
        public void Doc_WithOoxmlBytes_IsAccepted()
        {
            Assert.True(CvFileValidator.Validate("cv.doc", 40_000, Zip).IsValid);
        }

        [Fact]
        public void UppercaseExtension_IsAccepted()
        {
            Assert.True(CvFileValidator.Validate("CV.PDF", 1000, Pdf).IsValid);
        }

        /// <summary>The whole point of sniffing: an executable renamed to .pdf.</summary>
        [Fact]
        public void ExecutableRenamedToPdf_IsRejected()
        {
            var result = CvFileValidator.Validate("cv.pdf", 120_000, WindowsExecutable);

            Assert.False(result.IsValid);
            Assert.Contains("does not look like", result.Error);
        }

        [Fact]
        public void DocxBytesInAPdfName_IsRejected()
        {
            Assert.False(CvFileValidator.Validate("cv.pdf", 40_000, Zip).IsValid);
        }

        [Theory]
        [InlineData("payload.exe")]
        [InlineData("notes.txt")]
        [InlineData("archive.zip")]
        [InlineData("script.js")]
        [InlineData("noextension")]
        public void DisallowedExtension_IsRejected(string fileName)
        {
            var result = CvFileValidator.Validate(fileName, 1000, Pdf);

            Assert.False(result.IsValid);
            Assert.Contains("not accepted", result.Error);
        }

        [Fact]
        public void OverTheSizeLimit_IsRejected()
        {
            var result = CvFileValidator.Validate("cv.pdf", CvFileValidator.MaxSizeBytes + 1, Pdf);

            Assert.False(result.IsValid);
            Assert.Contains("limit", result.Error);
        }

        [Fact]
        public void ExactlyAtTheSizeLimit_IsAccepted()
        {
            Assert.True(CvFileValidator.Validate("cv.pdf", CvFileValidator.MaxSizeBytes, Pdf).IsValid);
        }

        [Fact]
        public void EmptyFile_IsRejected()
        {
            var result = CvFileValidator.Validate("cv.pdf", 0, Array.Empty<byte>());

            Assert.False(result.IsValid);
            Assert.Contains("empty", result.Error);
        }

        [Fact]
        public void MissingName_IsRejected()
        {
            Assert.False(CvFileValidator.Validate("", 1000, Pdf).IsValid);
        }

        /// <summary>A file shorter than the signature must not read past its end.</summary>
        [Fact]
        public void TruncatedHeader_IsRejectedWithoutThrowing()
        {
            var result = CvFileValidator.Validate("cv.pdf", 3, new byte[] { 0x25, 0x50, 0x44 });

            Assert.False(result.IsValid);
        }
    }
}
