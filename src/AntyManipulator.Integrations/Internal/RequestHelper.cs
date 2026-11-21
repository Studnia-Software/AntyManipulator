using AntyManipulator.Integrations.Internal.Models;
using AntyManipulator.Integrations.Models;
using AntyManipulator.Integrations.Options;

namespace AntyManipulator.Integrations.Internal;

internal class RequestHelper
{
    public static ScalewayRequest BuildScalewayRequest(MessageSettings messageSettings,
        SourceSettings sourceSettings, UploadMediaModel model)
    {
        return new ScalewayRequest
        {
            Model = messageSettings.Model,
            Messages =
            [
                new ScalewayRequest.MessageModel
                {
                    Role = "user",
                    Content = string.Format(messageSettings.Message, model.Content, sourceSettings.Demagog)
                }
            ],
        };
    }
}