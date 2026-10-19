using Saaskit.Core.Configuration;
using Saaskit.Core.Data.PostgreSql;
using Quartz;

namespace Saaskit.Scheduler;

public static class SchedulerRegistration
{
    /// <summary>
    /// Clustered Quartz.NET with its job store in the catalog database: several Scheduler instances share one set of jobs, and each
    /// trigger fires on exactly one of them. A crashed instance's work is picked up by another.
    /// </summary>
    public static IServiceCollection AddPlatformScheduler(this IServiceCollection services, DatabaseOptions database)
    {
        var postgres = database.Provider == PostgreSqlDatabaseProvider.ProviderName;
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(scheduler =>
            {
                scheduler.InstanceName = $"{Saaskit.Core.Platform.PlatformInfo.Product}-scheduler";
                scheduler.GenerateInstanceId = true;
            });
            quartz.UsePersistentStore(store =>
            {
                store.ConfigureStore(options => options.TablePrefix = QuartzSchema.TablePrefix);
                store.UseSystemTextJsonSerializer();
                store.UseClustering(cluster => cluster.CheckinInterval = TimeSpan.FromSeconds(10));
                if (postgres)
                    store.UsePostgres(data => data.ConnectionString = database.CatalogConnectionString);
                else
                    store.UseSqlServer(data => data.ConnectionString = database.CatalogConnectionString);
            });

            // Every 15 minutes on the quarter hour (UTC), which lines up with every time zone's 15-minute boundaries.
            // A missed firing is skipped, not replayed: the job only acts inside a 15-minute window, so a late replay would be wrong.
            var job = new JobKey(TenantDayJob.Name);
            quartz.AddJob<TenantDayJob>(configure => configure.WithIdentity(job).StoreDurably().WithDescription("Tells each tenant's modules that its day started (09:00 there)"));
            quartz.AddTrigger(trigger => trigger
                .ForJob(job)
                .WithIdentity($"{TenantDayJob.Name}-tick")
                .WithCronSchedule("0 0/15 * * * ?", schedule => schedule.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        return services;
    }
}
