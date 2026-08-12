using ResumeAnalyzer.Application.Common.Behaviours;
using ResumeAnalyzer.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace ResumeAnalyzer.Application.UnitTests.Common.Behaviours;

public class RequestLoggerTests
{
    public record SampleCommand : IRequest<int>
    {
        public string? Title { get; init; }
    }

    private Mock<ILogger<SampleCommand>> _logger = null!;
    private Mock<IUser> _user = null!;
    private Mock<IIdentityService> _identityService = null!;

    [SetUp]
    public void Setup()
    {
        _logger = new Mock<ILogger<SampleCommand>>();
        _user = new Mock<IUser>();
        _identityService = new Mock<IIdentityService>();
    }

    [Test]
    public async Task ShouldCallGetUserNameAsyncOnceIfAuthenticated()
    {
        _user.Setup(x => x.Id).Returns(Guid.NewGuid().ToString());

        var requestLogger = new LoggingBehaviour<SampleCommand>(_logger.Object, _user.Object, _identityService.Object);

        await requestLogger.Process(new SampleCommand { Title = "title" }, new CancellationToken());

        _identityService.Verify(i => i.GetUserNameAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ShouldNotCallGetUserNameAsyncOnceIfUnauthenticated()
    {
        var requestLogger = new LoggingBehaviour<SampleCommand>(_logger.Object, _user.Object, _identityService.Object);

        await requestLogger.Process(new SampleCommand { Title = "title" }, new CancellationToken());

        _identityService.Verify(i => i.GetUserNameAsync(It.IsAny<string>()), Times.Never);
    }
}
