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
using Hangfire.Annotations;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.RecurringDateRange.Constants;
using Hangfire.States;
using Hangfire.Storage;
using NCrontab.Advanced;

namespace Hangfire.RecurringDateRange.Server
{
    public class RecurringDateRangeJobManager
    {
        private readonly JobStorage _storage;
        private readonly IBackgroundJobFactory _factory;

        public RecurringDateRangeJobManager()
            : this(JobStorage.Current)
        {
        }

        public RecurringDateRangeJobManager([NotNull] JobStorage storage)
            : this(storage, new BackgroundJobFactory())
        {
        }

        public RecurringDateRangeJobManager([NotNull] JobStorage storage, [NotNull] IBackgroundJobFactory factory)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _storage = storage;
            _factory = factory;
        }

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, DateTime? startDate, DateTime? endDate, RecurringJobOptions options)
        {
            if (recurringJobId == null) throw new ArgumentNullException(nameof(recurringJobId));
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (cronExpression == null) throw new ArgumentNullException(nameof(cronExpression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            ValidateCronExpression(cronExpression);

            using (var connection = _storage.GetConnection())
            {
                var recurringJob = new Dictionary<string, string>();
                var invocationData = InvocationData.Serialize(job);

                recurringJob["Job"] = JobHelper.ToJson(invocationData);
                recurringJob["Cron"] = cronExpression;
                recurringJob["TimeZoneId"] = options.TimeZone.Id;
                recurringJob["Queue"] = options.QueueName;

                recurringJob["StartDate"] = JobHelper.SerializeDateTime(startDate ?? DateTime.MinValue);
                recurringJob["EndDate"] = JobHelper.SerializeDateTime(endDate ?? DateTime.MaxValue);

                var existingJob = connection.GetAllEntriesFromHash($"{PluginConstants.JobType}:{recurringJobId}");
                if (existingJob == null)
                {
                    recurringJob["CreatedAt"] = JobHelper.SerializeDateTime(DateTime.UtcNow);
                }

                using (var transaction = connection.CreateWriteTransaction())
                {
                    transaction.SetRangeInHash(
                        $"{PluginConstants.JobType}:{recurringJobId}",
                        recurringJob);

                    transaction.AddToSet(PluginConstants.JobSet, recurringJobId);

                    transaction.Commit();
                }
            }
        }

        public void Trigger(string recurringJobId)
        {
            if (recurringJobId == null) throw new ArgumentNullException(nameof(recurringJobId));

            using (var connection = _storage.GetConnection())
            {
                var hash = connection.GetAllEntriesFromHash($"{PluginConstants.JobType}:{recurringJobId}");
                if (hash == null)
                {
                    return;
                }

                var job = JobHelper.FromJson<InvocationData>(hash["Job"]).Deserialize();
                var state = new EnqueuedState { Reason = "Triggered using recurring job manager" };

                if (hash.ContainsKey("Queue"))
                {
                    state.Queue = hash["Queue"];
                }

                var context = new CreateContext(_storage, connection, job, state);
                context.Parameters["RecurringJobId"] = recurringJobId;
                _factory.Create(context);
            }
        }

        public void RemoveIfExists(string recurringJobId)
        {
            if (recurringJobId == null) throw new ArgumentNullException(nameof(recurringJobId));

            using (var connection = _storage.GetConnection())
            using (var transaction = connection.CreateWriteTransaction())
            {
                transaction.RemoveHash($"{PluginConstants.JobType}:{recurringJobId}");
                transaction.RemoveFromSet(PluginConstants.JobSet, recurringJobId);

                transaction.Commit();
            }
        }

        private static void ValidateCronExpression(string cronExpression)
        {
            try
            {
                var schedule = CrontabSchedule.Parse(cronExpression);
                schedule.GetNextOccurrence(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("CRON expression is invalid. Please see the inner exception for details.", nameof(cronExpression), ex);
            }
        }
    }
}
