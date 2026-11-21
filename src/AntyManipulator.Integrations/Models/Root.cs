namespace AntyManipulator.Integrations.Models;

public class Root
{
    public string id { get; set; }
    public string @object { get; set; }
    public long created { get; set; }
    public string model { get; set; }
    public Choice[] choices { get; set; }
}

public class Choice
{
    public int index { get; set; }
    public Message message { get; set; }
}

public class Message
{
    public string role { get; set; }
    public string content { get; set; }
}

public class Content
{
    public string TruthRate { get; set; }
    public string? QuickContentAnalysis { get; set; }
}