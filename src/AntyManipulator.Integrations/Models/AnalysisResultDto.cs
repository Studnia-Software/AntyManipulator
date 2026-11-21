using AntyManipulator.Integrations.Models.Enums;

namespace AntyManipulator.Integrations.Models;

public record AnalysisResultDto
{
    public required TruthRate TruthRate { get; init; }

    public required List<MisInformationSearchEngine> MisInformationSearchEngines { get; init; }

    public required string? QuickContentAnalysis { get; init; }
}

public record MisInformationSearchEngine
{
    public required string SearchEngineName { get; init; }
    public required TruthRate TruthRate { get; init; }
    public required string ArticleLink { get; init; }
}