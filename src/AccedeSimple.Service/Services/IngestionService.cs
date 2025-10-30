using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;

namespace AccedeSimple.Service.Services;

public class IngestionService(
    VectorStore vectorStore,
    ILoggerFactory loggerFactory,
    PdfPigReader reader,
    Tokenizer tokenizer)
{
    public async Task IngestAsync(string sourceDirectory)
    {
        // Create chunker with injected tokenizer
        var chunkerOptions = new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = 200,
            OverlapTokens = 0
        };
        var chunker = new DocumentTokenChunker(chunkerOptions);

        // Create writer. dimensionCount must match the model that is being used to generate embeddings.
        using var writer = new VectorStoreWriter<string>(vectorStore, dimensionCount: EmbeddingModel.DIMENSION);

        // Create and configure the pipeline
        using var pipeline = new IngestionPipeline<string>(reader, chunker, writer, loggerFactory: loggerFactory);

        // Add document processors to clean up the documents
        pipeline.DocumentProcessors.Add(RemovalProcessor.EmptySections);

        // Process all PDF files in the directory
        await pipeline.ProcessAsync(new DirectoryInfo(sourceDirectory), "*.pdf");
    }
}