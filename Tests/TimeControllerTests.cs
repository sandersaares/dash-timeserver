using Autofac;
using DashTimeserver.Server.Controllers;
using Koek;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using System;

namespace DashTimeserver.Tests
{
    [TestClass]
    public sealed class TimeControllerTests : IDisposable
    {
        /// <summary>
        /// Our time source is frozen at a specific moment in time.
        /// </summary>
        private static readonly DateTimeOffset DefaultTime = new DateTimeOffset(1999, 6, 5, 4, 3, 2, TimeSpan.Zero);

        public TimeControllerTests()
        {
            _timeSource.GetCurrentTime().ReturnsForAnyArgs(DefaultTime);

            var builder = new ContainerBuilder();

            builder.RegisterInstance(_timeSource);

            builder.RegisterType<TimeController>();

            _scope = builder.Build();
        }

        private readonly ILifetimeScope _scope;

        public void Dispose()
        {
            _scope.Dispose();
        }

        private ITimeSource _timeSource = Substitute.For<ITimeSource>();

        [TestMethod]
        public void WithoutOffset_ReturnsCurrentTime()
        {
            var instance = _scope.Resolve<TimeController>();

            var time = instance.CurrentTimeAsXsDateTime(offsetSeconds: null);
            var expected = DefaultTime.ToString(TimeController.FormatString);

            Assert.AreEqual(expected, time);
        }

        [TestMethod]
        public void WithDifferentOffsets_ReturnsOffsetTime()
        {
            var instance = _scope.Resolve<TimeController>();

            // Positive offset
            var time = instance.CurrentTimeAsXsDateTime(offsetSeconds: 3.3);
            var expected = DefaultTime.AddSeconds(3.3).ToString(TimeController.FormatString);

            Assert.AreEqual(expected, time);

            // Negative offset
            time = instance.CurrentTimeAsXsDateTime(offsetSeconds: -3.3);
            expected = DefaultTime.AddSeconds(-3.3).ToString(TimeController.FormatString);

            Assert.AreEqual(expected, time);

            // No offset but parameter specified.
            time = instance.CurrentTimeAsXsDateTime(offsetSeconds: 0);
            expected = DefaultTime.ToString(TimeController.FormatString);

            Assert.AreEqual(expected, time);
        }
    }
}
