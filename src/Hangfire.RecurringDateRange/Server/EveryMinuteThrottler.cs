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
using System.Threading;
using Hangfire.RecurringDateRange.Contracts;

namespace Hangfire.RecurringDateRange.Server
{
    internal class EveryMinuteThrottler : IThrottler
    {
        public void Throttle(CancellationToken token)
        {
            while (DateTime.Now.Second != 0)
            {
                WaitASecondOrThrowIfCanceled(token);
            }
        }

        public void Delay(CancellationToken token)
        {
            WaitASecondOrThrowIfCanceled(token);
        }

        private static void WaitASecondOrThrowIfCanceled(CancellationToken token)
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            token.ThrowIfCancellationRequested();
        }
    }
}
