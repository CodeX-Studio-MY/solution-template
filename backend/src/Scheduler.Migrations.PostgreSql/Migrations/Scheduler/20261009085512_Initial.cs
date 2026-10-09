using Microsoft.EntityFrameworkCore.Migrations;
using Acme.Scheduler;

#nullable disable

namespace Acme.Scheduler.Migrations.PostgreSql.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // Quartz's tables are defined once (QuartzSchema), for both providers; this is the only place they are created.
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS " + SchedulerStoreDbContext.SchemaName);
            foreach (var statement in QuartzSchema.CreateStatements(postgres: true))
                migrationBuilder.Sql(statement);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            foreach (var statement in QuartzSchema.DropStatements(postgres: true))
                migrationBuilder.Sql(statement);

        }
    }
}
