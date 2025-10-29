using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AccedeSimple.Service.Services;

/// <summary>
/// Custom PDF reader using PdfPig that implements IngestionDocumentReader
/// </summary>
public class PdfPigReader : IngestionDocumentReader
{
    public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        using var pdf = PdfDocument.Open(source);
        var document = new IngestionDocument(identifier);

        foreach (var page in pdf.GetPages())
        {
            var section = new IngestionDocumentSection();

            // Extract text blocks from the page
            var letters = page.Letters;
            var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
            var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

            foreach (var textBlock in textBlocks)
            {
                var text = textBlock.Text.ReplaceLineEndings(" ");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    section.Elements.Add(new IngestionDocumentParagraph(text));
                }
            }

            // Add page number metadata
            if (section.Elements.Count > 0)
            {
                document.Sections.Add(section);
            }
        }

        return Task.FromResult(document);
    }
}
