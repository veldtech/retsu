![retsu logo](https://cdn.miki.ai/branding/retsu.png)

> :warning: This project is still in Proof-of-concept mode. However, during testing has proven to be stable. Systems might change over time.
            
A bi-directional consumer/publisher pattern using RabbitMQ to create distributed systems for Event-based systems.

## Protocol

As this protocol was originally designed to leverage the Discord gateway it is heavily designed for event-based systems. 

### Publisher

The publisher receives an event code from downstream (in Discord's case `EventName`), which then gets propagated as JSON or proto to RabbitMQ. 

#### Configuration

To generate the configuration file, run the app once, it'll close due to there not being any configuration, but will generate a file for you to enter your credentials into.

|Property|Type|Description|
| --- | --- | --- |
|discord| DiscordConfiguration | Your Discord gateway properties |
|ignored_packets*| Array<string> | Packets by event name that you want to discard |
|log_level| Integer or String | Miki.Logging log level, propagates through the app |
|msg_queue| MQConfig | RabbitMQ settings |
|redis_url| string | formatted as the following: `{{host}}:{{port}},Password={{password}}`. Redis is heavily recommended when running Retsu in more than one instance at a time. |
|sentry_url| string | Handles error logging. |

##### DiscordConfiguration
Represents Discord-specific configuration. This is the current set of needed features.

|Property|Type|Description|
| --- | --- | --- |
|token|String|The Discord Token|
|shard_count|Integer|Your total amount of shards|
|shard_start_index|Integer|The start offset of your shards to start initializing|
|shard_num|Integer|The amount of shards to create on this node|

##### MQConfig
Message queue specific configuration.

|Property|Type|Description|
| --- | --- | --- |
|url| URL| The RabbitMQ connection URL|

### Consumer

The consumer subscribes to specific event names by calling the following queue pattern `gateway:{{event_name}}` This event name is not being altered from the original documentation, meaning that for a discord incoming message you have to use the event name `MESSAGE_CREATE`.
