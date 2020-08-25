using System;

namespace Retsu.Consumer.Models
{
    public class QueueConfiguration
    {       
        /// <summary>
        /// The connection uri to connect to the message queue.
        /// </summary>
        public Uri ConnectionString { get; set; }

        /// <summary>
        /// The name of the exchange the messages get sent to.
        /// </summary>
        public string ExchangeName { get; set; }

        /// <summary>
        /// Routing key of the Exchange. This can be used to route specific messages 
        /// </summary>
        public string ExchangeRoutingKey { get; set; } = "";

        /// <summary>
        /// Automatically deletes the queue if no consumers or are active.
        /// </summary>
        public bool QueueAutoDelete { get; set; } = false;

        /// <summary>
        /// 
        /// </summary>
        public bool QueueDurable { get; set; } = true;

        /// <summary>
        /// 
        /// </summary>
        public bool QueueExclusive { get; set; } = false;

        /// <summary>
        /// 
        /// </summary>
        public string QueueName { get; set; }
    }
}
