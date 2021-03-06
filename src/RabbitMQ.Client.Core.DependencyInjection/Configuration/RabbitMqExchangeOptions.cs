﻿using System.Collections.Generic;

namespace RabbitMQ.Client.Core.DependencyInjection.Configuration
{
    /// <summary>
    /// Exchange options.
    /// </summary>
    public class RabbitMqExchangeOptions
    {
        /// <summary>
        /// Exchange type.
        /// </summary>
        public string Type { get; set; } = "direct";

        /// <summary>
        /// Durable option.
        /// </summary>
        public bool Durable { get; set; } = true;

        /// <summary>
        /// AutoDelete option.
        /// </summary>
        public bool AutoDelete { get; set; }

        /// <summary>
        /// Default dead-letter-exchange.
        /// </summary>
        public string DeadLetterExchange { get; set; } = "default.dlx.exchange";

        /// <summary>
        /// Option to re-queue failed messages.
        /// </summary>
        public bool RequeueFailedMessages { get; set; } = true;

        /// <summary>
        /// Re-queue message attempts.
        /// </summary>
        public int RequeueAttempts { get; set; } = 2;
        
        /// <summary>
        /// Re-queue timeout in milliseconds.
        /// </summary>
        public int RequeueTimeoutMilliseconds { get; set; } = 200;

        /// <summary>
        /// Additional arguments.
        /// </summary>
        public IDictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Collection of queues bound to the exchange.
        /// </summary>
        public IList<RabbitMqQueueOptions> Queues { get; set; } = new List<RabbitMqQueueOptions>();
    }
}