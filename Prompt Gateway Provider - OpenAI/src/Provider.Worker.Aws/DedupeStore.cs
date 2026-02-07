using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

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

    public async Task<DedupeDecision> TryStartAsync(
        string jobId,
        string attemptId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DedupeTableName))
        {
            return _memoryStore.TryStart(jobId, attemptId);
        }

        var id = $"{jobId}#{attemptId}";
        var now = DateTimeOffset.UtcNow;
        var ttl = now.AddMinutes(_options.DedupeMemoryTtlMinutes).ToUnixTimeSeconds();

        var request = new PutItemRequest
        {
            TableName = _options.DedupeTableName,
            ConditionExpression = "attribute_not_exists(id) OR expires_at < :now",
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id },
                ["job_id"] = new AttributeValue { S = jobId },
                ["attempt_id"] = new AttributeValue { S = attemptId },
                ["expires_at"] = new AttributeValue { N = ttl.ToString() },
                ["status"] = new AttributeValue { S = "in_progress" }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":now"] = new AttributeValue { N = now.ToUnixTimeSeconds().ToString() }
            }
        };

        try
        {
            await _dynamoDb.PutItemAsync(request, cancellationToken);
            return DedupeDecision.Started;
        }
        catch (ConditionalCheckFailedException)
        {
            return await GetDecisionAsync(id, cancellationToken);
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
            UpdateExpression = "SET expires_at = :ttl, #status = :status",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ttl"] = new AttributeValue { N = ttl.ToString() },
                [":status"] = new AttributeValue { S = "completed" }
            },
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "status"
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

    private async Task<DedupeDecision> GetDecisionAsync(string id, CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = _options.DedupeTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id }
            },
            ConsistentRead = true
        };

        try
        {
            var response = await _dynamoDb.GetItemAsync(request, cancellationToken);
            if (response.Item.Count == 0)
            {
                return DedupeDecision.DuplicateInProgress;
            }

            if (response.Item.TryGetValue("status", out var status) &&
                string.Equals(status.S, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return DedupeDecision.DuplicateCompleted;
            }

            return DedupeDecision.DuplicateInProgress;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read dedupe status.");
            return DedupeDecision.DuplicateInProgress;
        }
    }

    private sealed class MemoryDedupeStore(int ttlMinutes)
    {
        private sealed record Entry(DateTimeOffset ExpiresAt, bool Completed);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();
        private readonly TimeSpan _ttl = TimeSpan.FromMinutes(Math.Max(5, ttlMinutes));

        public DedupeDecision TryStart(string jobId, string attemptId)
        {
            CleanupExpired();
            var key = $"{jobId}#{attemptId}";
            var now = DateTimeOffset.UtcNow;
            var entry = new Entry(now.Add(_ttl), false);

            if (_entries.TryAdd(key, entry))
            {
                return DedupeDecision.Started;
            }

            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.Completed)
                {
                    return DedupeDecision.DuplicateCompleted;
                }

                return DedupeDecision.DuplicateInProgress;
            }

            return DedupeDecision.DuplicateInProgress;
        }

        public void MarkCompleted(string jobId, string attemptId)
        {
            var key = $"{jobId}#{attemptId}";
            _entries[key] = new Entry(DateTimeOffset.UtcNow.Add(_ttl), true);
        }

        private void CleanupExpired()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _entries)
            {
                if (entry.Value.ExpiresAt <= now)
                {
                    _entries.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}
