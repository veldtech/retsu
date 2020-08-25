using System;
using Retsu.Consumer.Models;

namespace Retsu.Consumer
{
	public class ConsumerConfiguration : QueueConfiguration
	{
        /// <summary>
		/// Checks if the consumer should automatically acknowledge packages. This means that you will disable persistence in favor of performance.
		/// </summary>
		public bool ConsumerAutoAck { get; set; } = true;

        /// <summary>
		/// 
		/// </summary>
		public ushort PrefetchCount { get; set; } = 100;

		/// <summary>
		/// 
		/// </summary>
		public uint PrefetchSize { get; set; } = 0;
	}
}
