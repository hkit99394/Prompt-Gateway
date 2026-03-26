# provider-execution

## Mission

Own provider-facing behavior so OpenAI execution remains correct across ECS, Lambda, and hybrid runtime phases.

## Use When

- OpenAI timeout or retry behavior changes
- Prompt execution parameters change
- Provider-specific error mapping changes
- Usage, payload, or result shaping changes

## Owns

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/OpenAiClient.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/PromptLoader.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/PromptBuilder.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/ResultPayloadStore.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Models/`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Options/`
- `Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/`

## Avoid

- Owning SQS event source mapping
- Owning Terraform runtime resources

## Handoff

Provide a handler-friendly execution flow with clear timeout, retry, and payload-storage behavior for `async-event-processing` and `lambda-platform`.

## Success Checks

- Provider calls honor configured limits
- Retry behavior is explicit and tested
- Payload storage and result publication semantics remain stable
