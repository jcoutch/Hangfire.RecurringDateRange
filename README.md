# Hangfire.RecurringDateRange
Job Scheduler/Background processor for recurring jobs within a specified date range.  Jobs are stored under a new `recurring-daterange-job` type, so they won't conflict with any jobs created with the existing `recurring-job` type.

|Branch|Build Status|
|------|------------|
|Master|[![Build status](https://ci.appveyor.com/api/projects/status/o0vhstcjde04iaeh/branch/master?svg=true)](https://ci.appveyor.com/project/jcoutch/hangfire-recurringdaterange/branch/master)|

# Installation
Run the following from Package Manager Console:
```
  Install-Package Hangfire.RecurringDateRange
```

Also, during development/testing, I will frequently publish CI builds to Nuget as pre-release builds (so I can test/debug from my own projects that use Hangfire.RecurringDateRange.)  If you want to use these **un-tested builds at your own risk**, run the following command:
```
  Install-Package -IncludePrerelease Hangfire.RecurringDateRange
```


# Usage
To use, pass in a new instance of `RecurringDateRangeJobScheduler()` into `UseHangfireServer`:
```
  var backgroundServerJobOptions = new BackgroundJobServerOptions();
  appBuilder.UseHangfireServer(backgroundServerJobOptions, new RecurringDateRangeJobScheduler());
```

This library also uses [`NCrontab.Advanced`](https://github.com/jcoutch/NCrontab-Advanced) for cron expression parsing.  You can specify the format of your cron expressions via the `cronStringFormat` parameter:
```
  appBuilder.UseHangfireServer(backgroundServerJobOptions, new RecurringDateRangeJobScheduler(CronStringFormat.WithSecondsAndYears));
``` 

By default, `RecurringDateRangeJobScheduler` will use the time component of the start/end dates.  If you want to ignore them, you can pass `true`/`false` as the 2nd parameter to the constructor (`false` is default):
```
  // Ingore the time component on start/end dates
  appBuilder.UseHangfireServer(backgroundServerJobOptions, new RecurringDateRangeJobScheduler(CronStringFormat.WithSecondsAndYears, true));
``` 

Then, instead of using the `RecurringJob` static methods, use the `RecurringDateRangeJob` static methods to create/remove jobs:

```
  // Runs a job every second between the 1st and 27th of January 2017
  RecurringDateRangeJob.AddOrUpdate("my-date-range-job",
      () => Console.WriteLine("Awesomesauce!"), "* * * * *",
      startDate: DateTime.Parse("2017-01-01"),
      endDate: DateTime.Parse("2017-01-27")
  );

  RecurringDateRangeJob.RemoveIfExists("my-date-range-job")
```
