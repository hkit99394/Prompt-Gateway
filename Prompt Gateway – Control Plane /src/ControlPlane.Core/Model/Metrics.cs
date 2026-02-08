namespace ControlPlane.Core;

public sealed record UsageMetrics(int PromptTokens, int CompletionTokens, int TotalTokens);

public sealed record CostMetrics(decimal Amount, string Currency, bool IsEstimated);
