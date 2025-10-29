using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;

namespace AccedeSimple.Service.Services;

public class IngestionService
{
    private readonly VectorStoreCollection<int, Document> _collection;
    private readonly ILogger<IngestionService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PdfPigReader _reader;
    private readonly Tokenizer _tokenizer;

    public IngestionService(
        VectorStoreCollection<int, Document> collection,
        ILogger<IngestionService> logger,
        ILoggerFactory loggerFactory,
        PdfPigReader reader,
        Tokenizer tokenizer)
    {
        _collection = collection;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _reader = reader;
        _tokenizer = tokenizer;
    }

    public async Task IngestAsync(string sourceDirectory)
    {
        await _collection.EnsureCollectionExistsAsync();
        var sourceFiles = Directory.GetFiles(sourceDirectory, "*.pdf")
            .Select(f => new FileInfo(f))
            .ToArray();

        if (sourceFiles.Length == 0)
        {
            _logger.LogWarning("No PDF files found in {SourceDirectory}", sourceDirectory);
            return;
        }

        // Create chunker with injected tokenizer
        var chunkerOptions = new IngestionChunkerOptions(_tokenizer)
        {
            MaxTokensPerChunk = 200,
            OverlapTokens = 0
        };
        var chunker = new DocumentTokenChunker(chunkerOptions);

        // Create writer
        using var writer = new DocumentWriter(_collection, _logger);

        // Create and configure the pipeline
        using var pipeline = new IngestionPipeline<string>(_reader, chunker, writer, loggerFactory: _loggerFactory);

        // Add document processors to clean up the documents
        pipeline.DocumentProcessors.Add(RemovalProcessor.EmptySections);

        // Process all files
        await pipeline.ProcessAsync(sourceFiles);
    }
}

/// <summary>
/// Custom writer that maps IngestionChunk to Document records
/// </summary>
internal sealed class DocumentWriter : IngestionChunkWriter<string>
{
    private readonly VectorStoreCollection<int, Document> _collection;
    private readonly ILogger _logger;
    private int _currentId = 0;

    public DocumentWriter(VectorStoreCollection<int, Document> collection, ILogger logger)
    {
        _collection = collection;
        _logger = logger;
    }

    public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk<string>> chunks, CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            // Extract metadata from the chunk
            var fileName = Path.GetFileName(chunk.Document.Identifier);

            // Try to get page number from metadata if available
            int pageNumber = 0;
            int indexOnPage = 0;

            if (chunk.HasMetadata)
            {
                if (chunk.Metadata.TryGetValue("PageNumber", out var pageObj) && pageObj is int page)
                {
                    pageNumber = page;
                }
                if (chunk.Metadata.TryGetValue("IndexOnPage", out var indexObj) && indexObj is int index)
                {
                    indexOnPage = index;
                }
            }

            var document = new Document
            {
                Id = _currentId++,
                FileName = fileName,
                PageNumber = pageNumber,
                IndexOnPage = indexOnPage,
                Text = chunk.Content,
                Embedding = chunk.Content
            };

            documents.Add(document);
        }

        if (documents.Count > 0)
        {
            _logger.LogInformation("Upserting {Count} document chunks", documents.Count);
            await _collection.UpsertAsync(documents, cancellationToken);
        }
    }
}

public record class Document
{
    [VectorStoreKey]
    public int Id { get; set; }

    [VectorStoreData]
    public required string FileName { get; set; }

    [VectorStoreData]
    public int PageNumber { get; set; }

    [VectorStoreData]
    public int IndexOnPage { get; set; }

    [VectorStoreData]
    public string? Text { get; set; }

    [VectorStoreVector(Dimensions: 1536)]
    public string? Embedding { get; set; }
}