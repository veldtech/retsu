namespace Retsu.Consumer
{
    public interface IMQMessage<out T>
    {
        T Body { get; }

        void Ack();
        
        void Nack();
    }
}
