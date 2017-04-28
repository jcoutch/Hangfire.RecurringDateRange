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
using Hangfire.RecurringDateRange.Contracts;
using Hangfire.Server;
using NCrontab.Advanced;

namespace Hangfire.RecurringDateRange.Server
{
    internal class ScheduleInstant : IScheduleInstant
    {
        private readonly TimeZoneInfo _timeZone;
        private readonly CrontabSchedule _schedule;

        public static Func<CrontabSchedule, TimeZoneInfo, IScheduleInstant> Factory =
            (schedule, timeZone) => new ScheduleInstant(DateTime.UtcNow, timeZone, schedule);

        public ScheduleInstant(DateTime nowInstant, TimeZoneInfo timeZone, [NotNull] CrontabSchedule schedule, DateTime? endDate = null)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (nowInstant.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Only DateTime values in UTC should be passed.", nameof(nowInstant));
            }

            _timeZone = timeZone;
            _schedule = schedule;

            NowInstant = nowInstant.AddSeconds(-nowInstant.Second);

	        var occurenceEndDate = endDate ?? DateTime.MaxValue;

			var nextOccurrences = _schedule.GetNextOccurrences(TimeZoneInfo.ConvertTime(NowInstant, TimeZoneInfo.Utc, _timeZone), occurenceEndDate)
				.Where(x => x != occurenceEndDate); // Explicitly exclude the end date

            foreach (var nextOccurrence in nextOccurrences)
            {
                if (_timeZone.IsInvalidTime(nextOccurrence)) continue;

                NextInstant = TimeZoneInfo.ConvertTime(nextOccurrence, _timeZone, TimeZoneInfo.Utc);
                break;
            }
        }

        public DateTime NowInstant { get; }
        public DateTime? NextInstant { get; }

        public IEnumerable<DateTime> GetNextInstants(DateTime lastInstant, DateTime? endDateTime = null)
        {
            endDateTime = endDateTime ?? DateTime.MaxValue;

            if (lastInstant.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Only DateTime values in UTC should be passed.", nameof(lastInstant));
            }

            var endDateTimeForZone = TimeZoneInfo.ConvertTime(endDateTime.Value, TimeZoneInfo.Utc, _timeZone);

            return _schedule
                .GetNextOccurrences(
                    TimeZoneInfo.ConvertTime(lastInstant, TimeZoneInfo.Utc, _timeZone),
                    TimeZoneInfo.ConvertTime(NowInstant.AddSeconds(1), TimeZoneInfo.Utc, _timeZone))
                .Where(x => x < endDateTimeForZone && !_timeZone.IsInvalidTime(x))
                .Select(x => TimeZoneInfo.ConvertTime(x, _timeZone, TimeZoneInfo.Utc))
                .ToList();
        }
    }
}