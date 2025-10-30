using Microsoft.Extensions.VectorData;

namespace AccedeSimple.Service.Services;

public class SearchService(VectorStore vectorStore)
{
    private readonly VectorStoreCollection<object, Dictionary<string, object?>> _collection =
        vectorStore.GetDynamicCollection("chunks", new VectorStoreCollectionDefinition
        {
            Properties =
            {
                new VectorStoreKeyProperty("key", typeof(string)),
                new VectorStoreVectorProperty("embedding", typeof(string), EmbeddingModel.DIMENSION),
                new VectorStoreDataProperty("content", typeof(string)),
                new VectorStoreDataProperty("context", typeof(string)),
                new VectorStoreDataProperty("documentid", typeof(string)) { IsIndexed = true }
            }
        });

    public async IAsyncEnumerable<string> SearchAsync(string query)
    {
        await foreach (var result in _collection.SearchAsync(query, top: 5))
        {
            // Return the content field from the vector store record
            yield return result.Record["content"]?.ToString() ?? string.Empty;
        }
    }
}