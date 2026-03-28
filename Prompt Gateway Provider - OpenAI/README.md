# Prompt Gateway Provider - OpenAI

Worker service that consumes canonical jobs from SQS, loads prompts from S3, calls OpenAI, normalizes output, and publishes results to SQS.

## Architecture

```
SQS (input) -> Worker -> S3 (prompt) -> OpenAI -> SQS (output)
                    \-> S3 (large payloads) ->/
                    \-> DynamoDB (dedupe, optional)
```

## Build

```bash
dotnet restore
dotnet build
```

## Run (local)

```bash
dotnet run --project "Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host"
```

## Deployment

1. Build a release artifact:
   ```bash
   dotnet publish "Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host" -c Release -o out
   ```
2. Provide configuration via environment variables (see Configuration section).
3. Run the service:
   ```bash
   dotnet "out/Provider.Worker.Host.dll"
   ```

## Configuration

Update `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host/appsettings.json` or provide environment variables:

- `ProviderWorker__InputQueueUrl`
- `ProviderWorker__OutputQueueUrl`
- `ProviderWorker__PromptBucket`
- `ProviderWorker__ResultBucket` (optional)
- `ProviderWorker__DedupeTableName` (optional DynamoDB table with `id` (PK) and TTL)
- `ProviderWorker__OpenAi__ApiKey`
- `ProviderWorker__OpenAi__TimeoutSeconds`
- `ProviderWorker__OpenAiRetryMaxAttempts`
- `ProviderWorker__OpenAiRetryMaxBackoffSeconds`
- `ProviderWorker__ExecutionTimeoutSeconds` (Lambda/runtime budget guard)
- `ProviderWorker__VisibilityTimeoutSeconds` (queue visibility budget guard)

Notes:
- Startup validates required settings and fails fast if they are missing.
- Prompt bucket overrides are blocked unless they match the configured `ProviderWorker__PromptBucket`.

## Tuning rules

- The provider Lambda currently processes SQS records sequentially within an invocation, so `provider_lambda_batch_size` should stay small. The default is now `1`.
- Worst-case invocation window is derived from:
  - `MaxMessages * ((OpenAiRetryMaxAttempts * OpenAi.TimeoutSeconds) + retry backoff budget + ProcessingOverheadBufferSeconds)`
- In Lambda mode:
  - `ProviderWorker__ExecutionTimeoutSeconds` must be at least that worst-case invocation window.
  - `ProviderWorker__VisibilityTimeoutSeconds` must be at least that same window.
- Reserved concurrency is the primary cap on parallel OpenAI calls in Lambda mode. Increase it deliberately alongside provider rate-limit and budget expectations.

## Example job payload

```json
{
  "job_id": "job-123",
  "attempt_id": "attempt-1",
  "task_type": "chat_completion",
  "prompt_key": "prompts/job-123.txt",
  "prompt_bucket": "your-prompt-bucket",
  "system_prompt": "You are a helpful assistant.",
  "model": "gpt-4o-mini",
  "parameters": {
    "temperature": 0.2,
    "max_tokens": 200
  }
}
```
