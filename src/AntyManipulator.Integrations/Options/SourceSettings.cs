using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

public record SourceSettings
{
    [Required]
    public required string Demagog { get; init; }
    
    [Required]
    public required string FakeHunter { get; init; }
    
    [Required]
    public required string Kontekst24 { get; init; }
    
    [Required]
    public required string FactCheck { get; init; }
    
    [Required]
    public required string PolitiFacts { get; init; }
}