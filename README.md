# DERAI RAG Assistant

![CI](https://github.com/derai/derai-rag-assistant/actions/workflows/ci.yml/badge.svg)

A .NET 10 reference app for bilingual document Q&A with citations and role based access control. Nordia Logística S.L. and all documents in the corpus are fictional.

## Run locally without Docker

From the repository root, run these three commands:

```bash
dotnet restore Derai.RagAssistant.slnx
dotnet build Derai.RagAssistant.slnx -c Release
dotnet run --project src/Derai.RagAssistant.Api --urls http://localhost:8080
```

Open <http://localhost:8080>. The default Fake model and in-memory index need no credentials.

## Run with Docker Compose

```bash
docker compose up --build -d
docker compose ps
curl http://localhost:8080/health/ready
```

Stop the service with `docker compose down`.

## Screenshots

| UI state | Capture |
|---|---|
| Direction, light theme, English answer with citation | ![Direction chat in light theme](docs/img/chat-direccion.png) |
| Direction, dark theme | ![Direction chat in dark theme](docs/img/chat-direccion-dark.png) |
| Support abstains from the restricted contract | ![Support chat abstention](docs/img/chat-soporte.png) |
| Bilingual retrieval evaluation | ![Quality evaluation](docs/img/calidad.png) |

Regenerate them with `tools/screenshots/run.ps1` on Windows or `bash tools/screenshots/run.sh` on Linux/macOS. Playwright and Chromium are development tools isolated in `tools/screenshots/`.

## Verification status

| Capability | Evidence and actual status |
|---|---|
| .NET 10 Release build with warnings as errors | **Verified locally** — build succeeded with 0 warnings and 0 errors. |
| Unit and API integration tests | **Verified locally** — 22 tests pass, including bilingual ACL, contract citations, and support abstention. |
| Bilingual evaluation | **Verified locally** — Fake provider returned 20/20 expected source hits (10 Spanish, 10 English). This is a fixed fixture check. |
| Health, Swagger, SSE and role ACL | **Verified locally** — health endpoints and Swagger returned 200; SSE emitted `token`, `sources`, `done`; restricted queries returned no sources to support. |
| UI screenshots | **Verified locally** — generated with Playwright Chromium headless; light/dark direction, support, and quality captures are in `docs/img/`. |
| OpenAI, Azure OpenAI, Anthropic and Ollama chat adapters | **Tested with mocked HTTP handlers** — request formats and streamed response parsing. **Not tested against real services.** |
| OpenAI, Azure OpenAI and Ollama embedding adapters | **Tested with mocked HTTP handlers** — request and response parsing. **Not tested against real services.** |
| Azure AI Search adapter | **Tested with mocked HTTP handler** — schema, batch upsert, role/language filters and fallback. **Not tested against a real service.** |
| Docker Compose in this environment | **Not tested against the real service/runtime** — Docker Desktop's Linux engine was unavailable during verification. |

The local API checks used the Fake model, the in-memory store, and demo JWTs. They do not establish production availability or service quality.

## Architecture and behavior

- Clean Architecture projects separate Domain, Application, Infrastructure, and API.
- The 15 logical documents are stored as Spanish and English counterparts under `data/docs/es/` and `data/docs/en/`. Each pair shares `id` and `allowedRoles` front matter and declares its own `language`.
- ACL filtering happens before retrieval. The store first searches the selected language and falls back to the other language only when it has no sufficiently relevant hit. Sources include the source language.
- `POST /api/v1/chat` streams `token`, `sources`, and `done` SSE events. `POST /api/v1/chat/sync` returns a JSON answer with citations.
- The local Fake model is deterministic and abstains when its evidence score is too low. Retrieved document text is treated as untrusted input.
- `GET /api/v1/eval` checks 20 fixed queries and expected source documents. `POST /api/v1/knowledge/reindex` requires `X-Admin-Key`.
- The browser UI supports English/Spanish labels, role selection, citations, evaluation, and light/dark themes.

## Limitations

The Fake hash embedding is **not semantic**. Local bilingual retrieval also uses lexical matching; a real embedding provider is expected to improve semantic retrieval, but that improvement has not been measured against a live provider here. The 20/20 evaluation is a small deterministic fixture, not a general relevance benchmark. Real LLM calls, real embeddings, and a deployed Azure AI Search index remain unverified. Demo JWTs signed with an ephemeral per-process key unless configured, in-memory history, and synthetic documents are not production identity, secret management, persistence, or operational guarantees.

## Configuration

Provider settings use .NET environment variable names such as `Llm__Provider`, `Llm__ApiKey`, `Llm__Endpoint`, `Llm__EmbeddingProvider`, `VectorStore__Provider`, `VectorStore__Endpoint`, `VectorStore__ApiKey`, and `Auth__SigningKey`. Defaults are `Fake` and `Memory`; without `Auth__SigningKey`, the API generates a random process-local signing key. See [the Spanish README](README.es.md) for the localized guide and [contribution instructions](CONTRIBUTING.md).




