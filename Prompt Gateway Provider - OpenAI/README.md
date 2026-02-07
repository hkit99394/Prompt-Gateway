# Prompt Gateway Provider - OpenAI

Worker service that consumes canonical jobs from SQS, loads prompts from S3, calls OpenAI, normalizes output, and publishes results to SQS.

## Build

```bash
dotnet restore
dotnet build
```

## Run (local)

```bash
dotnet run --project "Prompt Gateway Provider - OpenAI/src/Provider.Worker"
```

## Configuration

Update `Prompt Gateway Provider - OpenAI/src/Provider.Worker/appsettings.json` or provide environment variables:

- `ProviderWorker__InputQueueUrl`
- `ProviderWorker__OutputQueueUrl`
- `ProviderWorker__PromptBucket`
- `ProviderWorker__ResultBucket` (optional)
- `ProviderWorker__DedupeTableName` (optional DynamoDB table with `id` (PK) and TTL)
- `ProviderWorker__OpenAi__ApiKey`

## Example job payload

```json
{
  "job_id": "job-123",
  "attempt_id": "attempt-1",
  "task_type": "chat_completion",
  "prompt_s3_key": "prompts/job-123.txt",
  "prompt_s3_bucket": "your-prompt-bucket",
  "system_prompt": "You are a helpful assistant.",
  "model": "gpt-4o-mini",
  "parameters": {
    "temperature": 0.2,
    "max_tokens": 200
  }
}
```
