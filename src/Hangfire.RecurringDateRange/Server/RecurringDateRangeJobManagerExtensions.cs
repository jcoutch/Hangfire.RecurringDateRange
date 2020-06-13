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
using Hangfire.Annotations;
using Hangfire.Common;
using Hangfire.States;

namespace Hangfire.RecurringDateRange.Server
{
    public static class RecurringDateTimeJobManagerExtensions
    {
        public static void AddOrUpdate(
            [NotNull] this RecurringDateRangeJobManager manager,
            [NotNull] string recurringJobId,
            [NotNull] Job job,
            [NotNull] string cronExpression,
            DateTime? startDateTime = null,
            DateTime? endDateTime = null)
        {
            AddOrUpdate(manager, recurringJobId, job, cronExpression, TimeZoneInfo.Utc, startDateTime, endDateTime);
        }

        public static void AddOrUpdate(
            [NotNull] this RecurringDateRangeJobManager manager,
            [NotNull] string recurringJobId,
            [NotNull] Job job,
            [NotNull] string cronExpression,
            [NotNull] TimeZoneInfo timeZone,
            DateTime? startDateTime = null,
            DateTime? endDateTime = null)
        {
            AddOrUpdate(manager, recurringJobId, job, cronExpression, timeZone, EnqueuedState.DefaultQueue, startDateTime, endDateTime);
        }

        public static void AddOrUpdate(
            [NotNull] this RecurringDateRangeJobManager manager,
            [NotNull] string recurringJobId,
            [NotNull] Job job,
            [NotNull] string cronExpression,
            [NotNull] TimeZoneInfo timeZone,
            [NotNull] string queue,
            DateTime? startDateTime = null,
            DateTime? endDateTime = null)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (timeZone == null) throw new ArgumentNullException(nameof(timeZone));
            if (queue == null) throw new ArgumentNullException(nameof(queue));

            manager.AddOrUpdate(
                recurringJobId,
                job,
                cronExpression,
                startDateTime,
                endDateTime,
                new RecurringJobOptions { QueueName = queue, TimeZone = timeZone });
        }

        public static void AddOrUpdate(
            [NotNull] this RecurringDateRangeJobManager manager,
            string recurringJobId,
            Job job,
            string cronExpression,
            DateTime? startDate,
            DateTime? endDate,
            RecurringJobOptions options)
        {
            if (recurringJobId == null) throw new ArgumentNullException(nameof(recurringJobId));
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (cronExpression == null) throw new ArgumentNullException(nameof(cronExpression));
            if (options == null) throw new ArgumentNullException(nameof(options));

            manager.AddOrUpdate(
                new RecurringDateRangeJobOptions()
                {
                    RecurringJobId = recurringJobId,
                    Job = job,
                    CronExpression = cronExpression,
                    StartDateTime = startDate,
                    EndDateTime = endDate,
                    RecurringJobOptions = options
                });
        }
    }
}