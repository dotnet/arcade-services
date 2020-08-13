using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.Logging
{
    public class OperationManager
    {
        private readonly IOptions<OperationManagerOptions> _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly IServiceScopeFactory _scope;
        private readonly ILogger<OperationManager> _logger;

        public OperationManager(
            IOptions<OperationManagerOptions> options,
            IServiceProvider serviceProvider,
            IServiceScopeFactory scope,
            ILogger<OperationManager> logger = null)
        {
            _options = options;
            _serviceProvider = serviceProvider;
            _scope = scope;
            _logger = logger;
        }

        /// <summary>
        /// Bing an operation.  This will create a logging scope (including setting Activity.Id)
        /// as well as create a new scoped IServiceProvider (available on the return value <see cref="Operation.ServiceProvider"/>)
        /// </summary>
        /// <param name="name">Logging format string for scope. <example><code>Processing message {messageId}</code></example></param>
        /// <param name="args">Optional parameters to format the logging message. <example>message.Id will set the {messageId} in the name example</example></param>
        /// <returns>A new operation that exposes the <see cref="IServiceProvider"/>, and, when disposed, will end the operation and all resulting scopes</returns>
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
        
        /// <summary>
        /// The same as <see cref="BeginOperation"/> except this does not create a new ServiceProvider scope.
        /// In general, BeginOperation should be preferred, to ensure proper cleanup of scoped dependencies,
        /// but for tight loops where performance is a concern, this is a lighter weight alternative.
        /// </summary>
        /// <seealso cref="BeginOperation"/>
        public Operation BeginLoggingScope(string name, params object[] args)
        {
            string formatted = FormattableStringFormatter.Format(name, args);
            var o = new Operation(_serviceProvider);
            if (_options.Value.ShouldStartActivity)
            {
                var activity = new Activity(formatted);
                o.TrackActivity(activity.Start());
            }

            if (_options.Value.ShouldCreateLoggingScope && _logger != null)
            {
                o.TrackDisposable(_logger.BeginScope(name, args));
            }

            return o;
        }
    }
}
