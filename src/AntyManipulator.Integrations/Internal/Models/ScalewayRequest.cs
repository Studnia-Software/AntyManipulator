using System.Text.Json.Serialization;

namespace AntyManipulator.Integrations.Internal.Models;

internal record ScalewayRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }
    
    [JsonPropertyName("messages")]
    public required List<MessageModel> Messages { get; init; }

    public class MessageModel
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }
        
        [JsonPropertyName("content")]
        public required string Content { get; init; } 
    }
}