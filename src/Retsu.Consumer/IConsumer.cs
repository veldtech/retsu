namespace Retsu.Consumer
{
    using System.Threading.Tasks;

    public interface IConsumer
    {

        ValueTask SubscribeAsync(string ev);

        ValueTask UnsubscribeAsync(string ev);
    }
}
