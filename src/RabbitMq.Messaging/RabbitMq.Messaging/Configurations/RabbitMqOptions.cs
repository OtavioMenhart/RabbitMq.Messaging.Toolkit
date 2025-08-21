namespace RabbitMq.Messaging.Configurations
{
    public class RabbitMqOptions
    {
        /// <summary> 
        /// Section name of appsettings 
        /// </summary> 
        public const string SectionName = "RabbitMq";

        /// <summary> 
        /// Hosts RabbitMQ 
        /// </summary> 
        public List<string> Hosts { get; set; }

        /// <summary> 
        /// Port - the default is 5672 
        /// </summary> 
        public int Port { get; set; } = 5672;

        /// <summary> 
        /// User to login 
        /// </summary> 
        public string UserName { get; set; } = "guest";

        /// <summary>
        /// Password to login
        /// </summary>
        public string Password { get; set; } = "guest";

        /// <summary>
        /// Virutal host of the application
        /// </summary>
        public string VirtualHost { get; set; } = "/";

        /// <summary>
        /// Amount of time client will wait for before re-trying to recover connection.
        /// </summary>
        public int NetworkRecoveryInterval { get; set; }
    }
}
