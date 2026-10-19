using Microsoft.EntityFrameworkCore.Migrations;
using Saaskit.Scheduler;

#nullable disable

namespace Saaskit.Scheduler.Migrations.SqlServer.Migrations.Scheduler
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // Quartz's tables are defined once (QuartzSchema), for both providers; this is the only place they are created.
            migrationBuilder.Sql($"IF SCHEMA_ID('{SchedulerStoreDbContext.SchemaName}') IS NULL EXEC('CREATE SCHEMA [{SchedulerStoreDbContext.SchemaName}]')");
            foreach (var statement in QuartzSchema.CreateStatements(postgres: false))
                migrationBuilder.Sql(statement);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            foreach (var statement in QuartzSchema.DropStatements(postgres: false))
                migrationBuilder.Sql(statement);

        }
    }
}
