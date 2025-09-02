using Microsoft.AspNetCore.Mvc;
using RabbitMq.Example.Domain.Dto;
using RabbitMq.Messaging.Publisher;
using RabbitMQ.Client;

namespace RabbitMq.Example.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IRabbitMqPublisherService _publisher;

        public UsersController(IRabbitMqPublisherService publisher)
        {
            _publisher = publisher;
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] NewUserDto newUser)
        {
            // Publish to fanout exchange
            await _publisher.PublishMessage(newUser, "new-user-exchange");

            // Publish to direct exchange
            await _publisher.PublishMessage(newUser, "new-user-exchange-direct", exchangeType: ExchangeType.Direct, routingKey: "user.created");
            
            // Publish to topic exchange
            await _publisher.PublishMessage(newUser, "new-user-exchange-topic", exchangeType: ExchangeType.Topic, routingKey: "user.whatever");

            return Ok($"User '{newUser.Name}' created successfully.");
        }
    }
}
