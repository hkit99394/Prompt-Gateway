using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IDedupeStore
{
    Task<bool> TryStartAsync(string jobId, string attemptId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string jobId, string attemptId, CancellationToken cancellationToken);
}

public class DedupeStore : IDedupeStore
{
    private readonly ILogger<DedupeStore> _logger;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ProviderWorkerOptions _options;
    private readonly MemoryDedupeStore _memoryStore;

    public DedupeStore(
        ILogger<DedupeStore> logger,
        IAmazonDynamoDB dynamoDb,
        IOptions<ProviderWorkerOptions> options)
    {
        _logger = logger;
        _dynamoDb = dynamoDb;
        _options = options.Value;
        _memoryStore = new MemoryDedupeStore(_options.DedupeMemoryTtlMinutes);
    }

    public async Task<bool> TryStartAsync(string jobId, string attemptId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DedupeTableName))
        {
            return _memoryStore.TryStart(jobId, attemptId);
        }

        var id = $"{jobId}#{attemptId}";
        var ttl = DateTimeOffset.UtcNow.AddMinutes(_options.DedupeMemoryTtlMinutes).ToUnixTimeSeconds();

        var request = new PutItemRequest
        {
            TableName = _options.DedupeTableName,
            ConditionExpression = "attribute_not_exists(id)",
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id },
                ["job_id"] = new AttributeValue { S = jobId },
                ["attempt_id"] = new AttributeValue { S = attemptId },
                ["expires_at"] = new AttributeValue { N = ttl.ToString() }
            }
        };

        try
        {
            await _dynamoDb.PutItemAsync(request, cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dedupe table access failed. Falling back to memory.");
            return _memoryStore.TryStart(jobId, attemptId);
        }
    }

    public async Task MarkCompletedAsync(string jobId, string attemptId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DedupeTableName))
        {
            _memoryStore.MarkCompleted(jobId, attemptId);
            return;
        }

        var id = $"{jobId}#{attemptId}";
        var ttl = DateTimeOffset.UtcNow.AddMinutes(_options.DedupeMemoryTtlMinutes).ToUnixTimeSeconds();

        var request = new UpdateItemRequest
        {
            TableName = _options.DedupeTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id }
            },
            UpdateExpression = "SET expires_at = :ttl",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ttl"] = new AttributeValue { N = ttl.ToString() }
            }
        };

        try
        {
            await _dynamoDb.UpdateItemAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update dedupe completion state.");
            _memoryStore.MarkCompleted(jobId, attemptId);
        }
    }

    private sealed class MemoryDedupeStore(int ttlMinutes)
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new();
        private readonly TimeSpan _ttl = TimeSpan.FromMinutes(Math.Max(5, ttlMinutes));

        public bool TryStart(string jobId, string attemptId)
        {
            CleanupExpired();
            var key = $"{jobId}#{attemptId}";
            return _entries.TryAdd(key, DateTimeOffset.UtcNow.Add(_ttl));
        }

        public void MarkCompleted(string jobId, string attemptId)
        {
            var key = $"{jobId}#{attemptId}";
            _entries[key] = DateTimeOffset.UtcNow.Add(_ttl);
        }

        private void CleanupExpired()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _entries)
            {
                if (entry.Value <= now)
                {
                    _entries.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}
