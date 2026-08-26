namespace MiFuturoTrabajo.Api.Services
{
    /// <param name="IsValid">Whether the file may be stored.</param>
    /// <param name="Error">Why not, phrased for the person who chose the file.</param>
    /// <param name="Extension">Lowercased extension, including the dot.</param>
    /// <param name="ContentType">The content type to store, derived from the bytes rather than from the client.</param>
    public record CvFileValidation(bool IsValid, string? Error, string Extension = "", string ContentType = "")
    {
        public static CvFileValidation Rejected(string error) => new(false, error);
        public static CvFileValidation Accepted(string extension, string contentType) => new(true, null, extension, contentType);
    }

    /// <summary>
    /// Decides whether an uploaded file is a CV we are willing to store.
    ///
    /// Nothing the client asserts is trusted: the browser's declared content
    /// type is trivially forged, and the extension is just the tail of a string
    /// the user typed. So the extension picks the *candidate* format and the
    /// leading bytes have to agree with it. A file called cv.pdf whose bytes
    /// are a Windows executable is rejected here rather than served back to
    /// whoever clicks download later.
    ///
    /// Deliberately static and free of I/O so the rules can be unit-tested
    /// without a web host, a stream, or a storage account.
    /// </summary>
    public static class CvFileValidator
    {
        public const long MaxSizeBytes = 5 * 1024 * 1024;

        /// <summary>Enough bytes for the longest signature below.</summary>
        public const int HeaderBytesNeeded = 8;

        private static readonly string[] AllowedExtensions = { ".pdf", ".doc", ".docx" };

        // %PDF-
        private static readonly byte[] PdfSignature = { 0x25, 0x50, 0x44, 0x46, 0x2D };

        // Local file header of a zip archive. .docx is OOXML, which is a zip.
        private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };

        // OLE2 compound file, the container legacy .doc uses.
        private static readonly byte[] Ole2Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        public static CvFileValidation Validate(string? fileName, long sizeBytes, byte[] header)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return CvFileValidation.Rejected("The file has no name.");
            }

            if (sizeBytes <= 0)
            {
                return CvFileValidation.Rejected("The file is empty.");
            }

            if (sizeBytes > MaxSizeBytes)
            {
                return CvFileValidation.Rejected(
                    $"The file is {Describe(sizeBytes)}. The limit is {Describe(MaxSizeBytes)}.");
            }

            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(extension))
            {
                return CvFileValidation.Rejected(
                    $"'{extension}' files are not accepted. Upload a PDF, DOC or DOCX.");
            }

            return extension switch
            {
                ".pdf" when StartsWith(header, PdfSignature)
                    => CvFileValidation.Accepted(extension, "application/pdf"),

                ".docx" when StartsWith(header, ZipSignature)
                    => CvFileValidation.Accepted(extension,
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),

                // Word 2007+ can still be saved with a .doc extension, in which
                // case the bytes are OOXML rather than OLE2. Both are genuinely
                // Word documents, so both are accepted here.
                ".doc" when StartsWith(header, Ole2Signature) || StartsWith(header, ZipSignature)
                    => CvFileValidation.Accepted(extension, "application/msword"),

                _ => CvFileValidation.Rejected(
                    $"The file does not look like a {extension.TrimStart('.').ToUpperInvariant()} document. " +
                    "Renaming a file does not change its format.")
            };
        }

        private static bool StartsWith(byte[] header, byte[] signature)
        {
            if (header.Length < signature.Length) return false;

            for (var i = 0; i < signature.Length; i++)
            {
                if (header[i] != signature[i]) return false;
            }

            return true;
        }

        private static string Describe(long bytes) =>
            bytes >= 1024 * 1024
                ? $"{bytes / (double)(1024 * 1024):0.#} MB"
                : $"{bytes / 1024d:0.#} KB";
    }
}
