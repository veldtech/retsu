using System.Runtime.Serialization;
using Miki.Discord.Common.Gateway;

namespace Retsu.Models.Communication
{
    /// <summary>
	/// Communication pipeline model for consumer-to-publisher messaging.
	/// </summary>
	public class CommandMessage
	{
		/// <summary>
		/// Shard Id that is affected to this command.
		/// </summary>
		[DataMember(Name = "shard_id", Order = 1)]
		public int ShardId { get; set; }

        /// <summary>
        /// Discord Opcode. Used to identify your packet.
        /// </summary>
        [DataMember(Name = "opcode", Order = 2)]
		public GatewayOpcode Opcode { get; set; }

        /// <summary>
        /// Retsu Commands, will override discord commands.
        /// </summary>
        [DataMember(Name = "type", Order = 3)]
		public string Type { get; set; }

        /// <summary>
        /// To-be interpreted data from the opcode/type identifier.
        /// </summary>
        [DataMember(Name = "data", Order = 4)]
		public object Data { get; set; }

		public static CommandMessage FromGatewayMessage<T>(int shardId, GatewayMessage message)
			where T : class
		{
			if(message.OpCode == null)
			{
				return null;
			}

            return new CommandMessage
            {
                ShardId = shardId,
                Opcode = message.OpCode.Value,
                Data = message.Data as T
            };
        }
	}
}