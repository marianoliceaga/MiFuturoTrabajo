using System.IO.Compression;
using System.Text;
using MiFuturoTrabajo.Api.Services;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    /// <summary>
    /// Reading a stored CV back out. The .docx path is hand-rolled, so it is
    /// the one that needs covering: the failure it is guarding against is not a
    /// crash but silently mangled text, which would reach a model and produce
    /// confidently wrong advice with nothing to show anything went wrong.
    /// </summary>
    public class CvContentExtractorTests
    {
        private static readonly byte[] PdfBytes =
            Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a pdf, but it starts like one");

        /// <summary>Builds a real .docx: a zip with one WordprocessingML part.</summary>
        private static byte[] Docx(string documentXml)
        {
            using var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("word/document.xml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(documentXml);
            }

            return buffer.ToArray();
        }

        private static string Paragraphs(params string[] lines)
        {
            var body = string.Concat(lines.Select(line =>
                $"<w:p><w:r><w:t>{line}</w:t></w:r></w:p>"));

            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{body}</w:body>
                </w:document>
                """;
        }

        [Fact]
        public void Pdf_IsPassedThroughUntouched()
        {
            var (content, error) = CvContentExtractor.Extract("cv.pdf", PdfBytes);

            Assert.Null(error);
            Assert.NotNull(content);
            Assert.Equal(CvContentKind.Pdf, content!.Kind);

            // Byte-for-byte: the layout is part of the meaning, and anything
            // that rewrote the file here would be quietly discarding it.
            Assert.Equal(PdfBytes, content.Pdf);
        }

        [Fact]
        public void Pdf_IsDetectedByItsBytes_NotItsName()
        {
            // Someone exports a PDF and types the wrong extension. The bytes
            // are the truth, and refusing on the name would be a wrong refusal.
            var (content, error) = CvContentExtractor.Extract("cv.docx", PdfBytes);

            Assert.Null(error);
            Assert.Equal(CvContentKind.Pdf, content!.Kind);
        }

        [Fact]
        public void Docx_TextIsExtracted()
        {
            var (content, error) = CvContentExtractor.Extract(
                "cv.docx", Docx(Paragraphs("Jane Doe", "Backend developer")));

            Assert.Null(error);
            Assert.Equal(CvContentKind.Text, content!.Kind);
            Assert.Contains("Jane Doe", content.Text);
            Assert.Contains("Backend developer", content.Text);
        }

        [Fact]
        public void Docx_ParagraphsDoNotRunTogether()
        {
            // The bug this exists to prevent: concatenating every text node
            // yields "Jane DoeBackend developer", and a CV read that way turns
            // job titles and dates into one unreadable string.
            var (content, _) = CvContentExtractor.Extract(
                "cv.docx", Docx(Paragraphs("Jane Doe", "Backend developer")));

            Assert.DoesNotContain("DoeBackend", content!.Text);
            Assert.Contains("Jane Doe\nBackend developer", content.Text!.Replace("\r\n", "\n"));
        }

        [Fact]
        public void Docx_SplitRunsInOneParagraphStayOnOneLine()
        {
            // Word splits a paragraph into several runs whenever formatting
            // changes mid-sentence - a bolded word is enough. Those must join
            // without a newline, or one emphasised word breaks the line in two.
            var xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r><w:t>Senior </w:t></w:r>
                      <w:r><w:rPr><w:b/></w:rPr><w:t>Backend</w:t></w:r>
                      <w:r><w:t> Engineer</w:t></w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """;

            var (content, _) = CvContentExtractor.Extract("cv.docx", Docx(xml));

            Assert.Contains("Senior Backend Engineer", content!.Text);
        }

        [Fact]
        public void Docx_TabsAreKept()
        {
            var xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r><w:t>2021</w:t><w:tab/><w:t>Acme Corp</w:t></w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """;

            var (content, _) = CvContentExtractor.Extract("cv.docx", Docx(xml));

            Assert.Contains("2021\tAcme Corp", content!.Text);
        }

        [Fact]
        public void Docx_RunsOfBlankParagraphsCollapse()
        {
            var (content, _) = CvContentExtractor.Extract(
                "cv.docx", Docx(Paragraphs("Experience", "", "", "", "Acme Corp")));

            Assert.DoesNotContain("\n\n\n", content!.Text!.Replace("\r\n", "\n"));
            Assert.Contains("Experience", content.Text);
            Assert.Contains("Acme Corp", content.Text);
        }

        [Fact]
        public void Docx_WithNoDocumentPart_IsRefusedClearly()
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                archive.CreateEntry("word/styles.xml");
            }

            var (content, error) = CvContentExtractor.Extract("cv.docx", buffer.ToArray());

            Assert.Null(content);
            Assert.NotNull(error);
        }

        [Fact]
        public void LegacyDoc_IsRefusedWithAdvice()
        {
            var ole2 = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

            var (content, error) = CvContentExtractor.Extract("cv.doc", ole2);

            Assert.Null(content);

            // The refusal has to say what to do next. "Unsupported format"
            // leaves the user stuck with a file the app accepted on upload.
            Assert.Contains("PDF", error);
        }

        [Fact]
        public void EmptyFile_IsRefused()
        {
            var (content, error) = CvContentExtractor.Extract("cv.pdf", Array.Empty<byte>());

            Assert.Null(content);
            Assert.NotNull(error);
        }

        [Fact]
        public void CorruptZip_IsRefusedRatherThanThrowing()
        {
            // Starts like a zip and is not one. This reaches a controller, so
            // it has to come back as a refusal, not an unhandled exception.
            var broken = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF };

            var (content, error) = CvContentExtractor.Extract("cv.docx", broken);

            Assert.Null(content);
            Assert.NotNull(error);
        }

        [Fact]
        public void VeryLongDocument_IsCapped()
        {
            var long_ = string.Join("", Enumerable.Repeat("word ", 20_000));

            var (content, _) = CvContentExtractor.Extract("cv.docx", Docx(Paragraphs(long_)));

            Assert.NotNull(content);
            Assert.True(content!.Text!.Length <= CvContentExtractor.MaxTextChars);
        }
    }
}
