# Fictional company RAG assistant

.NET 10 reference demo for Spanish and English knowledge Q&A with citations and role based access. The UI shows one sample company: **Nordia Logistics (fictional)**. All financial documents, invoices, sales, and results are invented.

## Run

```bash
dotnet restore Derai.RagAssistant.slnx
dotnet build -c Release
dotnet run --project src/Derai.RagAssistant.Api --urls http://localhost:8080
```

Open <http://localhost:8080>. Fake and the in-memory store need no credentials. Docker Compose also works with `docker compose up --build`.

## Using NVIDIA API

The `OpenAICompatible` provider defaults to `https://integrate.api.nvidia.com/v1` and uses [NVIDIA Chat Completions SSE](https://docs.api.nvidia.com/nim/reference/meta-llama-3_2-1b-instruct-infer). Configure `.env` (never commit it):

```dotenv
LLM_PROVIDER=OpenAICompatible
LLM_OPENAI_COMPATIBLE_API_KEY=
LLM_OPENAI_COMPATIBLE_CHAT_MODEL=meta/llama-3.2-1b-instruct
LLM_OPENAI_COMPATIBLE_EMBEDDING_MODEL=
LLM_MAX_TOKENS=1200
LLM_RESILIENCE_ATTEMPT_TIMEOUT_SECONDS=45
LLM_RESILIENCE_TOTAL_REQUEST_TIMEOUT_SECONDS=180
LLM_RESILIENCE_CIRCUIT_BREAKER_SAMPLING_DURATION_SECONDS=90
LLM_RESILIENCE_MAX_RETRIES=2
```

Resilience settings group per-attempt timeout, total timeout, circuit-breaker sampling window, and retries under `Llm:Resilience`. The total is raised as needed to cover all attempts and their delays; SSE content is read after the headers arrive and is not cut off by the total request timeout.

The key stays in environment configuration and `.env` is Git-ignored. Without an embedding model, search uses Fake embeddings while chat may use NVIDIA. Configured embeddings send `input_type=query` for search and `input_type=passage` for indexing, with `truncate=END` as specified by [NVIDIA Embeddings](https://docs.api.nvidia.com/nim/reference/nvidia-nv-embedqa-e5-v5-infer). Startup indexes the documents; changing models selects a new index with model identifier and output dimension recorded.

**NVIDIA status:** request, stream, and error handling are covered by simulated `HttpMessageHandler` tests. The live API has not been tested with a real key.

## Verification status

| Capability | Evidence and status |
|---|---|
| .NET 10 Release build | Verified: 0 warnings and 0 errors with `dotnet build -c Release -m:1 --no-restore /nodeReuse:false /p:BuildInParallel=false`. |
| Formatting | Verified with `dotnet format --verify-no-changes`. |
| Tests | Verified: 45 .NET tests pass (7 integration, 38 unit), including simulated handlers and Fake ES/EN cases. |
| UI rendering security | Verified with `npm test`; both Markdown rendering and malicious HTML escaping tests pass. |
| Live NVIDIA API | Pending a test with a user key; simulated handler only. |
| Sample data | Nordia Logistics, people, invoices, amounts, and transactions are fictional. |

## Knowledge and API

- The API separates startup (`Program.cs`), configuration and initialization (`Configuration/`), endpoints (`Endpoints/`), authentication (`Security/`), and HTTP-facing services (`Services/`). Domain, application, and infrastructure layers remain in their respective projects.
- `data/docs/nordia/{es,en}/` is the only corpus, with fictional logistics operations and financial data.
- Greeting and suggested questions match this company in Spanish and English. Role based ACLs apply.
- `POST /api/v1/chat?corpus=nordia` streams SSE; `POST /api/v1/chat/sync?corpus=nordia` returns JSON and citations. Corpus is optional; Nordia is the only enabled corpus.
- `GET /api/v1/eval?corpus=nordia` runs 20 questions (10 ES and 10 EN). `POST /api/v1/knowledge/reindex?corpus=nordia` requires `X-Admin-Key`.
- Fake is deterministic for demos and evaluation; it does not replace a real model.
