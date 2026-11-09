using Derai.RagAssistant.Domain;
using Derai.RagAssistant.Infrastructure;
using Xunit;

namespace Derai.RagAssistant.Tests;

public sealed class FakeChatModelTests
{
    [Theory]
    [InlineData("es", "Cuanto vendio en marzo Nordia", "En marzo de 2025, Nordia Logistica registro ventas de muestra por 46.800 euros en 124 servicios.", "46.800 euros")]
    [InlineData("en", "How much did it sell in March?", "In March 2025, Nordia Logistics recorded sample sales of 46,800 euros across 124 services.", "46,800 euros")]
    [InlineData("es", "Que facturas estan pendientes", "La factura ficticia N-2025-031, emitida a Cliente Roble por 2.400 euros, figura pendiente de pago con vencimiento el 15 de abril de 2025.", "pendiente de pago")]
    [InlineData("en", "Which invoices are unpaid?", "Fictional invoice N-2025-031, issued to Oak Customer for 2,400 euros, is listed as unpaid and due on 15 April 2025.", "listed as unpaid")]
    [InlineData("es", "Cual fue el resultado operativo", "Los costes directos de muestra fueron 29.600 euros y los gastos operativos fueron 8.800 euros. El resultado operativo de muestra fue 8.400 euros antes de impuestos.", "8.400 euros")]
    [InlineData("en", "What was the operating result?", "Sample direct costs were 29,600 euros and operating expenses were 8,800 euros. The sample operating result was 8,400 euros before tax.", "8,400 euros")]
    [InlineData("es", "Como se tramita una reclamacion", "La reclamacion debe presentarse dentro de los cinco dias laborables siguientes a la entrega. Adjunta el numero de expedicion.", "cinco dias laborables")]
    [InlineData("en", "How do I request a return?", "Submit a return request within seven calendar days after delivery. Include the shipment identifier.", "seven calendar days")]
    [InlineData("es", "Como doy de alta un proveedor", "Registra la razon social y el identificador fiscal. Compras valida los datos antes de activar el proveedor.", "Compras valida los datos")]
    [InlineData("en", "How do I file a claim?", "For a claim, report the incident within five business days after delivery. Attach the shipment identifier.", "five business days")]
    public async Task Composes_clean_cited_answer_for_sample_company_in_both_languages(string language, string question, string text, string relevantPhrase)
    {
        var model = new FakeChatModel();
        var hit = new SearchHit(new KnowledgeChunk("sample-source.md", "Sample section", $"## Sample heading\n{text}", language, ["soporte"]), .04);
        var answer = await model.GenerateAsync(question, [hit], language);
        Assert.Contains(relevantPhrase, answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[1]", answer);
        Assert.DoesNotContain("#", answer);
        Assert.DoesNotContain("According to the documentation", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Segun la documentacion", answer, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("[1]", answer.Trim());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Abstains_briefly_and_offers_human_help_when_context_is_insufficient(string language)
    {
        var answer = await new FakeChatModel().GenerateAsync("question", [], language);
        Assert.Contains(language == "en" ? "connect you with someone" : "contacto con alguien", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No encuentro", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Procedure_answer_skips_sentences_that_only_match_generic_terms()
    {
        SearchHit[] context =
        [
            new(new KnowledgeChunk("reclamaciones.md", "Claims procedure", "Lost goods require notification and a claim file.", "en", ["operaciones"]), .04),
            new(new KnowledgeChunk("reclamaciones.md", "Claims procedure", "Record the incident within five business days and attach the shipment identifier.", "en", ["operaciones"]), .035),
            new(new KnowledgeChunk("alta-proveedores.md", "Supplier onboarding", "Onboarding is completed within three business days after the file is complete.", "en", ["operaciones"]), .03)
        ];

        var answer = await new FakeChatModel().GenerateAsync("How do I file a claim?", context, "en");

        Assert.Contains("five business days", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Onboarding", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[1]", answer);
    }

    [Fact]
    public async Task Sales_question_does_not_include_costs_from_the_same_finance_document()
    {
        SearchHit[] context =
        [
            new(new KnowledgeChunk("finanzas-muestra.md", "March sales", "In March 2025, Nordia Logistics recorded sample sales of 46,800 euros across 124 services.", "en", ["direccion"]), .04),
            new(new KnowledgeChunk("finanzas-muestra.md", "March costs and result", "Sample direct costs were 29,600 euros and operating expenses were 8,800 euros. The sample operating result was 8,400 euros before tax.", "en", ["direccion"]), .035)
        ];

        var answer = await new FakeChatModel().GenerateAsync("How much did it sell in March?", context, "en");

        Assert.Contains("46,800 euros", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("29,600 euros", answer, StringComparison.Ordinal);
    }
}
