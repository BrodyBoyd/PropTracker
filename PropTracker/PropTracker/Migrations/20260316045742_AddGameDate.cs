using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddGameDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GameDate",
                table: "Props",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameDate",
                table: "Props");
        }
    }
}
