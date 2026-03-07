# ControlPlane.Api Operations Runbook

## Required configuration

- Configure at least one API key using one of:
  - `ApiSecurity__ApiKeys__0`, `ApiSecurity__ApiKeys__1`, ...
  - `ApiSecurity__ApiKey` (single key fallback)
- Configure DynamoDB storage:
  - `AwsStorage__TableName`
  - `AwsStorage__JobListIndexName` (defaults to `gsi1`, expected key schema: `gsi1pk` + `gsi1sk`)
- Do not store production keys in `appsettings.json`.
- Keep keys in an environment variable source or external secret manager.

## Key rotation procedure

1. Add the new key as an additional value (for example `ApiSecurity__ApiKeys__1`).
2. Deploy and verify clients can authenticate with both old and new keys.
3. Update clients to use the new key.
4. Remove the old key and redeploy.

## Request contract

- Header: `X-API-Key`
- Protected endpoints:
  - `POST /jobs`
  - `GET /jobs`
  - `GET /jobs/{jobId}`
  - `GET /jobs/{jobId}/result`
  - `GET /jobs/{jobId}/events`
  - `GET /jobs/{jobId}/detail`

## Quick verification

```bash
curl -i \
  -H "X-API-Key: $CONTROL_PLANE_API_KEY" \
  "http://localhost:5000/jobs?limit=5"
```

```bash
curl -i \
  -X POST \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $CONTROL_PLANE_API_KEY" \
  -d '{"taskType":"chat_completion"}' \
  "http://localhost:5000/jobs"
```

## Expected response codes

- `200`: request accepted or read operation succeeded
- `400`: invalid request payload (for example missing `taskType`)
- `401`: missing/invalid `X-API-Key`
- `409`: orchestration state conflict (for example concurrent update conflict)

## Troubleshooting

- Startup fails with API key configuration error:
  - Ensure at least one key exists in `ApiSecurity:ApiKeys` or `ApiSecurity:ApiKey`.
- Requests return `401`:
  - Verify `X-API-Key` header is present.
  - Confirm key value exactly matches configured key (case-sensitive).
- Unexpected `409`:
  - Retry the request after reloading current job state.
