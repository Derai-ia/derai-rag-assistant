using Derai.RagAssistant.Application;

namespace Derai.RagAssistant.Api.Endpoints;

public static class EvaluationEndpoints
{
    private static readonly (string Question, string Expected, string Role, string Language)[] NordiaCases =
    [
        ("plazo reclamaciones cinco dÃ­as", "reclamaciones.md", "soporte", "es"),
        ("alta proveedores documentaciÃ³n", "alta-proveedores.md", "operaciones", "es"),
        ("crear envÃ­o identificador TMS", "manual-tms.md", "operaciones", "es"),
        ("diferencias inventario almacÃ©n", "incidencias-almacen.md", "operaciones", "es"),
        ("seguridad informaciÃ³n autenticaciÃ³n multifactor", "seguridad.md", "direccion", "es"),
        ("tarifas negociadas por palÃ© estÃ¡ndar", "contrato-marco-transportista.md", "direccion", "es"),
        ("vacaciones permisos diez dÃ­as laborables", "rrhh.md", "direccion", "es"),
        ("entrega demorada Alba Vega", "caso-01.md", "soporte", "es"),
        ("etiqueta duplicada envÃ­o", "caso-02.md", "soporte", "es"),
        ("penalizaciÃ³n transportista retraso", "contrato-marco-transportista.md", "direccion", "es"),
        ("supplier onboarding legal business name", "alta-proveedores.md", "operaciones", "en"),
        ("duplicate label same parcel", "caso-02.md", "soporte", "en"),
        ("damaged goods package quality", "caso-04.md", "soporte", "en"),
        ("standard pallet negotiated rates euros", "contrato-marco-transportista.md", "direccion", "en"),
        ("claims procedure business days", "reclamaciones.md", "soporte", "en"),
        ("warehouse inventory discrepancies units", "incidencias-almacen.md", "operaciones", "en"),
        ("vacation requests business days", "rrhh.md", "direccion", "en"),
        ("multifactor authentication security", "seguridad.md", "operaciones", "en"),
        ("unpaid invoice N-2025-031 amount due date", "finanzas-muestra.md", "direccion", "en"),
        ("sample sales March revenue operating result expenses", "finanzas-muestra.md", "direccion", "en")
    ];

    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/eval", async (string? corpus, ChatService chat) =>
        {
            corpus ??= "nordia";
            if (corpus != "nordia")
                return Results.BadRequest();

            var rows = new List<object>();
            var hits = 0;
            foreach (var (question, expected, role, language) in NordiaCases)
            {
                var answer = await chat.AskAsync(new(question, "eval", language), role, corpus: corpus);
                var found = answer.Sources.Any(source =>
                    source.File.Equals(expected, StringComparison.OrdinalIgnoreCase)
                    && source.Language == language);
                if (found)
                    hits++;
                rows.Add(new { question, expected, language, hit = found, role });
            }

            return Results.Ok(new
            {
                corpus,
                metric = "hit-rate@5",
                score = (double)hits / NordiaCases.Length,
                hits,
                total = NordiaCases.Length,
                cases = rows
            });
        });

        return endpoints;
    }
}
