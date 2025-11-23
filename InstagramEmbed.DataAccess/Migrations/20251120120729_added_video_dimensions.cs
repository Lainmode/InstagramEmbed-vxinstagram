using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InstagramEmbed.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class added_video_dimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Posts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Posts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Height",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Posts");
        }
    }
}
