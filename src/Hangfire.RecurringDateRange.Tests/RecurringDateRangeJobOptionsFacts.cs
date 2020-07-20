using Hangfire.States;
using System;
using System.Linq;
using Xunit;

namespace Hangfire.RecurringDateRange.Tests
{
    public class RecurringDateRangeJobOptionsFacts
    {
        [Fact]
        public void Ctor_ThrowsAnException_WhenMethodCallIsNull()
        {

            var exception = Assert.Throws<ArgumentNullException>(
                () => new RecurringDateRangeJobOptions(null));

            Assert.Equal("methodCall", exception.ParamName);
        }

        [Fact]
        public void Validate_ThrowsAnException_WhenJobIsNull()
        {
            var options = CreateRecurringDateRangeJobOptions();
            options.Job = null;
            var exception = Assert.Throws<NullReferenceException>(
                () => options.Validate());

            Assert.True(exception.Message.Contains(nameof(RecurringDateRangeJobOptions.Job)));
        }

        [Fact]
        public void Validate_ThrowsAnException_WhenRecurringJobIdIsNull()
        {
            var options = CreateRecurringDateRangeJobOptions();
            options.RecurringJobId = null;
            var exception = Assert.Throws<NullReferenceException>(
                () => options.Validate());

            Assert.True(exception.Message.Contains(nameof(RecurringDateRangeJobOptions.RecurringJobId)));
        }

        [Fact]
        public void Validate_ThrowsAnException_WhenCronExpressionIsNull()
        {
            var options = CreateRecurringDateRangeJobOptions();
            options.CronExpression = null;
            var exception = Assert.Throws<NullReferenceException>(
                () => options.Validate());

            Assert.True(exception.Message.Contains(nameof(RecurringDateRangeJobOptions.CronExpression)));
        }

        [Fact]
        public void Validate_ThrowsAnException_WhenRecurringJobOptionsIsNull()
        {
            var options = CreateRecurringDateRangeJobOptions();
            options.RecurringJobOptions = null;
            var exception = Assert.Throws<NullReferenceException>(
                () => options.Validate());

            Assert.True(exception.Message.Contains(nameof(RecurringDateRangeJobOptions.RecurringJobOptions)));
        }

        private RecurringDateRangeJobOptions CreateRecurringDateRangeJobOptions()
        {
            var options = new RecurringDateRangeJobOptions(() => string.Empty.Reverse())
            {
                RecurringJobOptions = new RecurringJobOptions() { QueueName = EnqueuedState.DefaultQueue, TimeZone = TimeZoneInfo.Utc },
                CronExpression = "* * * * * *",
                EndDateTime = DateTime.MaxValue,
                StartDateTime = DateTime.UtcNow,
                RecurringJobId = "Id"
            };

            return options;
        }
    }
}
