using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhatsAppBot.Services.Learning
{
    public interface IEmbeddingService
    {
        Task<float[]> GetEmbeddingAsync(string text);
        double CosineSimilarity(float[] a, float[] b);
        Task<List<(int Id, double Score)>> FindSimilarAsync(
            string query, List<(int Id, float[] Embedding)> candidates, int topK = 5);
    }
}