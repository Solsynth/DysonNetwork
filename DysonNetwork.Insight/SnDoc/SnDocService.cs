using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text;

namespace DysonNetwork.Insight.SnDoc;

public class SnDocService(
    AppDatabase database,
    EmbeddingService embeddingService,
    ILogger<SnDocService> logger
)
{
    private const int DefaultChunkSize = 1000; // characters per chunk
    private const int DefaultChunkOverlap = 200; // overlap between chunks
    private const int DefaultEmbeddingLimit = 1536; // token limit for embedding

    /// <summary>
    /// Creates or updates a documentation page with automatic chunking.
    /// </summary>
    public async Task<SnDocPage> UpsertPageAsync(
        string slug,
        string title,
        string? description,
        string content,
        CancellationToken ct = default)
    {
        // Normalize slug
        slug = slug.ToLowerInvariant().Trim();

        // Check if page exists
        var existingPage = await database.SnDocPages
            .AsTracking()
            .Include(p => p.Chunks)
            .FirstOrDefaultAsync(p => p.Slug == slug && p.DeletedAt == null, ct);

        if (existingPage != null)
        {
            logger.LogInformation("Updating existing doc page: {Slug}", slug);

            // Soft delete existing chunks
            foreach (var chunk in existingPage.Chunks)
            {
                chunk.DeletedAt = SystemClock.Instance.GetCurrentInstant();
            }

            // Update page metadata
            existingPage.Title = title;
            existingPage.Description = description;
            existingPage.Content = content;
            existingPage.ContentLength = content.Length;
            existingPage.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

            // Re-chunk and create new embeddings
            var chunks = await CreateChunksAsync(existingPage.Id, title, description, content, ct);
            existingPage.Chunks = chunks;
            existingPage.ChunkCount = chunks.Count;

            await database.SaveChangesAsync(ct);
            return existingPage;
        }
        else
        {
            logger.LogInformation("Creating new doc page: {Slug}", slug);

            // Create new page
            var page = new SnDocPage
            {
                Slug = slug,
                Title = title,
                Description = description,
                Content = content,
                ContentLength = content.Length,
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            // Create chunks with embeddings
            var chunks = await CreateChunksAsync(page.Id, title, description, content, ct);
            page.Chunks = chunks;
            page.ChunkCount = chunks.Count;

            database.SnDocPages.Add(page);
            await database.SaveChangesAsync(ct);

            return page;
        }
    }

    /// <summary>
    /// Performs semantic search across documentation chunks.
    /// Returns the matching page IDs with relevance scores.
    /// </summary>
    public async Task<List<SnDocSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        double? minSimilarity = 0.6,
        CancellationToken ct = default)
    {
        // Generate query embedding
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, ct);
        if (queryEmbedding == null)
        {
            logger.LogWarning("Failed to generate embedding for query: {Query}", query);
            return [];
        }

        // Search for similar chunks using cosine distance
        var results = await database.SnDocChunks
            .AsNoTracking()
            .Where(c => c.DeletedAt == null && c.Embedding != null)
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding!.CosineDistance(queryEmbedding)
            })
            .OrderBy(x => x.Distance)
            .Take(limit * 3) // Take more than needed for deduplication
            .ToListAsync(ct);

        // Group by page and take best match per page
        var pageResults = results
            .GroupBy(x => x.Chunk.PageId)
            .Select(g => new
            {
                PageId = g.Key,
                BestChunk = g.OrderBy(x => x.Distance).First(),
                RelevantChunks = g.OrderBy(x => x.Distance).Take(3).ToList()
            })
            .Select(x => new SnDocSearchResult
            {
                PageId = x.PageId,
                Similarity = 1.0 - x.BestChunk.Distance,
                BestChunkIndex = x.BestChunk.Chunk.ChunkIndex,
                RelevantChunkIndices = x.RelevantChunks.Select(c => c.Chunk.ChunkIndex).ToList()
            })
            .Where(r => !minSimilarity.HasValue || r.Similarity >= minSimilarity.Value)
            .OrderByDescending(r => r.Similarity)
            .Take(limit)
            .ToList();

        return pageResults;
    }

    /// <summary>
    /// Retrieves a documentation page by its slug.
    /// </summary>
    public async Task<SnDocPage?> GetPageBySlugAsync(string slug, CancellationToken ct = default)
    {
        return await database.SnDocPages
            .AsNoTracking()
            .Include(p => p.Chunks.Where(c => c.DeletedAt == null).OrderBy(c => c.ChunkIndex))
            .FirstOrDefaultAsync(p => p.Slug == slug.ToLowerInvariant() && p.DeletedAt == null, ct);
    }

    /// <summary>
    /// Retrieves a documentation page by its ID.
    /// </summary>
    public async Task<SnDocPage?> GetPageByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await database.SnDocPages
            .AsNoTracking()
            .Include(p => p.Chunks.Where(c => c.DeletedAt == null).OrderBy(c => c.ChunkIndex))
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
    }

    /// <summary>
    /// Reads a page's content with pagination support.
    /// Returns the requested portion of the content.
    /// </summary>
    public async Task<SnDocPageContent?> ReadPageAsync(
        Guid pageId,
        int? offset = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var page = await database.SnDocPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.DeletedAt == null, ct);

        if (page == null) return null;

        var content = page.Content;
        var totalLength = content.Length;

        // Apply pagination
        var startOffset = offset ?? 0;
        var length = take ?? totalLength;

        if (startOffset < 0) startOffset = 0;
        if (startOffset >= totalLength) startOffset = totalLength;
        if (length < 0) length = 0;
        if (startOffset + length > totalLength) length = totalLength - startOffset;

        var paginatedContent = content.Substring(startOffset, length);

        return new SnDocPageContent
        {
            PageId = page.Id,
            Slug = page.Slug,
            Title = page.Title,
            Description = page.Description,
            Content = paginatedContent,
            TotalLength = totalLength,
            Offset = startOffset,
            Taken = length,
            HasMore = startOffset + length < totalLength,
            ChunkCount = page.ChunkCount
        };
    }

    /// <summary>
    /// Soft deletes a documentation page.
    /// </summary>
    public async Task<bool> DeletePageAsync(string slug, CancellationToken ct = default)
    {
        var page = await database.SnDocPages
            .AsTracking()
            .Include(p => p.Chunks)
            .FirstOrDefaultAsync(p => p.Slug == slug.ToLowerInvariant() && p.DeletedAt == null, ct);

        if (page == null) return false;

        var now = SystemClock.Instance.GetCurrentInstant();

        page.DeletedAt = now;
        foreach (var chunk in page.Chunks)
        {
            chunk.DeletedAt = now;
        }

        await database.SaveChangesAsync(ct);
        logger.LogInformation("Deleted doc page: {Slug}", slug);

        return true;
    }

    /// <summary>
    /// Lists all active documentation pages (basic info only, no content).
    /// </summary>
    public async Task<List<SnDocPageSummary>> ListPagesAsync(
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        var query = database.SnDocPages
            .AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Title);

        if (offset.HasValue)
        {
            query = (IOrderedQueryable<SnDocPage>)query.Skip(offset.Value);
        }

        if (limit.HasValue)
        {
            query = (IOrderedQueryable<SnDocPage>)query.Take(limit.Value);
        }

        return await query
            .Select(p => new SnDocPageSummary
            {
                Id = p.Id,
                Slug = p.Slug,
                Title = p.Title,
                Description = p.Description,
                ContentLength = p.ContentLength,
                ChunkCount = p.ChunkCount,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Creates chunks from the page content and generates embeddings.
    /// </summary>
    private async Task<List<SnDocChunk>> CreateChunksAsync(
        Guid pageId,
        string title,
        string? description,
        string content,
        CancellationToken ct)
    {
        var chunks = new List<SnDocChunk>();
        var now = SystemClock.Instance.GetCurrentInstant();

        // Prepare the header that will be prepended to each chunk
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"Title: {title}\n");
        if (!string.IsNullOrEmpty(description))
        {
            headerBuilder.Append($"Description: {description}\n");
        }
        headerBuilder.Append("Content:\n");
        var header = headerBuilder.ToString();
        var headerLength = header.Length;

        // Chunk the content
        var contentChunks = ChunkContent(content, DefaultChunkSize, DefaultChunkOverlap);

        // Prepare chunk texts for batch embedding
        var chunkTexts = contentChunks.Select((c, i) =>
        {
            var chunkHeader = i == 0 ? header : $"Title: {title}\nContent (continued):\n";
            return chunkHeader + c.Content;
        }).ToList();

        // Generate embeddings in batch
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(chunkTexts, ct);

        for (int i = 0; i < contentChunks.Count; i++)
        {
            var chunkData = contentChunks[i];
            var embedding = embeddings.Count > i ? embeddings[i] : null;

            if (embedding == null)
            {
                logger.LogWarning("Failed to generate embedding for chunk {Index} of page {PageId}", i, pageId);
            }

            var chunk = new SnDocChunk
            {
                PageId = pageId,
                ChunkIndex = i,
                Content = chunkData.Content,
                StartOffset = chunkData.StartOffset,
                EndOffset = chunkData.EndOffset,
                Embedding = embedding,
                IsFirstChunk = i == 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            chunks.Add(chunk);
        }

        logger.LogInformation("Created {ChunkCount} chunks for page {PageId}", chunks.Count, pageId);
        return chunks;
    }

    /// <summary>
    /// Splits content into overlapping chunks.
    /// </summary>
    private List<ContentChunk> ChunkContent(string content, int chunkSize, int overlap)
    {
        var chunks = new List<ContentChunk>();
        var contentLength = content.Length;

        if (contentLength <= chunkSize)
        {
            // No need to chunk
            chunks.Add(new ContentChunk
            {
                Content = content,
                StartOffset = 0,
                EndOffset = contentLength
            });
            return chunks;
        }

        var start = 0;
        while (start < contentLength)
        {
            var end = Math.Min(start + chunkSize, contentLength);

            // Try to find a better break point (end of sentence/paragraph)
            if (end < contentLength)
            {
                // Look for paragraph break
                var paragraphBreak = content.LastIndexOf("\n\n", end, end - start);
                if (paragraphBreak > start)
                {
                    end = paragraphBreak + 2;
                }
                else
                {
                    // Look for sentence break
                    var sentenceBreak = content.LastIndexOfAny(['.', '!', '?', '\n'], end - 1, Math.Min(100, end - start));
                    if (sentenceBreak > start)
                    {
                        end = sentenceBreak + 1;
                    }
                }
            }

            chunks.Add(new ContentChunk
            {
                Content = content[start..end].Trim(),
                StartOffset = start,
                EndOffset = end
            });

            // Move start with overlap
            start = end - overlap;
            if (start >= end) start = end; // Safety check
        }

        return chunks;
    }

    private class ContentChunk
    {
        public string Content { get; set; } = null!;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }
}

/// <summary>
/// Result of a semantic search operation.
/// </summary>
public class SnDocSearchResult
{
    public Guid PageId { get; set; }
    public double Similarity { get; set; }
    public int BestChunkIndex { get; set; }
    public List<int> RelevantChunkIndices { get; set; } = [];
}

/// <summary>
/// Summary information about a doc page (for listing).
/// </summary>
public class SnDocPageSummary
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int ContentLength { get; set; }
    public int ChunkCount { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
}

/// <summary>
/// Paginated content of a doc page.
/// </summary>
public class SnDocPageContent
{
    public Guid PageId { get; set; }
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Content { get; set; } = null!;
    public int TotalLength { get; set; }
    public int Offset { get; set; }
    public int Taken { get; set; }
    public bool HasMore { get; set; }
    public int ChunkCount { get; set; }
}
