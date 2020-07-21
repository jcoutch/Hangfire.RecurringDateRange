﻿using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Core.Tests;
using Hangfire.RecurringDateRange.Constants;
using Hangfire.RecurringDateRange.Contracts;
using Hangfire.RecurringDateRange.Server;
using Hangfire.States;
using Hangfire.Storage;
using Moq;
using NCrontab.Advanced;
using Xunit;

namespace Hangfire.RecurringDateRange.Tests
{
    public class RecurringDateTimeJobSchedulerFacts
    {
        private const string RecurringJobId = "recurring-job-id";

        private readonly Mock<IStorageConnection> _connection;
        private readonly Dictionary<string, string> _recurringJob;
        private Func<CrontabSchedule, TimeZoneInfo, IScheduleInstant> _instantFactory; 
        private readonly Mock<IThrottler> _throttler;
        private readonly Mock<IScheduleInstant> _instant;
        private readonly BackgroundProcessContextMock _context;
        private readonly Mock<IBackgroundJobFactory> _factory;
        private readonly BackgroundJobMock _backgroundJobMock;

        public RecurringDateTimeJobSchedulerFacts()
        {
            _context = new BackgroundProcessContextMock();

            _throttler = new Mock<IThrottler>();

            // Setting up the successful path
            _instant = new Mock<IScheduleInstant>();
            _instant.Setup(x => x.GetNextInstants(It.IsAny<DateTime>(), null)).Returns(new[] { _instant.Object.NowInstant });
            _instant.Setup(x => x.GetNextInstants(It.IsAny<DateTime>(), It.IsAny<DateTime>())).Returns(new[] { _instant.Object.NowInstant });
            _instant.Setup(x => x.NowInstant).Returns(DateTime.UtcNow);
            _instant.Setup(x => x.NextInstant).Returns(_instant.Object.NowInstant);

            var timeZone1 = TimeZoneInfo.Local;

            _instantFactory = (schedule, timeZone) => _instant.Object;

            _recurringJob = new Dictionary<string, string>
            {
                { "Cron", "* * * * *" },
                { "Job", JobHelper.ToJson(InvocationData.Serialize(Job.FromExpression(() => Console.WriteLine()))) },
                { "TimeZoneId", timeZone1.Id },
                { "StartDate", null },
                { "EndDate", null }
            };

            _connection = new Mock<IStorageConnection>();
            _context.Storage.Setup(x => x.GetConnection()).Returns(_connection.Object);

            _connection.Setup(x => x.GetAllItemsFromSet(PluginConstants.JobSet))
                .Returns(new HashSet<string> { RecurringJobId });

            _connection.Setup(x => x.GetAllEntriesFromHash($"{PluginConstants.JobType}:{RecurringJobId}"))
                .Returns(_recurringJob);

            _backgroundJobMock = new BackgroundJobMock();

            _factory = new Mock<IBackgroundJobFactory>();
            _factory.Setup(x => x.Create(It.IsAny<CreateContext>())).Returns(_backgroundJobMock.Object);
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenProcessIsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
// ReSharper disable once AssignNullToNotNullAttribute
                () => new RecurringDateRangeJobScheduler(null, _instantFactory, _throttler.Object));

            Assert.Equal("factory", exception.ParamName);
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenInstantFactoryIsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
// ReSharper disable once AssignNullToNotNullAttribute
                () => new RecurringDateRangeJobScheduler(_factory.Object, null, _throttler.Object));

            Assert.Equal("instantFactory", exception.ParamName);
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenThrottlerIsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(
// ReSharper disable once AssignNullToNotNullAttribute
                () => new RecurringDateRangeJobScheduler(_factory.Object, _instantFactory, null));

            Assert.Equal("throttler", exception.ParamName);
        }

        [Fact]
        public void Execute_EnqueuesAJob_WhenItIsTimeToRunIt()
        {
            var scheduler = CreateScheduler();

            scheduler.Execute(_context.Object);

            _factory.Verify(x => x.Create(It.IsNotNull<CreateContext>()));
        }

        [Fact]
        public void Execute_EnqueuesAJobToAGivenQueue_WhenItIsTimeToRunIt()
        {
            _recurringJob["Queue"] = "critical";
            var scheduler = CreateScheduler();

            scheduler.Execute(_context.Object);

            _factory.Verify(x => x.Create(
                It.Is<CreateContext>(cc => ((EnqueuedState)cc.InitialState).Queue == "critical")));
        }

        [Fact]
        public void Execute_UpdatesRecurringJobParameters_OnCompletion()
        {
            // Arrange
            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            var jobKey = $"{PluginConstants.JobType}:{RecurringJobId}";

            _connection.Verify(x => x.SetRangeInHash(
                jobKey,
                It.Is<Dictionary<string, string>>(rj =>
                    rj.ContainsKey("LastJobId") && rj["LastJobId"] == _backgroundJobMock.Id)));

            _connection.Verify(x => x.SetRangeInHash(
                jobKey,
                It.Is<Dictionary<string, string>>(rj =>
                    rj.ContainsKey("LastExecution") && rj["LastExecution"]
                        == JobHelper.SerializeDateTime(_instant.Object.NowInstant))));

            _connection.Verify(x => x.SetRangeInHash(
                jobKey,
                It.Is<Dictionary<string, string>>(rj =>
                    rj.ContainsKey("NextExecution") && rj["NextExecution"]
                        == JobHelper.SerializeDateTime(_instant.Object.NowInstant))));
        }

        [Fact]
        public void Execute_DoesNotEnqueueRecurringJob_AndDoesNotUpdateIt_ButNextExecution_WhenItIsNotATimeToRunIt()
        {
            _instant.Setup(x => x.GetNextInstants(It.IsAny<DateTime>(), null)).Returns(Enumerable.Empty<DateTime>);
            var scheduler = CreateScheduler();

            scheduler.Execute(_context.Object);

            _factory.Verify(x => x.Create(It.IsAny<CreateContext>()), Times.Never);

            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj =>
                    rj.ContainsKey("NextExecution") && rj["NextExecution"]
                        == JobHelper.SerializeDateTime(_instant.Object.NextInstant.Value))));
        }

        [Fact]
        public void Execute_TakesIntoConsideration_LastExecutionTime_ConvertedToLocalTimezone()
        {
            var time = DateTime.UtcNow;
            _recurringJob["LastExecution"] = JobHelper.SerializeDateTime(time);
            var scheduler = CreateScheduler();

            scheduler.Execute(_context.Object);

            _instant.Verify(x => x.GetNextInstants(time, null));
        }
        
        [Fact]
        public void Execute_DoesNotFail_WhenRecurringJobDoesNotExist()
        {
            _connection.Setup(x => x.GetAllItemsFromSet(It.IsAny<string>()))
                .Returns(new HashSet<string> { "non-existing-job" });
            var scheduler = CreateScheduler();

            // Does not throw
            scheduler.Execute(_context.Object);
        }

        [Fact]
        public void Execute_HandlesJobLoadException()
        {
            // Arrange
            _recurringJob["Job"] =
                JobHelper.ToJson(new InvocationData("SomeType", "SomeMethod", "Parameters", "arguments"));

            var scheduler = CreateScheduler();

            // Act & Assert does not throw
            scheduler.Execute(_context.Object);
        }

        [Fact]
        public void Execute_GetsInstance_InAGivenTimeZone()
        {
            var timeZoneId = PlatformHelper.IsRunningOnWindows() ? "Hawaiian Standard Time" : "Pacific/Honolulu";

            _instantFactory = (schedule, timeZoneInfo) =>
            {
                if (timeZoneInfo.Id != timeZoneId) throw new InvalidOperationException("Invalid timezone");
                return _instant.Object;
            };
            // Arrange
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            _recurringJob["TimeZoneId"] = timeZone.Id;
            var scheduler = CreateScheduler();

            // Act & Assert does not throw
            scheduler.Execute(_context.Object);
        }

        [Fact]
        public void Execute_GetInstance_UseUtcTimeZone_WhenItIsNotProvided()
        {
            // Arrange
            _instantFactory = (schedule, timeZoneInfo) =>
            {
                if (!timeZoneInfo.Equals(TimeZoneInfo.Utc)) throw new InvalidOperationException("Invalid timezone");
                return _instant.Object;
            };
            _recurringJob.Remove("TimeZoneId");
            var scheduler = CreateScheduler();

            // Act & Assert does not throw
            scheduler.Execute(_context.Object);
        }

        [Fact]
        public void Execute_GetInstance_DoesNotCreateAJob_WhenGivenOneIsNotFound()
        {
            _recurringJob["TimeZoneId"] = "Some garbage";
            var scheduler = CreateScheduler();

            scheduler.Execute(_context.Object);

            _factory.Verify(x => x.Create(It.IsAny<CreateContext>()), Times.Never);
        }

        [Fact]
        public void Execute_GetNextInstants_IsCalledWithCreatedAtTime_IfExists()
        {
            // Arrange
            var createdAt = DateTime.UtcNow.AddHours(-3);
            _recurringJob["CreatedAt"] = JobHelper.SerializeDateTime(createdAt);
            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _instant.Verify(x => x.GetNextInstants(createdAt, null), Times.Once);
        }

        [Fact]
        public void Execute_DoesNotFixCreatedAtField_IfItExists()
        {
            // Arrange
            _recurringJob["CreatedAt"] = JobHelper.SerializeDateTime(DateTime.UtcNow);
            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);
            
            // Assert
            _connection.Verify(
                x => x.SetRangeInHash(
                    $"{PluginConstants.JobType}:{RecurringJobId}",
                    It.Is<Dictionary<string, string>>(rj => rj.ContainsKey("CreatedAt"))),
                Times.Never);
        }

        [Fact]
        public void Execute_FixedMissingCreatedAtField()
        {
            // Arrange
            _recurringJob.Remove("CreatedAt");
            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(
                x => x.SetRangeInHash(
                    $"{PluginConstants.JobType}:{RecurringJobId}",
                    It.Is<Dictionary<string, string>>(rj => rj.ContainsKey("CreatedAt"))),
                Times.Once);
        }

        [Fact]
        public void Execute_PassesNextExecutionTime_ToGetNextInstants_WhenBothLastExecutionAndCreatedAtAreNotAvailable()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob.Remove("CreatedAt");
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _instant.Verify(x => x.GetNextInstants(
                It.Is<DateTime>(time => time < nextExecution), null));
        }

        [Fact]
        public void Execute_LowerDateRange_DoesNotTriggerWhenOutOfBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["StartDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(20));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => !rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_LowerDateRange_DoesTriggerWhenInBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["StartDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(5));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_UpperDateRange_DoesNotTriggerWhenOutOfBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["EndDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(-10));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => !rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_UpperDateRange_DoesNotTriggerWhenOutOfBoundsUsingEndDateTimeComponent()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.Date.AddHours(-10);
            _recurringJob[HashKeys.NextExecution] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob[HashKeys.EndDate] = JobHelper.SerializeDateTime(nextExecution.AddHours(-1));
            _recurringJob[HashKeys.UseEndDateTimeComponent] = bool.TrueString;
            _recurringJob.Remove(HashKeys.LastExecution);

            var scheduler = CreateScheduler(true);

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => !rj.ContainsKey(HashKeys.LastExecution))));
        }

        [Fact]
        public void Execute_UpperDateRange_DoesTriggerWhenInBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["EndDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(20));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_DateRange_DoesTriggerWhenInBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["StartDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(-10));
            _recurringJob["EndDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(20));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_DateRange_DoesTriggerWhenInBoundsUsingEndDateTimeComponentWithSingleDayRange()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddMinutes(-10);
            var startDate = nextExecution.Date;
            var endDate = nextExecution.AddMinutes(10);
            _recurringJob[HashKeys.NextExecution] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob[HashKeys.StartDate] = JobHelper.SerializeDateTime(startDate);
            _recurringJob[HashKeys.EndDate] = JobHelper.SerializeDateTime(endDate);
            _recurringJob[HashKeys.UseEndDateTimeComponent] = bool.TrueString;
            _recurringJob.Remove(HashKeys.LastExecution);

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => rj.ContainsKey(HashKeys.LastExecution))));
        }

        [Fact]
        public void Execute_DateRange_DoesNotTriggerWhenOutOfBounds()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddHours(-10);
            _recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob["StartDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(20));
            _recurringJob["EndDate"] = JobHelper.SerializeDateTime(nextExecution.AddHours(25));
            _recurringJob.Remove("LastExecution");

            var scheduler = CreateScheduler();

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => !rj.ContainsKey("LastExecution"))));
        }

        [Fact]
        public void Execute_DateRange_DoesTriggerOnEndDateAfterEndTime_WhenIgnoringTimeComponentsAndNotUsingEndDateTimeComponentAndIgnoring()
        {
            // Arrange
            var nextExecution = DateTime.UtcNow.AddMinutes(-10);
            var startDate = nextExecution.AddMinutes(-10);
            var endDate = nextExecution.AddMinutes(-9);
            _recurringJob[HashKeys.NextExecution] = JobHelper.SerializeDateTime(nextExecution);
            _recurringJob[HashKeys.StartDate] = JobHelper.SerializeDateTime(startDate);
            _recurringJob[HashKeys.EndDate] = JobHelper.SerializeDateTime(endDate);
            _recurringJob[HashKeys.UseEndDateTimeComponent] = bool.FalseString;
            _recurringJob.Remove(HashKeys.LastExecution);

            var scheduler = CreateScheduler(true);

            // Act
            scheduler.Execute(_context.Object);

            // Assert
            _connection.Verify(x => x.SetRangeInHash(
                $"{PluginConstants.JobType}:{RecurringJobId}",
                It.Is<Dictionary<string, string>>(rj => rj.ContainsKey(HashKeys.LastExecution))));
        }

        [Fact]
		public void Execute_DateRange_DoesNotThrowExceptionWhenAtDateTimeMinMaxBounds()
		{
			// Arrange
			var nextExecution = DateTime.UtcNow.AddHours(-10);
			_recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
			_recurringJob["StartDate"] = JobHelper.SerializeDateTime(DateTime.MinValue);
			_recurringJob["EndDate"] = JobHelper.SerializeDateTime(DateTime.MaxValue);
			_recurringJob.Remove("LastExecution");

			var scheduler = CreateScheduler(true);
			
			// Act (if it doesn't throw an exception, we're good!)
			scheduler.Execute(_context.Object);
		}

		[Fact]
		public void Execute_DateRange_DoesNotPukeWhenLastInstantIsOutsideBounds()
		{
			// Arrange
			var currentTime = DateTime.UtcNow;
			_recurringJob["StartDate"] = JobHelper.SerializeDateTime(currentTime.AddDays(-1));
			_recurringJob["EndDate"] = JobHelper.SerializeDateTime(currentTime.AddDays(1));
			_recurringJob["LastExecution"] = JobHelper.SerializeDateTime(currentTime.AddDays(-2));
			_recurringJob["TimeZoneId"] = TimeZoneInfo.Local.Id;

			var scheduler = CreateScheduler(true);

			// Act (if it doesn't throw an exception, we're good!)
			scheduler.Execute(_context.Object);
        }

        //Ensure use of Windows or IANA TimeZone Id is allowed in either environment
        [Fact]
        public void Execute_DateRange_DoesNotPukeWhenTimeZoneIsIana()
        {
            _recurringJob[HashKeys.TimeZoneId] = "America/Detroit";

            var scheduler = CreateScheduler(true);

            // Act (if it doesn't throw an exception, we're good!)
            scheduler.Execute(_context.Object);
        }

        [Fact]
        public void Execute_DateRange_DoesNotPukeWhenTimeZoneIsWindows()
        {
            _recurringJob[HashKeys.TimeZoneId] = "Hawaiian Standard Time";

            var scheduler = CreateScheduler(true);

            // Act (if it doesn't throw an exception, we're good!)
            scheduler.Execute(_context.Object);
        }

        [Fact]
		public void Execute_DateRange_DoesNotThrowExceptionWhenNextInstantIsNull()
		{
			var nullNextInstant = new Mock<IScheduleInstant>();
			nullNextInstant.Setup(x => x.GetNextInstants(It.IsAny<DateTime>(), null)).Returns(new[] { _instant.Object.NowInstant });
			nullNextInstant.Setup(x => x.GetNextInstants(It.IsAny<DateTime>(), It.IsAny<DateTime>())).Returns(new[] { _instant.Object.NowInstant });
			nullNextInstant.Setup(x => x.NowInstant).Returns(DateTime.UtcNow);
			nullNextInstant.Setup(x => x.NextInstant).Returns((DateTime?) null);

			// Arrange
			var nextExecution = DateTime.UtcNow.AddHours(-10);
			_recurringJob["NextExecution"] = JobHelper.SerializeDateTime(nextExecution);
			_recurringJob["StartDate"] = JobHelper.SerializeDateTime(DateTime.MinValue);
			_recurringJob["EndDate"] = JobHelper.SerializeDateTime(DateTime.MaxValue);
			_recurringJob.Remove("LastExecution");

			var scheduler = new RecurringDateRangeJobScheduler(
				_factory.Object,
				(schedule, timeZone) => nullNextInstant.Object,
				_throttler.Object,
				true);

			// Act (if it doesn't throw an exception, we're good!)
			scheduler.Execute(_context.Object);
		}

		private RecurringDateRangeJobScheduler CreateScheduler(bool ignoreTimeComponentInStartEndDates = false)
        {
            return new RecurringDateRangeJobScheduler(
                _factory.Object,
                _instantFactory,
                _throttler.Object,
				ignoreTimeComponentInStartEndDates);
        }
    }
}
