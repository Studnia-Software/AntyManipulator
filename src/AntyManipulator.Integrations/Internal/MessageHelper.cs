using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using AntyManipulator.Integrations.Internal.Models;
using AntyManipulator.Integrations.Options;

namespace AntyManipulator.Integrations.Internal;

internal class MessageHelper
{
    public static HttpRequestMessage CreateHttpRequestMessage(ScalewayRequest request, ScalewayOptions options)
    {
        var message = new HttpRequestMessage();
        
        message.Method = HttpMethod.Post;
        
        var serializedRequest = JsonSerializer.Serialize(request);

        message.RequestUri = new Uri(options.Uri);
        message.Content = new StringContent(serializedRequest, Encoding.UTF8, MediaTypeNames.Application.Json);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        return message;
    }
}