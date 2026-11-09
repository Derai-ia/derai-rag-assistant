# Asistente RAG de empresa ficticia

Demo de referencia en .NET 10 para consultar conocimiento en español e inglés, con citas y permisos por rol. La interfaz muestra una sola empresa de muestra: **Nordia Logística (ficticia)**. Los documentos financieros, facturas, ventas y resultados son inventados.

## Ejecutar

```bash
dotnet restore Derai.RagAssistant.slnx
dotnet build -c Release
dotnet run --project src/Derai.RagAssistant.Api --urls http://localhost:8080
```

Abre <http://localhost:8080>. Fake y el almacenamiento en memoria no requieren credenciales. También puedes usar `docker compose up --build`.

## Usar la API de NVIDIA

El proveedor `OpenAICompatible` usa por defecto `https://integrate.api.nvidia.com/v1` y [Chat Completions de NVIDIA con SSE](https://docs.api.nvidia.com/nim/reference/meta-llama-3_2-1b-instruct-infer). Configura en `.env` (sin subirlo al repositorio):

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

La configuración agrupa timeout por intento, timeout total, ventana del circuit breaker y reintentos bajo `Llm:Resilience`. El total se ajusta para abarcar los intentos y sus pausas; el stream SSE se lee después de recibir las cabeceras, sin quedar limitado por ese timeout total.

La clave se mantiene en variables de entorno y `.env` está ignorado por Git. Sin modelo de embeddings, la búsqueda usa Fake aunque el chat utilice NVIDIA. Si se configura, las consultas envían `input_type=query` y la indexación `input_type=passage`, con `truncate=END`, según [NVIDIA Embeddings](https://docs.api.nvidia.com/nim/reference/nvidia-nv-embedqa-e5-v5-infer). Al arrancar se indexan los documentos; al cambiar el modelo se usa un índice nuevo con el identificador y la dimensión de salida registrados.

**Estado NVIDIA:** formato, stream y errores están cubiertos con `HttpMessageHandler` simulado. No se ha probado con una clave real.

## Capturas

| Vista | Captura |
|---|---|
| Consulta con acceso de dirección | ![Consulta de muestra](docs/img/chat-direccion.png) |
| Tema oscuro | ![Tema oscuro](docs/img/chat-direccion-dark.png) |
| Permisos del rol de soporte | ![Permisos](docs/img/chat-soporte.png) |
| Evaluación | ![Evaluación](docs/img/calidad.png) |

## Estado verificado

| Capacidad | Evidencia y estado |
|---|---|
| Build Release .NET 10 | Verificado: 0 advertencias y 0 errores con `dotnet build -c Release -m:1 --no-restore /nodeReuse:false /p:BuildInParallel=false`. |
| Formato | Verificado con `dotnet format --verify-no-changes`. |
| Tests | Verificado: pasan 45 tests .NET (7 de integración, 38 unitarios), incluidos handlers simulados y casos Fake ES/EN. |
| Seguridad de UI | Verificado con `npm test`; pasan los tests de Markdown y escape de HTML malicioso. |
| NVIDIA real | Pendiente de prueba con una clave del usuario; solo handler simulado. |
| Datos | Nordia Logística, personas, facturas, importes y operaciones son ficticios. |

## Conocimiento y API

- La API separa el arranque (`Program.cs`), configuración e inicialización (`Configuration/`), endpoints (`Endpoints/`), autenticación (`Security/`) y servicios HTTP (`Services/`). Las capas de dominio, aplicación e infraestructura permanecen en sus proyectos.
- `data/docs/nordia/{es,en}/` es el único corpus, con operaciones logísticas y datos financieros inventados.
- El saludo y las preguntas sugeridas corresponden a esta empresa en español e inglés. Los documentos tienen ACL por rol.
- `POST /api/v1/chat?corpus=nordia` transmite SSE; `POST /api/v1/chat/sync?corpus=nordia` devuelve JSON y citas. `corpus` es opcional y Nordia es el único corpus habilitado.
- `GET /api/v1/eval?corpus=nordia` ejecuta 20 preguntas (10 ES y 10 EN). `POST /api/v1/knowledge/reindex?corpus=nordia` requiere `X-Admin-Key`.
- Fake es determinista para demostración y evaluación, no sustituye un modelo real.
