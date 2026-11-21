namespace AntyManipulator.Core.Domain;

public enum Verdict
{
    Confirmed,
    False,
    Misleading,
    InsufficientEvidence,
    NotVerifiable,
}

public enum ServiceStatus
{
    TechnicalFailure,
    CannotCheck,
    NeedsClarification,
}

public enum OutcomeKind
{
    Verdict,
    Status,
}

public enum EvidenceStrength
{
    Low,
    Medium,
    High,
}

public enum Stance
{
    Supports,
    Refutes,
    Misleading,
    Unrelated,
}

public enum Relevance
{
    Low,
    Medium,
    High,
}

public enum SourceType
{
    FactChecker,
    Encyclopedia,
    Official,
    News,

    /// <summary>Dowolna strona znaleziona przez wyszukiwarkę internetową, spoza rejestru.</summary>
    Web,
}

public enum ClaimKind
{
    Factual,
    Opinion,
    Prediction,
    Satire,
    Other,
}

public enum RiskLevel
{
    Normal,
    High,
}

public enum SourceQueryStatus
{
    Ok,
    NoResults,
    Failed,
    Skipped,
}
