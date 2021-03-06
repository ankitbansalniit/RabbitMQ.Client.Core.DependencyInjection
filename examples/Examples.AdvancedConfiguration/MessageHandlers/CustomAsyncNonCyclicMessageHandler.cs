using System.Threading.Tasks;
using RabbitMQ.Client.Core.DependencyInjection.MessageHandlers;
using RabbitMQ.Client.Core.DependencyInjection.Services;
using RabbitMQ.Client.Events;

namespace Examples.AdvancedConfiguration.MessageHandlers
{
    public class CustomAsyncNonCyclicMessageHandler : IAsyncNonCyclicMessageHandler
    {
        public async Task Handle(BasicDeliverEventArgs eventArgs, string matchingRoute, IQueueService queueService)
        {
            // The message handler does not do anything.
            // It is just an example.
            await Task.CompletedTask;
        }
    }
}