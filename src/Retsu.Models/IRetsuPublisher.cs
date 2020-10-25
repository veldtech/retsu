using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Retsu.Models.Communication;

namespace Retsu.Models
{
    public interface IRetsuPublisher : IHostedService
    {
        IObservable<CommandMessage> OnCommandReceived { get; }
        
        Task PublishPayloadAsync(CommandMessageSendArgs msg);
    }
}