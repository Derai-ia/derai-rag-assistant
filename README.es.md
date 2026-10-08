# DERAI RAG Assistant

![CI](https://github.com/derai/derai-rag-assistant/actions/workflows/ci.yml/badge.svg)

Aplicación de referencia en .NET 10 para consultar documentos en dos idiomas con citas y control de acceso por rol. Nordia Logística S.L. y todos los documentos del corpus son ficticios.

## Ejecutar localmente sin Docker

Desde la raíz del repositorio, ejecuta estos tres comandos:

```bash
dotnet restore Derai.RagAssistant.slnx
dotnet build Derai.RagAssistant.slnx -c Release
dotnet run --project src/Derai.RagAssistant.Api --urls http://localhost:8080
```

Abre <http://localhost:8080>. El modelo Fake y el índice en memoria no necesitan credenciales.

## Ejecutar con Docker Compose

```bash
docker compose up --build -d
docker compose ps
curl http://localhost:8080/health/ready
```

Detén el servicio con `docker compose down`.

## Capturas de la interfaz

| Estado | Captura |
|---|---|
| Dirección, tema claro, respuesta inglesa con cita | ![Chat de dirección en tema claro](docs/img/chat-direccion.png) |
| Dirección, tema oscuro | ![Chat de dirección en tema oscuro](docs/img/chat-direccion-dark.png) |
| Soporte se abstiene ante el contrato restringido | ![Abstención del chat de soporte](docs/img/chat-soporte.png) |
| Evaluación de recuperación bilingüe | ![Evaluación de calidad](docs/img/calidad.png) |

Regenera las capturas con `tools/screenshots/run.ps1` en Windows o `bash tools/screenshots/run.sh` en Linux/macOS. Playwright y Chromium están aislados en `tools/screenshots/` como herramientas de desarrollo.

## Estado verificado

| Capacidad | Evidencia y estado real |
|---|---|
| Build Release .NET 10 con warnings como errores | **Verificado localmente**: build correcto con 0 warnings y 0 errores. |
| Tests unitarios e integración API | **Verificado localmente**: 22 tests pasan, incluidos ACL bilingües, citas del contrato y abstención de soporte. |
| Evaluación bilingüe | **Verificado localmente**: Fake obtuvo las 20 fuentes esperadas (10 consultas españolas y 10 inglesas). Es una comprobación fija de fixtures. |
| Health, Swagger, SSE y ACL por rol | **Verificado localmente**: health y Swagger devolvieron 200; SSE emitió `token`, `sources` y `done`; las consultas restringidas no devolvieron fuentes a soporte. |
| Capturas UI | **Verificado localmente**: generadas con Playwright Chromium headless; hay capturas de dirección en claro/oscuro, soporte y calidad en `docs/img/`. |
| Adaptadores de chat OpenAI, Azure OpenAI, Anthropic y Ollama | **Probado con handler simulado**: formato de petición y lectura de respuestas en streaming. **No probado contra servicios reales.** |
| Adaptadores de embeddings OpenAI, Azure OpenAI y Ollama | **Probado con handler simulado**: formato de petición y lectura de respuesta. **No probado contra servicios reales.** |
| Adaptador Azure AI Search | **Probado con handler simulado**: esquema, lotes de upsert, filtros por rol/idioma y fallback. **No probado contra servicio real.** |
| Docker Compose en este entorno | **No probado contra servicio/ejecución real**: el motor Linux de Docker Desktop no estaba disponible durante la verificación. |

Las comprobaciones de API locales usaron Fake, almacenamiento en memoria y JWT de demo. No demuestran disponibilidad ni calidad de servicio en producción.

## Arquitectura y comportamiento

- La arquitectura separa Domain, Application, Infrastructure y API.
- Los 15 documentos lógicos están en versiones equivalentes en español e inglés en `data/docs/es/` y `data/docs/en/`. Cada pareja comparte `id` y `allowedRoles` en el front matter e indica su propio `language`.
- El filtro ACL se aplica antes de recuperar documentos. Primero se busca en el idioma seleccionado y solo se recurre al otro si no hay resultados suficientemente relevantes. Las fuentes muestran el idioma.
- `POST /api/v1/chat` envía eventos SSE `token`, `sources` y `done`. `POST /api/v1/chat/sync` devuelve JSON con respuesta y citas.
- El modelo Fake es determinista y se abstiene si la evidencia tiene una puntuación demasiado baja. El texto recuperado se trata como dato no confiable.
- `GET /api/v1/eval` comprueba 20 consultas fijas y sus documentos esperados. `POST /api/v1/knowledge/reindex` requiere `X-Admin-Key`.
- La UI permite cambiar idioma, rol, citas, evaluación y tema claro/oscuro.

## Limitaciones

El embedding hash Fake **no es semántico**. La recuperación bilingüe local también usa coincidencia léxica; se espera que un proveedor real de embeddings mejore la recuperación semántica, pero aquí no se midió contra un proveedor real. La evaluación 20/20 es una fixture determinista pequeña, no una medida general de relevancia. Las llamadas LLM reales, embeddings reales y un índice Azure AI Search desplegado siguen sin verificarse. Los JWT de demo firmados con una clave efímera por proceso salvo configuración, el historial en memoria y los documentos sintéticos no son identidad, gestión de secretos, persistencia ni garantías operativas de producción.

## Configuración

Las opciones usan nombres de variables de entorno .NET como `Llm__Provider`, `Llm__ApiKey`, `Llm__Endpoint`, `Llm__EmbeddingProvider`, `VectorStore__Provider`, `VectorStore__Endpoint`, `VectorStore__ApiKey` y `Auth__SigningKey`. Los valores iniciales son `Fake` y `Memory`; si falta `Auth__SigningKey`, la API genera una clave aleatoria solo para ese proceso. Consulta la [guía en inglés](README.md) y las [instrucciones para contribuir](CONTRIBUTING.md).




