using System.ComponentModel.DataAnnotations;

namespace AntyManipulator.Integrations.Options;

public class MessageSettings
{
    [Required]
    public required string Model { get; set; }
    
    [Required] 
    public required string Message { get; set; }
}