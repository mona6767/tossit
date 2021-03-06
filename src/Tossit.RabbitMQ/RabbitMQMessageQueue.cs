using System;
using System.Text;
using RabbitMQ.Client;
using Tossit.Core;

namespace Tossit.RabbitMQ
{
    /// <summary>
    /// RabbitMQ message queue implementation.
    /// </summary>
    public class RabbitMQMessageQueue : IMessageQueue
    {
        /// <summary>
        /// ConnectionWrapper field.
        /// </summary>
        private readonly IConnectionWrapper _connectionWrapper;
        /// <summary>
        /// JsonConverter field.
        /// </summary>
        private readonly IJsonConverter _jsonConverter;
        /// <summary>
        /// ChannelFactory field.
        /// </summary>
        private readonly IChannelFactory _channelFactory;
        /// <summary>
        /// EventingBasicConsumerImpl field.
        /// </summary>
        private readonly IEventingBasicConsumerImpl _eventingBasicConsumerImpl;
        /// <summary>
        /// ConsumerInvoker field.
        /// </summary>
        private readonly IConsumerInvoker _consumerInvoker;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="connectionWrapper">IConnectionWrapper</param>
        /// <param name="jsonConverter">IJsonConverter</param>
        /// <param name="channelFactory">IChannelFactory</param>
        /// <param name="eventingBasicConsumerImpl">IEventingBasicConsumerImpl</param>
        /// <param name="consumerInvoker">IConsumerInvoker</param>
        public RabbitMQMessageQueue(
            IConnectionWrapper connectionWrapper,
            IJsonConverter jsonConverter,
            IChannelFactory channelFactory,
            IEventingBasicConsumerImpl eventingBasicConsumerImpl,
            IConsumerInvoker consumerInvoker)
        {
            _connectionWrapper = connectionWrapper;
            _jsonConverter = jsonConverter;            
            _channelFactory = channelFactory;
            _eventingBasicConsumerImpl = eventingBasicConsumerImpl;
            _consumerInvoker = consumerInvoker;
        }

        /// <summary>
        /// Send to queue.
        /// </summary>
        /// <param name="name">Name of queue or whatever using to describing work/job/event. Same as Receive method's name.</param>
        /// <param name="message">Message string to sends to queue.</param>
        /// <param name="options">Options</param>
        /// <returns>Returns true if data send completed successfully, otherwise returns false.</returns>
        /// <exception cref="Exception">Throws when name or data is null.</exception>>
        public bool Send(string name, string message, Options options = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentNullException(nameof(message));

            // If options not specified, use default values.
            if (options == null) options = new Options();

            // Create new channel for each dispatch.
            using (var channel = _connectionWrapper.ProducerConnection.CreateModel())
            {
                // Create queue if it is not exist.
                var queueName = name;
                channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);

                // Enable publisher confirms.
                if (options.ConfirmIsActive)
                {
                    channel.ConfirmSelect();
                }

                var body = Encoding.UTF8.GetBytes(message);

                var properties = channel.CreateBasicProperties();
                // Set message as persistent.
                properties.Persistent = true;

                // Publish message.
                channel.BasicPublish(exchange: string.Empty, routingKey: queueName, basicProperties: properties,
                    body: body);

                // Wait form ack from worker.
                if (options.ConfirmIsActive)
                {
                    channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(options.ConfirmTimeout));
                }
            }

            return true;
        }

        /// <summary>
        /// Receive messages from message queue.
        /// This method will register the given function to consume queue.
        /// </summary>
        /// <param name="name">Name of queue or whatever using to describing work/job/event. Same as Send method's name.</param>
        /// <param name="func">Receiver function.</param>
        /// <returns>Returns true if receiver method registered successfully, otherwise returns false.</returns>
        /// <exception cref="Exception">Throws when name or func is null.</exception>>
        public bool Receive(string name, Func<string, bool> func)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (func == null) throw new ArgumentNullException(nameof(func));

            // Get new channel.
            var channel = _channelFactory.Channel;

            // Create queue if it is not exist.
            var queueName = name;
            channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);

            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            // Create new consumer.
            var consumer = _eventingBasicConsumerImpl.GetEventingBasicConsumer(channel);

            // Register to job received event.
            consumer.Received += (model, ea) =>
            {
                _consumerInvoker.Invoke(func, ea, channel);
            };

            // Start consuming.
            channel.BasicConsume(queueName, autoAck: false, consumer: consumer);
            
            return true;
        }
    }
}