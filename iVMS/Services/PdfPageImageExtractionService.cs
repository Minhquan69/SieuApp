using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace V3SClient.Services
{
    public sealed class PdfPageImageExtractionService
    {
        private const double RenderDpi = 220.0;

        public async Task<IReadOnlyList<string>> ExtractPagesAsync(string pdfPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentException("PDF path is empty.", nameof(pdfPath));
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF file not found.", pdfPath);

            string outputDirectory = BuildOutputDirectory(pdfPath);
            Directory.CreateDirectory(outputDirectory);

            var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath).AsTask(cancellationToken).ConfigureAwait(false);
            var document = await PdfDocument.LoadFromFileAsync(storageFile).AsTask(cancellationToken).ConfigureAwait(false);
            var outputFiles = new List<string>();

            for (uint pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var page = document.GetPage(pageIndex))
                using (var stream = new InMemoryRandomAccessStream())
                {
                    var options = new PdfPageRenderOptions();
                    uint width = (uint)Math.Max(1, Math.Round(page.Size.Width * RenderDpi / 96.0));
                    options.DestinationWidth = width;

                    await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken).ConfigureAwait(false);
                    stream.Seek(0);

                    string outputPath = Path.Combine(
                        outputDirectory,
                        $"{Path.GetFileNameWithoutExtension(pdfPath)}_page_{pageIndex + 1:000}.png");
                    using (var input = stream.AsStreamForRead())
                    using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await input.CopyToAsync(output, 1024 * 128, cancellationToken).ConfigureAwait(false);
                    }

                    outputFiles.Add(outputPath);
                }
            }

            return outputFiles;
        }

        private static string BuildOutputDirectory(string pdfPath)
        {
            string parent = Path.GetDirectoryName(pdfPath);
            string name = Path.GetFileNameWithoutExtension(pdfPath);
            if (string.IsNullOrWhiteSpace(parent))
                parent = Environment.CurrentDirectory;

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidChar, '_');
            }

            return Path.Combine(parent, name + "_pdf_pages");
        }
    }
}
