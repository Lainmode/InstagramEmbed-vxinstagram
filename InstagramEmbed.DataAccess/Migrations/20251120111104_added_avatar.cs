using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InstagramEmbed.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class added_avatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Posts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresOn",
                table: "Posts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                table: "Posts");
        }
    }
}
