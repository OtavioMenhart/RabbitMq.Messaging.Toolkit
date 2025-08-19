using Microsoft.AspNetCore.Mvc;
using RabbitMq.Example.Domain.Dto;
using RabbitMq.Messaging.Publisher;

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
            // Publish the user creation event to RabbitMQ
            await _publisher.PublishMessage(newUser, "new-user-exchange");
            return Ok($"User '{newUser.Name}' created successfully.");
        }
    }
}
