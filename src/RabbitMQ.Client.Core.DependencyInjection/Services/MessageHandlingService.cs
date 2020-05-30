using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Core.DependencyInjection.InternalExtensions;
using RabbitMQ.Client.Core.DependencyInjection.MessageHandlers;
using RabbitMQ.Client.Core.DependencyInjection.Models;
using RabbitMQ.Client.Events;

namespace RabbitMQ.Client.Core.DependencyInjection.Services
{
    /// <summary>
    /// An implementation of the service that handles message receiving (consumption) events.
    /// </summary>
    public class MessageHandlingService : IMessageHandlingService
    {
        readonly IEnumerable<RabbitMqExchange> _exchanges;
        readonly IEnumerable<MessageHandlerContainer> _messageHandlerContainers;
        readonly ILogger<MessageHandlingService> _logger;

        public MessageHandlingService(
            IMessageHandlerContainerBuilder messageHandlerContainerBuilder,
            IEnumerable<RabbitMqExchange> exchanges,
            ILogger<MessageHandlingService> logger)
        {
            _exchanges = exchanges;
            _messageHandlerContainers = messageHandlerContainerBuilder.BuildCollection();
            _logger = logger;
        }

        /// <summary>
        /// Handle message receiving event.
        /// </summary>
        /// <param name="eventArgs">Arguments of message receiving event.</param>
        /// <param name="queueService">An instance of the queue service <see cref="IQueueService"/>.</param>
        public async Task HandleMessageReceivingEvent(BasicDeliverEventArgs eventArgs, IQueueService queueService)
        {
            try
            {
                var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                _logger.LogInformation($"A new message received with deliveryTag {eventArgs.DeliveryTag}.");
                _logger.LogInformation(message);
                
                var matchingRoutes = GetMatchingRoutePatterns(eventArgs.Exchange, eventArgs.RoutingKey);
                await ProcessMessage(eventArgs.Exchange, message, queueService, matchingRoutes).ConfigureAwait(false);
                _logger.LogInformation($"Message processing finished successfully. Acknowledge has been sent with deliveryTag {eventArgs.DeliveryTag}.");
            }
            catch (Exception exception)
            {
                _logger.LogError(new EventId(), exception, $"An error occurred while processing received message with the delivery tag {eventArgs.DeliveryTag}.");
                await HandleFailedMessageProcessing(eventArgs, queueService).ConfigureAwait(false);
            }
            finally
            {
                queueService.ConsumingChannel.BasicAck(eventArgs.DeliveryTag, false);
            }
        }

        IEnumerable<string> GetMatchingRoutePatterns(string exchange, string routingKey)
        {
            var tree = _messageHandlerContainers.FirstOrDefault(x => x.Exchange == exchange)?.Tree ??
                _messageHandlerContainers.FirstOrDefault(x => x.IsGeneral)?.Tree;
            if (tree is null)
            {
                return Enumerable.Empty<string>();
            }

            var routingKeyParts = routingKey.Split(".");
            return WildcardExtensions.GetMatchingRoutePatterns(tree, routingKeyParts).ToList();
        }

        async Task ProcessMessage(string exchange, string message, IQueueService queueService, IEnumerable<string> matchingRoutes)
        {
            var container = _messageHandlerContainers.FirstOrDefault(x => x.Exchange == exchange) ??
                _messageHandlerContainers.FirstOrDefault(x => x.IsGeneral);
            if (container is null)
            {
                return;
            }

            var messageHandlerOrderingContainers = new List<MessageHandlerOrderingContainer>();
            foreach (var matchingRoute in matchingRoutes)
            {
                if (!container.MessageHandlers.ContainsKey(matchingRoute))
                {
                    continue;
                }

                var orderingContainers = container.MessageHandlers[matchingRoute].Select(handler => new MessageHandlerOrderingContainer
                {
                    MessageHandler = handler,
                    Order = container.MessageHandlerOrderingModels.FirstOrDefault(x => x.MessageHandlerType == handler.GetType())?.Order,
                    MatchingRoute = matchingRoute
                });
                messageHandlerOrderingContainers.AddRange(orderingContainers);
            }

            var executedHandlers = new List<Type>();
            var orderedContainers = messageHandlerOrderingContainers.OrderByDescending(x => x.Order)
                .ThenByDescending(x => x.MessageHandler.GetHashCode())
                .ToList();
            foreach (var orderedContainer in orderedContainers)
            {
                var handlerType = orderedContainer.MessageHandler.GetType();
                if (executedHandlers.Contains(handlerType))
                {
                    continue;
                }

                switch (orderedContainer.MessageHandler)
                {
                    case IMessageHandler messageHandler:
                        RunMessageHandler(messageHandler, message, orderedContainer.MatchingRoute);
                        break;
                    case IAsyncMessageHandler asyncMessageHandler:
                        await RunAsyncMessageHandler(asyncMessageHandler, message, orderedContainer.MatchingRoute).ConfigureAwait(false);
                        break;
                    case INonCyclicMessageHandler nonCyclicMessageHandler:
                        RunNonCyclicMessageHandler(nonCyclicMessageHandler, message, orderedContainer.MatchingRoute, queueService);
                        break;
                    case IAsyncNonCyclicMessageHandler asyncNonCyclicMessageHandler:
                        await RunAsyncNonCyclicMessageHandler(asyncNonCyclicMessageHandler, message, orderedContainer.MatchingRoute, queueService).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"The type {orderedContainer.MessageHandler.GetType()} of message handler is not supported.");
                }

                executedHandlers.Add(handlerType);
            }
        }

        void RunMessageHandler(IMessageHandler handler, string message, string routingKey)
        {
            ValidateMessageHandler(handler);
            _logger.LogDebug($"Starting processing the message by message handler {handler.GetType().Name}.");
            handler.Handle(message, routingKey);
            _logger.LogDebug($"The message has been processed by message handler {handler.GetType().Name}.");
        }

        void RunNonCyclicMessageHandler(INonCyclicMessageHandler handler, string message, string routingKey, IQueueService queueService)
        {
            ValidateMessageHandler(handler);
            _logger.LogDebug($"Starting processing the message by non-cyclic message handler {handler.GetType().Name}.");
            handler?.Handle(message, routingKey, queueService);
            _logger.LogDebug($"The message has been processed by non-cyclic message handler {handler.GetType().Name}.");
        }

        async Task RunAsyncMessageHandler(IAsyncMessageHandler handler, string message, string routingKey)
        {
            ValidateMessageHandler(handler);
            _logger.LogDebug($"Starting processing the message by async message handler {handler.GetType().Name}.");
            await handler.Handle(message, routingKey);
            _logger.LogDebug($"The message has been processed by async message handler {handler.GetType().Name}.");
        }

        async Task RunAsyncNonCyclicMessageHandler(
            IAsyncNonCyclicMessageHandler handler,
            string message,
            string routingKey,
            IQueueService queueService)
        {
            ValidateMessageHandler(handler);
            _logger.LogDebug($"Starting processing the message by async non-cyclic message handler {handler.GetType().Name}.");
            await handler.Handle(message, routingKey, queueService);
            _logger.LogDebug($"The message has been processed by async non-cyclic message handler {handler.GetType().Name}.");
        }

        static void ValidateMessageHandler<T>(T messageHandler)
        {
            if (messageHandler is null)
            {
                throw new ArgumentNullException(nameof(messageHandler), "Message handler is null.");
            }
        }
        
        async Task HandleFailedMessageProcessing(BasicDeliverEventArgs eventArgs, IQueueService queueService)
        {
            var exchange = _exchanges.FirstOrDefault(x => x.Name == eventArgs.Exchange);
            if (exchange is null)
            {
                _logger.LogWarning($"Could not detect an exchange \"{eventArgs.Exchange}\" to determine the necessity of resending the failed message. The message won't be re-queued.");
                return;
            }
            
            if (exchange.Options is null)
            {
                _logger.LogWarning($"Options of an exchange \"{eventArgs.Exchange}\" are not configured. The message won't be re-queued.");
                return;
            }
            
            if (!exchange.Options.RequeueFailedMessages)
            {
                _logger.LogWarning($"RequeueFailedMessages option for an exchange \"{eventArgs.Exchange}\" is disabled. The message won't be re-queued.");
                return;
            }

            if (string.IsNullOrEmpty(exchange.Options.DeadLetterExchange))
            {
                _logger.LogWarning($"DeadLetterExchange has not been configured for an exchange \"{eventArgs.Exchange}\". The message won't be re-queued.");
                return;
            }

            if (exchange.Options.RequeueTimeoutMilliseconds < 1)
            {
                _logger.LogWarning($"The value RequeueTimeoutMilliseconds for an exchange \"{eventArgs.Exchange}\" less than 1 millisecond. Configuration is invalid. The message won't be re-queued.");
                return;
            }
            
            if (exchange.Options.RequeueAttempts < 1)
            {
                _logger.LogWarning($"The value RequeueAttempts for an exchange \"{eventArgs.Exchange}\" less than 1. Configuration is invalid. The message won't be re-queued.");
                return;
            }

            if (eventArgs.BasicProperties.Headers is null)
            {
                eventArgs.BasicProperties.Headers = new Dictionary<string, object>();
            }

            if (!eventArgs.BasicProperties.Headers.ContainsKey("re-queue-attempts"))
            {
                eventArgs.BasicProperties.Headers.Add("re-queue-attempts", 1);
                await RequeueMessage(eventArgs, queueService, exchange.Options.RequeueTimeoutMilliseconds);
                return;
            }
            
            var currentAttempt = (int)eventArgs.BasicProperties.Headers["re-queue-attempts"];
            if (currentAttempt < exchange.Options.RequeueAttempts)
            {
                eventArgs.BasicProperties.Headers["re-queue-attempts"] = currentAttempt + 1;
                await RequeueMessage(eventArgs, queueService, exchange.Options.RequeueTimeoutMilliseconds);
            }
            else
            {
                _logger.LogInformation("The failed message would not be re-queued. Attempts limit exceeded.");   
            }
        }

        async Task RequeueMessage(BasicDeliverEventArgs eventArgs, IQueueService queueService, int timeoutMilliseconds)
        {
            await queueService.SendAsync(eventArgs.Body, eventArgs.BasicProperties, eventArgs.Exchange, eventArgs.RoutingKey, timeoutMilliseconds);
            _logger.LogInformation("The failed message has been re-queued.");
        }
    }
}