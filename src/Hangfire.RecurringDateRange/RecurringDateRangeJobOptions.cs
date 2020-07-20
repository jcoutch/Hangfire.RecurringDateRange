using System;
using System.Linq.Expressions;
using Hangfire.Common;

namespace Hangfire
{
    public class RecurringDateRangeJobOptions
    {
        private const string NullReferenceExceptionFormat = "{0} cannot be null";
        
        public string RecurringJobId {get; set;}
        public Job Job {get; set;}
        public string CronExpression {get; set;}
        public DateTime? StartDateTime = null;
        public DateTime? EndDateTime = null;
        public bool? UseEndDateTimeComponent = null;
        public RecurringJobOptions RecurringJobOptions { get; set; }

        public RecurringDateRangeJobOptions()
        {
            
        }

        public RecurringDateRangeJobOptions(Expression<Action> methodCall)
        {
            if (methodCall == null)
            {
                throw new ArgumentNullException(nameof(methodCall));
            }
            Job = Job.FromExpression(methodCall);
        }

        public void Validate()
        {
            if (RecurringJobId == null)
            {
                throw new NullReferenceException(string.Format(NullReferenceExceptionFormat, nameof(RecurringJobId)));
            }

            if (Job == null)
            {
                throw new NullReferenceException(string.Format(NullReferenceExceptionFormat, nameof(Job)));
            }

            if (CronExpression == null)
            {
                throw new NullReferenceException(string.Format(NullReferenceExceptionFormat, nameof(CronExpression)));
            }

            if (RecurringJobOptions == null)
            {
                throw new NullReferenceException(string.Format(NullReferenceExceptionFormat, nameof(RecurringJobOptions)));
            }
        }
    }
}
