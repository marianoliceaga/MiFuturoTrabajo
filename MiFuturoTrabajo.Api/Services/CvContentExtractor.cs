using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MiFuturoTrabajo.Api.Services
{
    public enum CvContentKind
    {
        /// <summary>Send the file itself; the model reads PDFs natively.</summary>
        Pdf,

        /// <summary>Send extracted plain text.</summary>
        Text
    }

    /// <param name="Kind">Which of the two payloads below is populated.</param>
    /// <param name="Pdf">The original bytes, for <see cref="CvContentKind.Pdf"/>.</param>
    /// <param name="Text">Extracted text, for <see cref="CvContentKind.Text"/>.</param>
    public record CvContent(CvContentKind Kind, byte[]? Pdf, string? Text);

    /// <summary>
    /// Turns a stored CV into something a model can read.
    ///
    /// PDFs are passed through untouched rather than scraped for text. That is
    /// deliberate: a CV's meaning lives partly in its layout - columns, section
    /// headings, the grouping of dates against roles - and a text extractor
    /// flattens exactly that. The model accepts PDFs directly and reads the
    /// page as laid out, so extracting first would throw away information and
    /// add a dependency to do it.
    ///
    /// .docx has no such path, so it is unzipped here. A .docx is a zip holding
    /// XML; pulling the text out needs no library, and adding one to save
    /// thirty lines is a dependency to audit and update forever.
    ///
    /// Legacy .doc is refused. It is an undocumented binary format that needs a
    /// real library to read, and it is rare enough among CVs written this
    /// decade that the honest refusal beats the dependency.
    ///
    /// Pure and I/O-free, like <see cref="CvFileValidator"/> next to it, so the
    /// awkward cases are unit-testable without a storage account.
    /// </summary>
    public static class CvContentExtractor
    {
        private static readonly XNamespace W =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        /// <summary>
        /// Where the text of a Word document lives inside the zip. Casing is not
        /// guaranteed by the spec, so the lookup below is case-insensitive.
        /// </summary>
        private const string DocumentPart = "word/document.xml";

        /// <summary>
        /// Roughly 15 pages of dense text. Past this the document is not a CV,
        /// and sending it would spend tokens proportional to someone's mistake.
        /// </summary>
        public const int MaxTextChars = 40_000;

        public static (CvContent? Content, string? Error) Extract(string fileName, byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0)
            {
                return (null, "That CV version has no file attached.");
            }

            var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

            // The extension is a hint; the bytes decide. An uploader that
            // renamed a .doc to .docx should get the accurate refusal rather
            // than a confusing zip error.
            if (LooksLikePdf(bytes))
            {
                return (new CvContent(CvContentKind.Pdf, bytes, null), null);
            }

            if (LooksLikeZip(bytes))
            {
                return ExtractDocx(bytes);
            }

            if (extension == ".doc")
            {
                return (null,
                    "Legacy .doc files cannot be read. Save the CV as PDF or .docx and upload it again.");
            }

            return (null, "That file is not a PDF or a .docx, so its contents could not be read.");
        }

        private static (CvContent? Content, string? Error) ExtractDocx(byte[] bytes)
        {
            try
            {
                using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, DocumentPart, StringComparison.OrdinalIgnoreCase));

                if (entry is null)
                {
                    return (null, "That .docx file has no document contents.");
                }

                using var stream = entry.Open();

                var text = ReadWordText(XDocument.Load(stream));

                return string.IsNullOrWhiteSpace(text)
                    ? (null, "No text could be read from that .docx file.")
                    : (new CvContent(CvContentKind.Text, null, Cap(text)), null);
            }
            catch (InvalidDataException)
            {
                return (null, "That file is damaged and could not be opened.");
            }
        }

        /// <summary>
        /// Paragraph by paragraph, because the alternative - concatenating every
        /// text node in the document - runs the last word of one line into the
        /// first word of the next, and a CV read that way turns job titles and
        /// dates into a single unreadable string.
        /// </summary>
        private static string ReadWordText(XDocument document)
        {
            var builder = new StringBuilder();

            foreach (var paragraph in document.Descendants(W + "p"))
            {
                var line = new StringBuilder();

                foreach (var node in paragraph.Descendants())
                {
                    if (node.Name == W + "t")
                    {
                        line.Append(node.Value);
                    }
                    else if (node.Name == W + "tab")
                    {
                        line.Append('\t');
                    }
                    else if (node.Name == W + "br")
                    {
                        line.Append('\n');
                    }
                }

                builder.Append(line.ToString().TrimEnd()).Append('\n');
            }

            // Word writes an empty paragraph for every blank line, and a CV has
            // many. Collapsing runs of them keeps the structure without paying
            // tokens for the padding.
            return CollapseBlankLines(builder.ToString()).Trim();
        }

        private static string CollapseBlankLines(string text)
        {
            var result = new StringBuilder(text.Length);
            var blanks = 0;

            foreach (var line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (++blanks > 1) continue;

                    result.Append('\n');
                    continue;
                }

                blanks = 0;
                result.Append(line).Append('\n');
            }

            return result.ToString();
        }

        private static string Cap(string text) =>
            text.Length <= MaxTextChars ? text : text[..MaxTextChars];

        /// <summary>"%PDF-"</summary>
        private static bool LooksLikePdf(byte[] bytes) =>
            bytes.Length >= 5 &&
            bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D;

        /// <summary>"PK\x03\x04" - the local file header every .docx starts with.</summary>
        private static bool LooksLikeZip(byte[] bytes) =>
            bytes.Length >= 4 &&
            bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
    }
}
