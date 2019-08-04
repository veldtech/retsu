using Miki.Discord.Common.Gateway;
using Miki.Discord.Common.Gateway.Packets;
using Newtonsoft.Json;

namespace Miki.Discord.Common
{
	/// <summary>
	/// Communication pipeline model for consumer-to-publisher messaging.
	/// </summary>
	public class CommandMessage
	{
		/// <summary>
		/// Shard Id that is affected to this command.
		/// </summary>
		[JsonProperty("shard_id")]
		public int ShardId { get; set; }

		/// <summary>
		/// Discord Opcode. Used to identify your packet.
		/// </summary>
		[JsonProperty("opcode")]
		public GatewayOpcode Opcode { get; set; }

		/// <summary>
		/// Retsu Commands, will override discord commands.
		/// </summary>
		[JsonProperty("type")]
		public string Type { get; set; }

		/// <summary>
		/// To-be interpreted data from the opcode/type identifier.
		/// </summary>
		[JsonProperty("data")]
		public object Data { get; set; }

		public static CommandMessage FromGatewayMessage<T>(int shardId, GatewayMessage message)
			where T : class
		{
			if(message.OpCode == null)
			{
				return null;
			}

			CommandMessage msg = new CommandMessage();
			msg.ShardId = shardId;
			msg.Opcode = message.OpCode.Value;
			msg.Data = message.Data as T;
			return msg;
		}
	}
}