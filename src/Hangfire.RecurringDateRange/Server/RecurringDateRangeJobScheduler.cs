// Portions of this codebase have been copied/modified from the Hangfire project

// Hangfire.RecurringDateRange is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as 
// published by the Free Software Foundation, either version 3 
// of the License, or any later version.
// 
// Hangfire.RecurringDateRange is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public 
// License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Annotations;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.RecurringDateRange.Constants;
using Hangfire.RecurringDateRange.Contracts;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using NCrontab.Advanced;
using NCrontab.Advanced.Enumerations;

namespace Hangfire.RecurringDateRange.Server
{
    public class RecurringDateRangeJobScheduler : IBackgroundProcess
    {
        private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(1);
        private static readonly ILog Logger = LogProvider.For<RecurringJobScheduler>();

        private readonly IBackgroundJobFactory _factory;
        private readonly Func<CrontabSchedule, TimeZoneInfo, IScheduleInstant> _instantFactory;
        private readonly IThrottler _throttler;

        private CronStringFormat _cronStringFormat;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringDateRangeJobScheduler"/>
        /// class with default background job factory.
        /// </summary>
        public RecurringDateRangeJobScheduler(CronStringFormat cronStringFormat = CronStringFormat.Default)
            : this(new BackgroundJobFactory())
        {
            _cronStringFormat = cronStringFormat;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringDateRangeJobScheduler"/>
        /// class with custom background job factory.
        /// </summary>
        /// <param name="factory">Factory that will be used to create background jobs.</param>
        /// 
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        public RecurringDateRangeJobScheduler([NotNull] IBackgroundJobFactory factory)
            : this(factory, ScheduleInstant.Factory, new EveryMinuteThrottler())
        {
        }

        internal RecurringDateRangeJobScheduler(
            [NotNull] IBackgroundJobFactory factory,
            [NotNull] Func<CrontabSchedule, TimeZoneInfo, IScheduleInstant> instantFactory,
            [NotNull] IThrottler throttler)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (instantFactory == null) throw new ArgumentNullException(nameof(instantFactory));
            if (throttler == null) throw new ArgumentNullException(nameof(throttler));

            _factory = factory;
            _instantFactory = instantFactory;
            _throttler = throttler;
        }

        /// <inheritdoc />
        public void Execute(BackgroundProcessContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _throttler.Throttle(context.CancellationToken);

            using (var connection = context.Storage.GetConnection())
            using (connection.AcquireDistributedLock($"{PluginConstants.JobSet}:lock", LockTimeout))
            {
                var recurringJobIds = connection.GetAllItemsFromSet(PluginConstants.JobSet);

                foreach (var recurringJobId in recurringJobIds)
                {
                    var recurringJob = connection.GetAllEntriesFromHash(
                        $"{PluginConstants.JobType}:{recurringJobId}");

                    if (recurringJob == null)
                    {
                        continue;
                    }

                    try
                    {
                        TryScheduleJob(context.Storage, connection, recurringJobId, recurringJob);
                    }
                    catch (JobLoadException ex)
                    {
                        Logger.WarnException(
                            $"Recurring job '{recurringJobId}' can not be scheduled due to job load exception.",
                            ex);
                    }
                }
            }

            // The code above may be completed in less than a second. Default throttler use
            // the second resolution, and without an extra delay, CPU and DB bursts may happen.
            _throttler.Delay(context.CancellationToken);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return GetType().Name;
        }

        private void TryScheduleJob(
            JobStorage storage,
            IStorageConnection connection,
            string recurringJobId,
            IReadOnlyDictionary<string, string> recurringJob)
        {
            var serializedJob = JobHelper.FromJson<InvocationData>(recurringJob["Job"]);
            var job = serializedJob.Deserialize();
            var cron = recurringJob["Cron"];
            var cronSchedule = CrontabSchedule.Parse(cron, _cronStringFormat);
            
            var startDate = JobHelper.DeserializeNullableDateTime(recurringJob["StartDate"]);
            var endDate = JobHelper.DeserializeNullableDateTime(recurringJob["EndDate"]);

            try
            {
                var timeZone = recurringJob.ContainsKey("TimeZoneId")
                    ? TimeZoneInfo.FindSystemTimeZoneById(recurringJob["TimeZoneId"])
                    : TimeZoneInfo.Utc;

                var nowInstant = _instantFactory(cronSchedule, timeZone);
                var changedFields = new Dictionary<string, string>();

                var lastInstant = GetLastInstant(recurringJob, nowInstant);

                if (WithinDateRange(nowInstant, startDate, endDate, timeZone) && nowInstant.GetNextInstants(lastInstant, endDate).Any())
                {
                    var state = new EnqueuedState { Reason = "Triggered by recurring job scheduler" };
                    if (recurringJob.ContainsKey("Queue") && !String.IsNullOrEmpty(recurringJob["Queue"]))
                    {
                        state.Queue = recurringJob["Queue"];
                    }

                    var context = new CreateContext(storage, connection, job, state);
                    context.Parameters["RecurringJobId"] = recurringJobId;

                    var backgroundJob = _factory.Create(context);
                    var jobId = backgroundJob?.Id;

                    if (String.IsNullOrEmpty(jobId))
                    {
                        Logger.Debug($"Recurring job '{recurringJobId}' execution at '{nowInstant.NowInstant}' has been canceled.");
                    }

                    changedFields.Add("LastExecution", JobHelper.SerializeDateTime(nowInstant.NowInstant));
                    changedFields.Add("LastJobId", jobId ?? String.Empty);
                }

                // Fixing old recurring jobs that doesn't have the CreatedAt field
                if (!recurringJob.ContainsKey("CreatedAt"))
                {
                    changedFields.Add("CreatedAt", JobHelper.SerializeDateTime(nowInstant.NowInstant));
                }

                changedFields.Add("NextExecution", nowInstant.NextInstant.HasValue ? JobHelper.SerializeDateTime(nowInstant.NextInstant.Value) : null);

                connection.SetRangeInHash(
                    $"{PluginConstants.JobType}:{recurringJobId}",
                    changedFields);
            }
#if NETFULL
            catch (TimeZoneNotFoundException ex)
            {
#else
            catch (Exception ex)
            {
                // https://github.com/dotnet/corefx/issues/7552
                if (!ex.GetType().Name.Equals("TimeZoneNotFoundException")) throw;
#endif

                Logger.ErrorException(
                    $"Recurring job '{recurringJobId}' was not triggered: {ex.Message}.",
                    ex);
            }

        }

        private static bool WithinDateRange(IScheduleInstant nowInstant, DateTime? startDate, DateTime? endDate, TimeZoneInfo timeZone)
        {
            var startDateForZone = startDate == null ? (DateTime?) null : TimeZoneInfo.ConvertTime(startDate.Value, TimeZoneInfo.Utc, timeZone);
            var endDateForZone = endDate == null ? (DateTime?) null : TimeZoneInfo.ConvertTime(endDate.Value, TimeZoneInfo.Utc, timeZone);
            var nowInstantForZone = TimeZoneInfo.ConvertTime(nowInstant.NowInstant, TimeZoneInfo.Utc, timeZone);

            return (startDateForZone == null || startDateForZone <= nowInstantForZone) && (endDateForZone == null || endDateForZone > nowInstantForZone);
        }

        private static DateTime GetLastInstant(IReadOnlyDictionary<string, string> recurringJob, IScheduleInstant instant)
        {
            DateTime lastInstant;

            if (recurringJob.ContainsKey("LastExecution"))
            {
                lastInstant = JobHelper.DeserializeDateTime(recurringJob["LastExecution"]);
            }
            else if (recurringJob.ContainsKey("CreatedAt"))
            {
                lastInstant = JobHelper.DeserializeDateTime(recurringJob["CreatedAt"]);
            }
            else if (recurringJob.ContainsKey("NextExecution"))
            {
                lastInstant = JobHelper.DeserializeDateTime(recurringJob["NextExecution"]);
                lastInstant = lastInstant.AddSeconds(-1);
            }
            else
            {
                lastInstant = instant.NowInstant.AddSeconds(-1);
            }

            return lastInstant;
        }
    }
}
