using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropTracker.Migrations
{
    /// <inheritdoc />
    public partial class Addresulttracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Result",
                table: "Props",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "HitAt",
                table: "Parlays",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Result",
                table: "Parlays",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Result",
                table: "Props");

            migrationBuilder.DropColumn(
                name: "HitAt",
                table: "Parlays");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "Parlays");
        }
    }
}
