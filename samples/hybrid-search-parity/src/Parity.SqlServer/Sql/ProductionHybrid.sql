
                DECLARE @queryVec vector(1536) = CAST('{vectorJson}' AS vector(1536));
                
                WITH TextScores AS (
                    SELECT 
                        dc.Id,
                        dc.Content,
                        dc.ContentFileId,
                        dc.NotebookFileId,
                        dc.ChunkIndex,
                        dc.IndexName,
                        dc.Embedding,
                        CAST(COALESCE(ft.[RANK], 0) AS FLOAT) / 1000.0 AS TextScore
                    FROM DocumentChunks dc
                    LEFT JOIN CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}', {candidateLimit}) ft 
                        ON dc.Id = ft.[KEY]
                    WHERE dc.IndexName = @indexName
                        AND (@notebookId IS NULL OR dc.NotebookId = @notebookId)
                )
                SELECT TOP (@maxResults)
                    Id AS ChunkId,
                    Content,
                    ContentFileId,
                    NotebookFileId,
                    ChunkIndex,
                    IndexName,
                    CAST((1.0 - vector_distance('cosine', Embedding, @queryVec)) AS FLOAT) AS VectorScore,
                    CAST(TextScore AS FLOAT) AS TextScore,
                    CAST((@vectorWeight * (1.0 - vector_distance('cosine', Embedding, @queryVec))) 
                        + (@textWeight * TextScore) AS FLOAT) AS Score
                FROM TextScores
                WHERE TextScore > 0 OR vector_distance('cosine', Embedding, @queryVec) < 1.0
                ORDER BY Score DESC