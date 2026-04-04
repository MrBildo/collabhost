namespace Collabhost.Api.Events;

public static class EventRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEventBus()
        {
            services.AddSingleton<IEventBus<ProcessStateChangedEvent>, EventBus<ProcessStateChangedEvent>>();

            return services;
        }
    }
}
