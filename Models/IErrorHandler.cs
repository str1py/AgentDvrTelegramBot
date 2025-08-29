using Microsoft.Extensions.Logging;

namespace CountryTelegramBot.Models
{
    public interface IErrorHandler
    {
        void HandleError(Exception ex, string context = "");
    }

    public class DefaultErrorHandler : IErrorHandler
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        public DefaultErrorHandler(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }
        public void HandleError(Exception ex, string context = "")
        {
            _logger.LogError(ex, $"{context}");
        }
    }
}
