using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

public record ScalewayOptions
{
    [Required]
    public required string Uri { get; set; }
    
    [Required] 
    public required string ApiKey { get; set; }
}