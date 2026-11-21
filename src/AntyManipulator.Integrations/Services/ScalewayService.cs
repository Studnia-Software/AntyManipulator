using System.Text.Json;
using AntyManipulator.Integrations.Internal;
using AntyManipulator.Integrations.Models;
using AntyManipulator.Integrations.Models.Enums;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Services;

public interface IScalewayService
{
    public Task<AnalysisResultDto> AnalyzePost(UploadMediaModel model);
}

public class ScalewayService : IScalewayService
{
    private readonly IOptions<MessageSettings> _messageSettings;
    private readonly IOptions<SourceSettings> _sourceSettings;
    private readonly IOptions<ScalewayOptions> _scalewayOptions;
    private readonly HttpClient _httpClient;

    public ScalewayService(IOptions<ScalewayOptions> scalewayOptions, IOptions<SourceSettings> sourceSettings, IOptions<MessageSettings> messageSettings, HttpClient httpClient)
    {
        _scalewayOptions = scalewayOptions;
        _sourceSettings = sourceSettings;
        _messageSettings = messageSettings;
        _httpClient = httpClient;
    }

    public async Task<AnalysisResultDto> AnalyzePost(UploadMediaModel model)
    {
        var request = RequestHelper.BuildScalewayRequest(_messageSettings.Value, _sourceSettings.Value, model);
        
        var messageToSend = MessageHelper.CreateHttpRequestMessage(request, _scalewayOptions.Value);

        var result = await _httpClient.SendAsync(messageToSend);
        
        var quickAnalysis = await result.Content.ReadAsStringAsync();
        quickAnalysis = quickAnalysis.Replace("/", string.Empty);
        
        var deserializedAnalysis = JsonSerializer.Deserialize<Root>(quickAnalysis);

        var content = (deserializedAnalysis?.choices.FirstOrDefault()?.message.content ?? string.Empty)
            .Replace("`", string.Empty)
            .Replace("json\n", string.Empty);
        
        var message = JsonSerializer.Deserialize<Content>(content);

        return new AnalysisResultDto
        {
            TruthRate = Enum.Parse<TruthRate>(message.TruthRate),
            MisInformationSearchEngines = [ new()
                {
                    SearchEngineName = _sourceSettings.Value.Demagog,
                    TruthRate = Enum.Parse<TruthRate>(message.TruthRate),
                    ArticleLink = string.Empty,
                }
            ],
            QuickContentAnalysis = message?.QuickContentAnalysis ?? string.Empty
        };
    }
}