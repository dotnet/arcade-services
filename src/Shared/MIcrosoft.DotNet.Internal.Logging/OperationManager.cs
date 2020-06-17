using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.Logging
{
    public class OperationManager
    {
        private readonly IOptions<OperationManagerOptions> _options;
        private readonly IServiceScopeFactory _scope;
        private readonly ILogger<OperationManager> _logger;

        public OperationManager(
            IOptions<OperationManagerOptions> options,
            IServiceScopeFactory scope,
            ILogger<OperationManager> logger = null)
        {
            _options = options;
            _scope = scope;
            _logger = logger;
        }

        public Operation BeginOperation(string name, params object[] args)
        {
            string formatted = FormattableStringFormatter.Format(name, args);
            IServiceScope scope = _scope.CreateScope();
            var o = new Operation(scope.ServiceProvider);
            if (_options.Value.ShouldStartActivity)
            {
                var activity = new Activity(formatted);
                o.TrackActivity(activity.Start());
            }

            o.TrackDisposable(scope);

            if (_options.Value.ShouldCreateLoggingScope && _logger != null)
            {
                o.TrackDisposable(_logger.BeginScope(name, args));
            }

            return o;
        }
    }
}
